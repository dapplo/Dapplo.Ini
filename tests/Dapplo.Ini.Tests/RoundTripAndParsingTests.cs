// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Dapplo.Ini;
using Dapplo.Ini.Attributes;
using Dapplo.Ini.Converters;
using Dapplo.Ini.Interfaces;
using Dapplo.Ini.Parsing;

namespace Dapplo.Ini.Tests;

[Flags]
public enum RoundTripFlags
{
    None = 0,
    A = 1,
    B = 2,
    C = 4,
}

/// <summary>Section with the value shapes that used to lose data on a save/load round trip.</summary>
[IniSection("RoundTrip")]
public interface IRoundTripSettings : IIniSection
{
    int? OptionalNumber { get; set; }
    List<string>? Names { get; set; }
    Dictionary<string, string>? Pairs { get; set; }
    List<RoundTripFlags>? FlagList { get; set; }
    short Small { get; set; }
    char Letter { get; set; }
    Uri? Link { get; set; }
    string? Text { get; set; }
}

/// <summary>A custom type with a converter registered by the application.</summary>
public sealed class Temperature
{
    public double Celsius { get; init; }
}

public sealed class TemperatureConverter : ValueConverterBase<Temperature>
{
    public override Temperature? ConvertFromString(string? raw, Temperature? defaultValue = default)
        => string.IsNullOrWhiteSpace(raw) ? defaultValue : new Temperature { Celsius = double.Parse(raw!.TrimEnd('C'), System.Globalization.CultureInfo.InvariantCulture) };

    public override string? ConvertToString(Temperature? value)
        => value == null ? null : value.Celsius.ToString(System.Globalization.CultureInfo.InvariantCulture) + "C";
}

/// <summary>
/// Round-trip tests for converters and the writer/parser, plus value sources versus constants
/// and file monitoring of atomic replaces.
/// </summary>
[Collection("IniConfigRegistry")]
public sealed class RoundTripAndParsingTests : IDisposable
{
    private readonly string _tempDir;

