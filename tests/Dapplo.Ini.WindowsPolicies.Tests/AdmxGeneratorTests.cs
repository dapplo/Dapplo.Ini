// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Schema;
using Dapplo.Ini.Interfaces;
using Dapplo.Ini.WindowsPolicies.Admx;

namespace Dapplo.Ini.WindowsPolicies.Tests;

/// <summary>The ADMX / ADML generator: schema, references and the mapping of the types.</summary>
public sealed class AdmxGeneratorTests
{
    private static readonly XNamespace Ns = AdmxGenerator.PolicyDefinitionsNamespace;

    private static AdministrativeTemplate Generate(Action<AdmxOptions>? configure = null)
    {
        var options = new AdmxOptions("Dapplo", "Policy Test") { SupportedOn = "Policy Test 2.0 or later" };
        configure?.Invoke(options);
        var core = new CoreSettingsImpl();
        core.ResetToDefaults();
        var hidden = new HiddenSettingsImpl();
        hidden.ResetToDefaults();
        return AdmxGenerator.Generate(new IIniSection[] { core, hidden }, options);
    }

    private static XElement Policy(AdministrativeTemplate template, string name)
        => template.Admx.Descendants(Ns + "policy").Single(p => (string)p.Attribute("name")! == name);

    private static string Text(AdministrativeTemplate template, string id, string language = "en-US")
        => template.Adml[language].Descendants(Ns + "string").Single(s => (string)s.Attribute("id")! == id).Value;

    [Fact]
    public void AdmxAndAdml_AreValidAgainstTheSchema()
    {
        var template = Generate(o => o.Languages = new List<string> { "en-US", "de-DE" });
        var schemas = new XmlSchemaSet();
        using (var stream = typeof(AdmxGeneratorTests).Assembly.GetManifestResourceStream(
                   typeof(AdmxGeneratorTests).Assembly.GetManifestResourceNames().Single(n => n.EndsWith("PolicyDefinitionsSubset.xsd", StringComparison.Ordinal)))!)
        using (var reader = XmlReader.Create(stream))
            schemas.Add(null, reader);

        var errors = new List<string>();
        // Validate the text as written, not only the in-memory tree
        XDocument.Parse(template.AdmxText).Validate(schemas, (_, e) => errors.Add("admx: " + e.Message));
        foreach (var language in template.Adml.Keys)
            XDocument.Parse(template.GetAdmlText(language)).Validate(schemas, (_, e) => errors.Add($"adml {language}: {e.Message}"));
        Assert.Empty(errors);
    }

    [Fact]
    public void EveryReference_IsDefined()
    {
        var template = Generate();
        var admx = template.AdmxText;
        var strings = template.Adml["en-US"].Descendants(Ns + "string").Select(s => (string)s.Attribute("id")!).ToList();
        Assert.Equal(strings.Count, strings.Distinct(StringComparer.Ordinal).Count());
        foreach (Match match in Regex.Matches(admx, @"\$\(string\.([A-Za-z0-9_]+)\)"))
            Assert.Contains(match.Groups[1].Value, strings);

        var presentations = template.Adml["en-US"].Descendants(Ns + "presentation").Select(p => (string)p.Attribute("id")!).ToList();
        foreach (Match match in Regex.Matches(admx, @"\$\(presentation\.([A-Za-z0-9_]+)\)"))
            Assert.Contains(match.Groups[1].Value, presentations);

        var categories = template.Admx.Descendants(Ns + "category").Select(c => (string)c.Attribute("name")!).ToList();
        Assert.Equal(categories.Count, categories.Distinct().Count());
        foreach (var parent in template.Admx.Descendants(Ns + "parentCategory"))
            Assert.Contains((string)parent.Attribute("ref")!, categories);

        var supportedOn = template.Admx.Descendants(Ns + "definition").Select(d => (string)d.Attribute("name")!).ToList();
        foreach (var reference in template.Admx.Descendants(Ns + "policy").Elements(Ns + "supportedOn"))
            Assert.Contains((string)reference.Attribute("ref")!, supportedOn);

        // presentation elements refer to the elements of their policy
        foreach (var policy in template.Admx.Descendants(Ns + "policy").Where(p => p.Attribute("presentation") != null))
        {
            var id = ((string)policy.Attribute("presentation")!).Replace("$(presentation.", "").TrimEnd(')');
            var presentation = template.Adml["en-US"].Descendants(Ns + "presentation").Single(p => (string)p.Attribute("id")! == id);
            var elementIds = policy.Element(Ns + "elements")!.Elements().Select(e => (string)e.Attribute("id")!).ToList();
            foreach (var control in presentation.Elements())
                Assert.Contains((string)control.Attribute("refId")!, elementIds);
        }

        // no reference to another namespace (Windows supportedOn), so it imports into Intune on its own
        Assert.Empty(template.Admx.Descendants(Ns + "using"));
        Assert.DoesNotContain("windows:", admx);
    }

