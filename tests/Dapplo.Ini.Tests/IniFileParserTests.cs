// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Dapplo.Ini.Parsing;

namespace Dapplo.Ini.Tests;

public sealed class IniFileParserTests
{
    private const string SampleIni = """
        ; Top-level comment

        [General]
        ; Application name
        AppName = MyApp
        MaxRetries = 5
        EnableLogging = True
        Threshold = 1.5

        [User]
        Username = admin
        Password = secret
        """;

    [Fact]
    public void Parse_WithSections_ReturnsTwoSections()
    {
        var file = IniFileParser.Parse(SampleIni);
        Assert.Equal(2, file.Sections.Count);
    }

    [Fact]
    public void Parse_SectionNames_AreCorrect()
    {
        var file = IniFileParser.Parse(SampleIni);
        Assert.NotNull(file.GetSection("General"));
        Assert.NotNull(file.GetSection("User"));
    }

    [Fact]
    public void Parse_KeyValues_AreCorrect()
    {
        var file = IniFileParser.Parse(SampleIni);
        var general = file.GetSection("General")!;

        Assert.Equal("MyApp", general.GetValue("AppName"));
        Assert.Equal("5", general.GetValue("MaxRetries"));
        Assert.Equal("True", general.GetValue("EnableLogging"));
        Assert.Equal("1.5", general.GetValue("Threshold"));
    }

    [Fact]
    public void Parse_EntryComments_ArePreserved()
    {
        var file = IniFileParser.Parse(SampleIni);
        var entry = file.GetSection("General")!.GetEntry("AppName");
        Assert.NotNull(entry);
        Assert.Contains("Application name", entry!.Comments);
    }

    [Fact]
    public void Parse_SectionLookup_IsCaseInsensitive()
    {
        var file = IniFileParser.Parse(SampleIni);
        Assert.NotNull(file.GetSection("GENERAL"));
        Assert.NotNull(file.GetSection("general"));
    }

    [Fact]
    public void Parse_KeyLookup_IsCaseInsensitive()
    {
        var file = IniFileParser.Parse(SampleIni);
        var general = file.GetSection("General")!;
        Assert.Equal("MyApp", general.GetValue("APPNAME"));
        Assert.Equal("MyApp", general.GetValue("appname"));
    }

    [Fact]
    public void WriteToString_RoundTrip_PreservesValues()
    {
        var file = IniFileParser.Parse(SampleIni);
        var output = IniFileWriter.WriteToString(file);
        var reparsed = IniFileParser.Parse(output);

        Assert.Equal("MyApp", reparsed.GetSection("General")!.GetValue("AppName"));
        Assert.Equal("admin", reparsed.GetSection("User")!.GetValue("Username"));
    }

    [Fact]
    public void Parse_EmptyContent_ReturnsEmptyFile()
    {
        var file = IniFileParser.Parse(string.Empty);
        Assert.Empty(file.Sections);
    }

    [Fact]
    public void Parse_CommentOnlyContent_ReturnsEmptyFile()
    {
        var file = IniFileParser.Parse("; just a comment\n# another");
        Assert.Empty(file.Sections);
    }

    [Fact]
    public void Parse_KeyWithoutSection_GoesToEmptySection()
    {
        const string content = "key = value\n[MySection]\nfoo = bar";
        var file = IniFileParser.Parse(content);
        // Global keys land in the synthetic "" section
        var global = file.GetSection(string.Empty);
        Assert.NotNull(global);
        Assert.Equal("value", global!.GetValue("key"));
    }

    [Fact]
    public void IniFile_GetOrAddSection_CreatesNewSection()
    {
        var file = new IniFile();
        var section = file.GetOrAddSection("Test");
        Assert.Equal("Test", section.Name);
        Assert.Same(section, file.GetSection("Test"));
    }

    [Fact]
    public void IniSection_SetValue_UpdatesExistingEntry()
    {
        var section = new IniSection("s", Array.Empty<string>());
        section.SetValue("key", "v1");
        section.SetValue("key", "v2");
        Assert.Equal("v2", section.GetValue("key"));
        // Only one entry
        Assert.Single(section.Entries);
    }

    // ── IniParserOptions: DuplicateKeyHandling ────────────────────────────────

    [Fact]
    public void Parse_DuplicateKey_LastWins_ByDefault()
    {
        const string content = "[S]\nkey = first\nkey = second";
        var file = IniFileParser.Parse(content);
        Assert.Equal("second", file.GetSection("S")!.GetValue("key"));
    }