    public RoundTripAndParsingTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        IniConfigRegistry.Clear();
    }

    public void Dispose()
    {
        IniConfigRegistry.Clear();
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private string WriteIni(string fileName, string content)
    {
        var path = Path.Combine(_tempDir, fileName);
        File.WriteAllText(path, content);
        return path;
    }

    private RoundTripSettingsImpl SaveAndReload(RoundTripSettingsImpl original, string fileName)
    {
        using (var config = IniConfigRegistry.ForFile(fileName).AddSearchPath(_tempDir)
                   .RegisterSection<IRoundTripSettings>(new RoundTripSettingsImpl()).Build())
        {
            var section = config.GetSection<IRoundTripSettings>();
            section.OptionalNumber = original.OptionalNumber;
            section.Names = original.Names;
            section.Pairs = original.Pairs;
            section.FlagList = original.FlagList;
            section.Small = original.Small;
            section.Letter = original.Letter;
            section.Link = original.Link;
            section.Text = original.Text;
            config.Save();
        }
        IniConfigRegistry.Clear();

        var reloaded = new RoundTripSettingsImpl();
        using var second = IniConfigRegistry.ForFile(fileName).AddSearchPath(_tempDir)
            .RegisterSection<IRoundTripSettings>(reloaded).Build();
        return reloaded;
    }

    // ── Section round trips ───────────────────────────────────────────────────

    [Fact]
    public void RoundTrip_ValuesThatContainSeparatorsQuotesAndNulls()
    {
        var original = new RoundTripSettingsImpl
        {
            OptionalNumber = null,
            Names = new List<string> { "a,b", "\"quoted\"", " padded ", "", "plain" },
            Pairs = new Dictionary<string, string> { ["simple key"] = "value,with,commas", ["x"] = "" },
            FlagList = new List<RoundTripFlags> { RoundTripFlags.A | RoundTripFlags.B, RoundTripFlags.C },
            Small = -12,
            Letter = 'x',
            Link = new Uri("docs/page.html?q=a%20b", UriKind.Relative),
            Text = "first line\n[Injected]\nKey = value",
        };

        var reloaded = SaveAndReload(original, "roundtrip.ini");

        Assert.Null(reloaded.OptionalNumber);
        Assert.Equal(original.Names, reloaded.Names);
        Assert.Equal(original.Pairs, reloaded.Pairs);
        Assert.Equal(original.FlagList, reloaded.FlagList);
        Assert.Equal((short)-12, reloaded.Small);
        Assert.Equal('x', reloaded.Letter);
        Assert.Equal("docs/page.html?q=a%20b", reloaded.Link!.OriginalString);
        // The line break cannot be stored raw; it must not inject a section either.
        var file = IniFileParser.ParseFile(Path.Combine(_tempDir, "roundtrip.ini"));
        Assert.Null(file.GetSection("Injected"));
    }

    [Fact]
    public void DictionaryConverter_KeysAndValuesWithSeparators_RoundTrip()
    {
        var converter = ValueConverterRegistry.GetConverter(typeof(IReadOnlyDictionary<string, string>))!;
        var original = new Dictionary<string, string> { ["k=1"] = "a,b", ["k2"] = "\"q\"", ["k3"] = "" };

        var raw = converter.ConvertToString(original);
        var parsed = (IDictionary<string, string>)converter.ConvertFromString(raw)!;

        Assert.Equal(original.OrderBy(p => p.Key), parsed.OrderBy(p => p.Key));
    }

    [Fact]
    public void RoundTrip_NullableWithValue()
    {
        var reloaded = SaveAndReload(new RoundTripSettingsImpl { OptionalNumber = 7 }, "nullable.ini");
        Assert.Equal(7, reloaded.OptionalNumber);
    }

    [Fact]
    public void ListConverter_UnquotedInput_IsReadAsBefore()
    {
        var converter = ValueConverterRegistry.GetConverter(typeof(List<string>))!;
        Assert.Equal(new List<string> { "a", "b", "c" }, converter.ConvertFromString("a, b ,c"));
        Assert.Equal(new List<string> { "a", "", "b" }, converter.ConvertFromString("a,,b"));
        Assert.Equal(new List<string>(), converter.ConvertFromString("   "));
    }

    [Fact]
    public void RegisteringAConverter_UpdatesListConvertersCreatedEarlier()
    {
        // Compose a list converter before the element converter exists …
        Assert.Null(ValueConverterRegistry.GetConverter(typeof(List<Temperature>)));
        ValueConverterRegistry.Register(new TemperatureConverter());

        var converter = ValueConverterRegistry.GetConverter(typeof(List<Temperature>))!;
        var list = (List<Temperature>)converter.ConvertFromString("1.5C,20C")!;

        Assert.Equal(new[] { 1.5, 20.0 }, list.Select(t => t.Celsius));
    }

    [Theory]
    [InlineData("yes", true)]
    [InlineData("1", true)]
    [InlineData("On", true)]
    [InlineData("no", false)]
    [InlineData("0", false)]
    [InlineData("False", false)]
    public void BoolConverter_AcceptsCommonSpellings(string raw, bool expected)
        => Assert.Equal(expected, ValueConverterRegistry.GetConverter(typeof(bool))!.ConvertFromString(raw));

    // ── Writer / parser ───────────────────────────────────────────────────────

    [Theory]
    [InlineData("a\"b")]
    [InlineData("a\\\"b")]
    [InlineData("ends with backslash\\")]
    [InlineData("\"already quoted\"")]
    [InlineData("C:\\path\\")]
    public void QuotedValues_RoundTripWithoutEscapeSequences(string value)
    {
        var file = new IniFile();
        file.GetOrAddSection("S").SetValue("Key", value);
        var text = IniFileWriter.WriteToString(file, new IniWriterOptions { QuoteStyle = IniValueQuoteStyle.Double });

        var parsed = IniFileParser.Parse(text, new IniParserOptions { QuotedValues = true });

        Assert.Equal(value, parsed.GetSection("S")!.GetValue("Key"));
    }

    [Fact]
    public void AutoQuoting_ValueThatLooksQuoted_KeepsItsQuotes()
    {
        var file = new IniFile();
        file.GetOrAddSection("S").SetValue("Key", "\"x\"");
        var text = IniFileWriter.WriteToString(file, new IniWriterOptions { QuoteStyle = IniValueQuoteStyle.Auto });

        var parsed = IniFileParser.Parse(text, new IniParserOptions { QuotedValues = true });

        Assert.Equal("\"x\"", parsed.GetSection("S")!.GetValue("Key"));
    }

    [Fact]
    public void Writer_MultiLineComment_PrefixesEveryLine()
    {
        var file = new IniFile();
        file.AddSection(new IniSection("S", new[] { "line one\nline two" }));
        file.GetSection("S")!.SetValue("Key", "v");

        var parsed = IniFileParser.Parse(IniFileWriter.WriteToString(file));

        Assert.Equal(new[] { "line one", "line two" }, parsed.GetSection("S")!.Comments);
        Assert.Single(parsed.GetSection("S")!.Entries);
    }

    [Fact]
    public void LineContinuation_DoesNotSwallowSectionHeaderOrEscapedBackslash()
    {
        var options = new IniParserOptions { LineContinuation = true };
        var parsed = IniFileParser.Parse("[A]\nDir = C:\\Temp\\\n[B]\nEscaped = x\\\\\nOther = y", options);

        Assert.Equal("C:\\Temp\\", parsed.GetSection("A")!.GetValue("Dir"));
        Assert.Equal("x\\\\", parsed.GetSection("B")!.GetValue("Escaped"));
        Assert.Equal("y", parsed.GetSection("B")!.GetValue("Other"));
    }

    [Fact]
    public void Parse_ColonStyleLineWithEqualsInValue_SplitsAtTheColon()
    {
        var parsed = IniFileParser.Parse("[S]\nConnectionString: Server=x;Db=y");
        Assert.Equal("Server=x;Db=y", parsed.GetSection("S")!.GetValue("ConnectionString"));
    }

    [Fact]
    public void QuotedValues_HandWrittenPathEndingWithBackslash_IsKept()
    {
        var parsed = IniFileParser.Parse("[S]\nDir = \"C:\\Temp\\\"", new IniParserOptions { QuotedValues = true });
        Assert.Equal("C:\\Temp\\", parsed.GetSection("S")!.GetValue("Dir"));
    }

    [Fact]
    public void Parse_StringWithByteOrderMark_ReadsFirstSection()
    {
        var parsed = IniFileParser.Parse("\uFEFF[S]\nKey = v");
        Assert.Equal("v", parsed.GetSection("S")!.GetValue("Key"));
    }

    // ── Value sources versus constants ────────────────────────────────────────

    [Fact]
    public void ValueSource_ForConstantKey_IsIgnoredInsteadOfFailingTheLoad()
    {
        WriteIni("vs-const.ini", "[ConstantsTest]");
        var constants = WriteIni("vs-const-constants.ini", "[ConstantsTest]\nAdminValue = admin");
        var source = new DictionaryValueSource();
        source.SetValue("ConstantsTest", "AdminValue", "from source");
        source.SetValue("ConstantsTest", "UserValue", "from source");

        var section = new ConstantsSettingsImpl();
        using var config = IniConfigRegistry.ForFile("vs-const.ini")
            .AddSearchPath(_tempDir)
            .AddConstantsFile(constants)
            .AddValueSource(source)
            .RegisterSection<IConstantsSettings>(section)
            .Build();

        Assert.Equal("admin", section.AdminValue);
        Assert.Equal("from source", section.UserValue);
    }

    [Fact]
    public void ValueSource_ForConstantKey_IsReportedToExtendedListeners()
    {
        WriteIni("vs-report.ini", "[ConstantsTest]");
        var constants = WriteIni("vs-report-constants.ini", "[ConstantsTest]\nAdminValue = admin");
        var source = new DictionaryValueSource();
        source.SetValue("ConstantsTest", "AdminValue", "from source");
        var listener = new IgnoredValueListener();

        using var config = IniConfigRegistry.ForFile("vs-report.ini")
            .AddSearchPath(_tempDir)
            .AddConstantsFile(constants)
            .AddValueSource(source)
            .AddListener(listener)
            .RegisterSection<IConstantsSettings>(new ConstantsSettingsImpl())
            .Build();

        Assert.Equal(new[] { ("ConstantsTest", "AdminValue", (string?)"from source") }, listener.Ignored);
    }

    private sealed class IgnoredValueListener : IniConfigListenerBase
    {
        public List<(string, string, string?)> Ignored { get; } = new();
        public override void OnValueSourceIgnored(string sectionName, string key, string? ignoredValue)
            => Ignored.Add((sectionName, key, ignoredValue));
    }

    // ── File monitoring ───────────────────────────────────────────────────────

    [Fact]
    public async Task MonitorFile_DetectsReplaceByRename()
    {
        var path = WriteIni("replaced.ini", "[ReloadSection]\nValue = original");
        var section = new ReloadSettingsImpl();
        using var config = IniConfigRegistry.ForFile("replaced.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<IReloadSettings>(section)
            .MonitorFile(debounceMs: 50)
            .Build();

        // Save the way many editors do: write a new file, then swap it in.
        var temp = Path.Combine(_tempDir, "replaced.ini.new");
        File.WriteAllText(temp, "[ReloadSection]\nValue = replaced");
        File.Delete(path);
        File.Move(temp, path);

        for (var i = 0; i < 50 && section.Value != "replaced"; i++)
            await Task.Delay(50);

        Assert.Equal("replaced", section.Value);
    }
}
