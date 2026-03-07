// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Dapplo.Ini.Config.Parsing;

namespace Dapplo.Ini.Config.Tests;

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
}