    [Fact]
    public void Parse_DuplicateKey_FirstWins_KeepsFirstValue()
    {
        const string content = "[S]\nkey = first\nkey = second";
        var opts = new IniParserOptions { DuplicateKeyHandling = DuplicateKeyHandling.FirstWins };
        var file = IniFileParser.Parse(content, opts);
        Assert.Equal("first", file.GetSection("S")!.GetValue("key"));
        // Only one entry should exist
        Assert.Single(file.GetSection("S")!.Entries);
    }

    [Fact]
    public void Parse_DuplicateKey_ThrowError_ThrowsInvalidOperationException()
    {
        const string content = "[S]\nkey = first\nkey = second";
        var opts = new IniParserOptions { DuplicateKeyHandling = DuplicateKeyHandling.ThrowError };
        Assert.Throws<InvalidOperationException>(() => IniFileParser.Parse(content, opts));
    }

    // ── IniParserOptions: QuotedValues ────────────────────────────────────────

    [Fact]
    public void Parse_QuotedValues_Disabled_KeepsQuotes()
    {
        const string content = "[S]\nkey = \"hello world\"";
        var file = IniFileParser.Parse(content);
        Assert.Equal("\"hello world\"", file.GetSection("S")!.GetValue("key"));
    }

    [Fact]
    public void Parse_QuotedValues_Enabled_StripsDoubleQuotes()
    {
        const string content = "[S]\nkey = \"hello world\"";
        var opts = new IniParserOptions { QuotedValues = true };
        var file = IniFileParser.Parse(content, opts);
        Assert.Equal("hello world", file.GetSection("S")!.GetValue("key"));
    }

    [Fact]
    public void Parse_QuotedValues_Enabled_StripsSingleQuotes()
    {
        const string content = "[S]\nkey = 'hello world'";
        var opts = new IniParserOptions { QuotedValues = true };
        var file = IniFileParser.Parse(content, opts);
        Assert.Equal("hello world", file.GetSection("S")!.GetValue("key"));
    }

    [Fact]
    public void Parse_QuotedValues_Enabled_PreservesInternalWhitespace()
    {
        const string content = "[S]\nkey = \"  spaces  \"";
        var opts = new IniParserOptions { QuotedValues = true };
        var file = IniFileParser.Parse(content, opts);
        Assert.Equal("  spaces  ", file.GetSection("S")!.GetValue("key"));
    }

    [Fact]
    public void Parse_QuotedValues_Enabled_NoQuotes_ValueUnchanged()
    {
        const string content = "[S]\nkey = plain value";
        var opts = new IniParserOptions { QuotedValues = true };
        var file = IniFileParser.Parse(content, opts);
        Assert.Equal("plain value", file.GetSection("S")!.GetValue("key"));
    }

    // ── IniParserOptions: LineContinuation ────────────────────────────────────

    [Fact]
    public void Parse_LineContinuation_Disabled_KeepsBackslash()
    {
        const string content = "[S]\nkey = first \\\nsecond";
        var file = IniFileParser.Parse(content);
        Assert.Equal("first \\", file.GetSection("S")!.GetValue("key"));
    }

    [Fact]
    public void Parse_LineContinuation_Enabled_JoinsLines()
    {
        const string content = "[S]\nkey = first \\\n      second";
        var opts = new IniParserOptions { LineContinuation = true };
        var file = IniFileParser.Parse(content, opts);
        Assert.Equal("first second", file.GetSection("S")!.GetValue("key"));
    }

    [Fact]
    public void Parse_LineContinuation_Enabled_MultipleLines()
    {
        const string content = "[S]\nkey = a \\\n      b \\\n      c";
        var opts = new IniParserOptions { LineContinuation = true };
        var file = IniFileParser.Parse(content, opts);
        Assert.Equal("a b c", file.GetSection("S")!.GetValue("key"));
    }

    // ── IniParserOptions: EscapeSequences ────────────────────────────────────

    [Fact]
    public void Parse_EscapeSequences_Disabled_KeepsRawBackslash()
    {
        const string content = "[S]\nkey = hello\\nworld";
        var file = IniFileParser.Parse(content);
        Assert.Equal("hello\\nworld", file.GetSection("S")!.GetValue("key"));
    }

    [Fact]
    public void Parse_EscapeSequences_Enabled_DecodesNewline()
    {
        const string content = "[S]\nkey = hello\\nworld";
        var opts = new IniParserOptions { EscapeSequences = true };
        var file = IniFileParser.Parse(content, opts);
        Assert.Equal("hello\nworld", file.GetSection("S")!.GetValue("key"));
    }