    [Fact]
    public void Header_NamespacePrefixAndFiles()
    {
        var template = Generate(o => o.Revision = "2.3");
        Assert.Equal("policy_test", template.FileName);
        var target = template.Admx.Descendants(Ns + "target").Single();
        Assert.Equal("policy_test", (string)target.Attribute("prefix")!);
        Assert.Equal("Dapplo.Policies.Policy_Test", (string)target.Attribute("namespace")!);
        Assert.Equal("2.3", (string)template.Admx.Root!.Attribute("revision")!);
        Assert.Equal("Policy Test 2.0 or later", Text(template, "SUPPORTED_Product"));

        var custom = Generate(o => { o.Prefix = "pt"; o.Namespace = "Dapplo.PT"; });
        Assert.Equal("pt", custom.FileName);
        Assert.Equal("Dapplo.PT", (string)custom.Admx.Descendants(Ns + "target").Single().Attribute("namespace")!);

        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            var files = template.WriteTo(dir);
            Assert.Equal(new[] { Path.Combine(dir, "policy_test.admx"), Path.Combine(dir, "en-US", "policy_test.adml") }, files);
            Assert.All(files, f => Assert.True(File.Exists(f)));
            XDocument.Load(files[0]);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Bool_IsAPolicyWithEnabledAndDisabledValue()
    {
        var policy = Policy(Generate(), "Core_CheckForUpdates");
        Assert.Equal("Both", (string)policy.Attribute("class")!);
        Assert.Equal(@"SOFTWARE\Policies\Dapplo\Policy Test\Core", (string)policy.Attribute("key")!);
        Assert.Equal("CheckForUpdates", (string)policy.Attribute("valueName")!);
        Assert.Equal("1", (string)policy.Element(Ns + "enabledValue")!.Element(Ns + "decimal")!.Attribute("value")!);
        Assert.Equal("0", (string)policy.Element(Ns + "disabledValue")!.Element(Ns + "decimal")!.Attribute("value")!);
        Assert.Null(policy.Attribute("presentation"));
    }

    [Fact]
    public void Integers_AreDecimalsWithTheRange()
    {
        var template = Generate();
        var retries = Policy(template, "Core_MaxRetries").Element(Ns + "elements")!.Element(Ns + "decimal")!;
        Assert.Equal("1", (string)retries.Attribute("minValue")!);
        Assert.Equal("10", (string)retries.Attribute("maxValue")!);
        var box = template.Adml["en-US"].Descendants(Ns + "decimalTextBox").First(b => (string)b.Attribute("refId")! == "MaxRetries");
        Assert.Equal("5", (string)box.Attribute("defaultValue")!);

        var flags = Policy(template, "Core_Flags").Element(Ns + "elements")!.Element(Ns + "decimal")!;
        Assert.Equal("4294967295", (string)flags.Attribute("maxValue")!);
        var big = Policy(template, "Core_BigNumber").Element(Ns + "elements")!.Element(Ns + "longDecimal")!;
        Assert.Equal(long.MaxValue.ToString(), (string)big.Attribute("maxValue")!);
        // A negative range can't be a decimal (unsigned): text
        Assert.NotNull(Policy(template, "Core_Offset").Element(Ns + "elements")!.Element(Ns + "text"));
    }

    [Fact]
    public void Enum_HasAnItemPerName()
    {
        var template = Generate();
        var element = Policy(template, "Core_OutputFileFormat").Element(Ns + "elements")!.Element(Ns + "enum")!;
        Assert.Equal(new[] { "Png", "Jpg", "Bmp" }, element.Elements(Ns + "item").Select(i => i.Element(Ns + "value")!.Element(Ns + "string")!.Value));
        Assert.Equal("Bitmap (uncompressed)", Text(template, "Core_OutputFileFormat_Bmp"));
        Assert.Equal("Png", Text(template, "Core_OutputFileFormat_Png"));
        var dropdown = template.Adml["en-US"].Descendants(Ns + "dropdownList").First();
        Assert.Equal("0", (string)dropdown.Attribute("defaultItem")!);
    }

    [Fact]
    public void Strings_ListsAndDictionaries()
    {
        var template = Generate();
        var name = Policy(template, "Core_AppName").Element(Ns + "elements")!.Element(Ns + "text")!;
        Assert.Null(name.Attribute("expandable"));
        var path = Policy(template, "Core_OutputPath").Element(Ns + "elements")!.Element(Ns + "text")!;
        Assert.Equal("true", (string)path.Attribute("expandable")!);
        Assert.NotNull(Policy(template, "Core_Threshold").Element(Ns + "elements")!.Element(Ns + "text"));

        var list = Policy(template, "Core_Plugins").Element(Ns + "elements")!.Element(Ns + "list")!;
        Assert.Equal(@"SOFTWARE\Policies\Dapplo\Policy Test\Core\Plugins", (string)list.Attribute("key")!);
        Assert.Equal("", (string)list.Attribute("valuePrefix")!);
        Assert.Null(list.Attribute("explicitValue"));

        var dictionary = Policy(template, "Core_Headers").Element(Ns + "elements")!.Element(Ns + "list")!;
        Assert.Equal("true", (string)dictionary.Attribute("explicitValue")!);
        Assert.Null(dictionary.Attribute("valuePrefix"));
    }

    [Fact]
    public void Skipped_SecretsNotAdministrableRuntimeOnlyAndIgnoreConstants()
    {
        var template = Generate(o => o.Include = info => info.Key != "Flags");
        var names = template.Admx.Descendants(Ns + "policy").Select(p => (string)p.Attribute("name")!).ToList();
        Assert.DoesNotContain("Core_ApiToken", names);
        Assert.DoesNotContain("Core_LastUsedFolder", names);
        Assert.DoesNotContain("Core_SessionCount", names);
        Assert.DoesNotContain("Core_Flags", names);
        Assert.DoesNotContain(names, n => n.StartsWith("Hidden_", StringComparison.Ordinal));
        // IgnoreConstants: no mandatory policy, but a recommended one
        Assert.DoesNotContain("Core_Nickname", names);
        Assert.Contains("Core_Nickname_Recommended", names);
    }

    [Fact]
    public void Categories_ProductSectionsRecommendedAndExtra()
    {
        var template = Generate();
        string Parent(string category) => (string)template.Admx.Descendants(Ns + "category")
            .Single(c => (string)c.Attribute("name")! == category).Element(Ns + "parentCategory")!.Attribute("ref")!;
        string PolicyCategory(string policy) => (string)Policy(template, policy).Element(Ns + "parentCategory")!.Attribute("ref")!;

        Assert.Equal("Dapplo", Parent("Policy_Test"));
        Assert.Equal("Policy_Test", Parent("Policy_Test_Core"));
        Assert.Equal("Dapplo", Parent("Policy_Test_Recommended"));
        Assert.Equal("Policy_Test_Recommended", Parent("Policy_Test_Recommended_Core"));
        Assert.Equal("Policy_Test_Core", PolicyCategory("Core_MaxRetries"));
        Assert.Equal("Policy_Test_Recommended_Core", PolicyCategory("Core_MaxRetries_Recommended"));
        Assert.Equal("Policy Test - Default Settings (users can override)", Text(template, "Cat_Policy_Test_Recommended"));
        Assert.Equal("Core settings", Text(template, "Cat_Policy_Test_Core_Explain"));

        // [PolicyCategory("Common")]
        Assert.Equal("Policy_Test_Common", PolicyCategory("Core_Language"));
        Assert.Equal("Policy_Test", Parent("Policy_Test_Common"));
        Assert.Equal("Language of the user interface", Text(template, "Core_Language"));

        var recommended = Policy(template, "Core_MaxRetries_Recommended");
        Assert.Equal(@"SOFTWARE\Policies\Dapplo\Policy Test\Recommended\Core", (string)recommended.Attribute("key")!);

        var noRecommended = Generate(o => o.IncludeRecommended = false);
        Assert.DoesNotContain(noRecommended.Admx.Descendants(Ns + "policy"), p => ((string)p.Attribute("name")!).EndsWith("_Recommended", StringComparison.Ordinal));
    }

    [Fact]
    public void ExplainText_DisplayNameAndLocalization()
    {
        var template = Generate(o =>
        {
            o.Languages = new List<string> { "en-US", "de-DE" };
            o.Localize = (language, id, text) => language == "de-DE" && id == "Core_MaxRetries" ? "Maximale Wiederholungen" : null;
        });
        Assert.Equal("Max retries", Text(template, "Core_MaxRetries"));
        Assert.Equal("Output file format", Text(template, "Core_OutputFileFormat"));
        Assert.Equal("Maximale Wiederholungen", Text(template, "Core_MaxRetries", "de-DE"));
        var explain = Text(template, "Core_MaxRetries_Explain");
        Assert.StartsWith("How often to retry", explain);
        Assert.Contains("Default: 5", explain);
        Assert.Contains(@"SOFTWARE\Policies\Dapplo\Policy Test\Core\MaxRetries", explain);
        Assert.Contains("users can't change", explain);
        Assert.Contains("users can change", Text(template, "Core_MaxRetries_Recommended_Explain"));
    }

    [Theory]
    [InlineData("OutputFileFormat", "Output file format")]
    [InlineData("HTTPProxy", "HTTP proxy")]
    [InlineData("MaxRetries", "Max retries")]
    [InlineData("UseIPV6", "Use IPV6")]
    [InlineData("Name", "Name")]
    public void SplitCamelCase(string name, string expected) => Assert.Equal(expected, PolicyMetadata.SplitCamelCase(name));
}
