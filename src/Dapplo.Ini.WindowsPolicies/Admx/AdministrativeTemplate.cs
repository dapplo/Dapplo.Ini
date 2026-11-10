// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace Dapplo.Ini.WindowsPolicies.Admx;

/// <summary>A generated administrative template: one ADMX and an ADML per language.</summary>
public sealed class AdministrativeTemplate
{
    internal AdministrativeTemplate(string fileName, XDocument admx, IReadOnlyDictionary<string, XDocument> adml, int policyCount)
    {
        FileName = fileName;
        Admx = admx;
        Adml = adml;
        PolicyCount = policyCount;
    }

    /// <summary>The file name without extension (<c>&lt;FileName&gt;.admx</c>, <c>&lt;lang&gt;\&lt;FileName&gt;.adml</c>).</summary>
    public string FileName { get; }

    /// <summary>The policy definitions.</summary>
    public XDocument Admx { get; }

    /// <summary>The resources, per language (e.g. <c>en-US</c>).</summary>
    public IReadOnlyDictionary<string, XDocument> Adml { get; }

    /// <summary>The number of policies (mandatory and recommended).</summary>
    public int PolicyCount { get; }

    /// <summary>
    /// Writes <c>&lt;directory&gt;\&lt;FileName&gt;.admx</c> and <c>&lt;directory&gt;\&lt;lang&gt;\&lt;FileName&gt;.adml</c>,
    /// the layout of <c>C:\Windows\PolicyDefinitions</c> and of the central store. Returns the written files.
    /// </summary>
    public IReadOnlyList<string> WriteTo(string directory)
    {
        Directory.CreateDirectory(directory);
        var files = new List<string>();
        var admxPath = Path.Combine(directory, FileName + ".admx");
        Save(Admx, admxPath);
        files.Add(admxPath);
        foreach (var pair in Adml)
        {
            var languageDirectory = Path.Combine(directory, pair.Key);
            Directory.CreateDirectory(languageDirectory);
            var admlPath = Path.Combine(languageDirectory, FileName + ".adml");
            Save(pair.Value, admlPath);
            files.Add(admlPath);
        }
        return files;
    }

    /// <summary>The ADMX as text.</summary>
    public string AdmxText => ToText(Admx);

    /// <summary>The ADML of <paramref name="language"/> as text.</summary>
    public string GetAdmlText(string language) => ToText(Adml[language]);

    private static XmlWriterSettings Settings => new()
    {
        Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true),
        Indent = true,
        IndentChars = "  ",
        NewLineChars = "\r\n"
    };

    private static void Save(XDocument document, string path)
    {
        using var writer = XmlWriter.Create(path, Settings);
        document.Save(writer);
    }

    private static string ToText(XDocument document)
    {
        var settings = Settings;
        settings.OmitXmlDeclaration = false;
        var sb = new StringBuilder();
        using (var writer = XmlWriter.Create(new Utf8StringWriter(sb), settings))
            document.Save(writer);
        return sb.ToString();
    }

    private sealed class Utf8StringWriter : StringWriter
    {
        public Utf8StringWriter(StringBuilder sb) : base(sb, System.Globalization.CultureInfo.InvariantCulture) { }
        public override Encoding Encoding => Encoding.UTF8;
    }
}