    [Fact]
    public void Parse_EscapeSequences_Enabled_DecodesTab()
    {
        const string content = "[S]\nkey = col1\\tcol2";
        var opts = new IniParserOptions { EscapeSequences = true };
        var file = IniFileParser.Parse(content, opts);
        Assert.Equal("col1\tcol2", file.GetSection("S")!.GetValue("key"));
    }

    [Fact]
    public void Parse_EscapeSequences_Enabled_DecodesLiteralBackslash()
    {
        const string content = "[S]\nkey = path\\\\to\\\\file";
        var opts = new IniParserOptions { EscapeSequences = true };
        var file = IniFileParser.Parse(content, opts);
        Assert.Equal("path\\to\\file", file.GetSection("S")!.GetValue("key"));
    }

    [Fact]
    public void Parse_EscapeSequences_Enabled_DecodesHexCharacter()
    {
        const string content = "[S]\nkey = \\x41\\x42"; // AB
        var opts = new IniParserOptions { EscapeSequences = true };
        var file = IniFileParser.Parse(content, opts);
        Assert.Equal("AB", file.GetSection("S")!.GetValue("key"));
    }

    [Fact]
    public void Parse_EscapeSequences_Enabled_UnknownSequence_LeftUnchanged()
    {
        const string content = "[S]\nkey = \\q";
        var opts = new IniParserOptions { EscapeSequences = true };
        var file = IniFileParser.Parse(content, opts);
        Assert.Equal("\\q", file.GetSection("S")!.GetValue("key"));
    }

    // ── IniParserOptions: CaseSensitiveKeys ───────────────────────────────────

    [Fact]
    public void Parse_CaseSensitiveKeys_Disabled_LookupIsCaseInsensitive()
    {
        const string content = "[S]\nAppName = MyApp";
        var file = IniFileParser.Parse(content); // default = case-insensitive
        Assert.Equal("MyApp", file.GetSection("S")!.GetValue("appname"));
        Assert.Equal("MyApp", file.GetSection("S")!.GetValue("APPNAME"));
    }

    [Fact]
    public void Parse_CaseSensitiveKeys_Enabled_LookupIsCaseSensitive()
    {
        const string content = "[S]\nAppName = MyApp";
        var opts = new IniParserOptions { CaseSensitiveKeys = true };
        var file = IniFileParser.Parse(content, opts);
        Assert.Equal("MyApp", file.GetSection("S")!.GetValue("AppName"));
        Assert.Null(file.GetSection("S")!.GetValue("appname"));
    }

    [Fact]
    public void Parse_CaseSensitiveKeys_Enabled_TreatsUpperAndLowerAsDistinct()
    {
        const string content = "[S]\nkey = lower\nKEY = upper";
        var opts = new IniParserOptions { CaseSensitiveKeys = true };
        var file = IniFileParser.Parse(content, opts);
        var section = file.GetSection("S")!;
        Assert.Equal(2, section.Entries.Count);
        Assert.Equal("lower", section.GetValue("key"));
        Assert.Equal("upper", section.GetValue("KEY"));
    }

    // ── IniParserOptions: CaseSensitiveSections ───────────────────────────────

    [Fact]
    public void Parse_CaseSensitiveSections_Disabled_LookupIsCaseInsensitive()
    {
        const string content = "[General]\nkey = v";
        var file = IniFileParser.Parse(content); // default
        Assert.NotNull(file.GetSection("GENERAL"));
        Assert.NotNull(file.GetSection("general"));
    }

    [Fact]
    public void Parse_CaseSensitiveSections_Enabled_LookupIsCaseSensitive()
    {
        const string content = "[General]\nkey = v";
        var opts = new IniParserOptions { CaseSensitiveSections = true };
        var file = IniFileParser.Parse(content, opts);
        Assert.NotNull(file.GetSection("General"));
        Assert.Null(file.GetSection("GENERAL"));
        Assert.Null(file.GetSection("general"));
    }

    [Fact]
    public void Parse_CaseSensitiveSections_Enabled_TreatsUpperAndLowerAsDistinct()
    {
        const string content = "[General]\nkey = v1\n[GENERAL]\nkey = v2";
        var opts = new IniParserOptions { CaseSensitiveSections = true };
        var file = IniFileParser.Parse(content, opts);
        Assert.Equal(2, file.Sections.Count);
        Assert.Equal("v1", file.GetSection("General")!.GetValue("key"));
        Assert.Equal("v2", file.GetSection("GENERAL")!.GetValue("key"));
    }
}
