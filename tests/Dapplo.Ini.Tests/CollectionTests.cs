// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace Dapplo.Ini.Tests;

/// <summary>
/// Integration tests for list, array, and dictionary property types.
/// Verifies that INI files with comma-separated values are correctly loaded and saved.
/// </summary>
[Collection("IniConfigRegistry")]
public sealed class CollectionTests : IDisposable
{
    private readonly string _tempDir;

    public CollectionTests()
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

    // ── Default values ────────────────────────────────────────────────────────

    [Fact]
    public void Build_WithNoFile_AppliesListDefaults()
    {
        var section = new CollectionSettingsImpl();
        IniConfigRegistry.ForFile("coldefault.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<ICollectionSettings>(section)
            .Build();

        Assert.Equal(new List<string> { "A", "B", "C" }, section.StringList);
        Assert.Equal(new List<int> { 1, 2, 3 }, section.IntList);
        Assert.Equal(new[] { "red", "green", "blue" }, section.StringArray);
        Assert.Equal(new List<string> { "left", "middle", "right" }, section.PipeStringList);
        Assert.Equal(new[] { 1, 2, 3 }, section.SemicolonIntArray);
    }

    [Fact]
    public void Build_WithNoFile_AppliesDictionaryDefault()
    {
        var section = new CollectionSettingsImpl();
        IniConfigRegistry.ForFile("dictdefault.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<ICollectionSettings>(section)
            .Build();

        Assert.NotNull(section.StringIntDictionary);
        Assert.Equal(2, section.StringIntDictionary!.Count);
        Assert.Equal(10, section.StringIntDictionary["x"]);
        Assert.Equal(20, section.StringIntDictionary["y"]);
    }

    // ── Load from file ────────────────────────────────────────────────────────

    [Fact]
    public void Build_WithFile_LoadsListProperty()
    {
        WriteIni("collist.ini", "[Collections]\nStringList = Feature1,Feature2,Feature3");

        var section = new CollectionSettingsImpl();
        IniConfigRegistry.ForFile("collist.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<ICollectionSettings>(section)
            .Build();

        Assert.Equal(new List<string> { "Feature1", "Feature2", "Feature3" }, section.StringList);
    }

    [Fact]
    public void Build_WithFile_LoadsIntListProperty()
    {
        WriteIni("colints.ini", "[Collections]\nIntList = 10,20,30");

        var section = new CollectionSettingsImpl();
        IniConfigRegistry.ForFile("colints.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<ICollectionSettings>(section)
            .Build();

        Assert.Equal(new List<int> { 10, 20, 30 }, section.IntList);
    }

    [Fact]
    public void Build_WithFile_LoadsIListProperty()
    {
        WriteIni("colilist.ini", "[Collections]\nStringIList = P,Q,R");

        var section = new CollectionSettingsImpl();
        IniConfigRegistry.ForFile("colilist.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<ICollectionSettings>(section)
            .Build();

        Assert.NotNull(section.StringIList);
        Assert.Equal(new[] { "P", "Q", "R" }, section.StringIList!.ToArray());
    }

    [Fact]
    public void Build_WithFile_LoadsArrayProperty()
    {
        WriteIni("colarray.ini", "[Collections]\nStringArray = alpha,beta,gamma");

        var section = new CollectionSettingsImpl();
        IniConfigRegistry.ForFile("colarray.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<ICollectionSettings>(section)
            .Build();

        Assert.Equal(new[] { "alpha", "beta", "gamma" }, section.StringArray);
    }

    [Fact]
    public void Build_WithFile_LoadsCustomDelimitedListAndArrayProperties()
    {
        WriteIni("colcustom.ini", "[Collections]\nPipeStringList = one|two|three\nSemicolonIntArray = 10;20;30");

        var section = new CollectionSettingsImpl();
        IniConfigRegistry.ForFile("colcustom.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<ICollectionSettings>(section)
            .Build();

        Assert.Equal(new List<string> { "one", "two", "three" }, section.PipeStringList);
        Assert.Equal(new[] { 10, 20, 30 }, section.SemicolonIntArray);
    }

    [Fact]
    public void Build_WithFile_LoadsDictionaryProperty()
    {
        // Dictionary<string, int> uses sub-key notation: "PropertyName.key = value"
        WriteIni("coldict.ini", "[Collections]\nStringIntDictionary.a = 1\nStringIntDictionary.b = 2\nStringIntDictionary.c = 3");

        var section = new CollectionSettingsImpl();
        IniConfigRegistry.ForFile("coldict.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<ICollectionSettings>(section)
            .Build();

        Assert.NotNull(section.StringIntDictionary);
        Assert.Equal(3, section.StringIntDictionary!.Count);
        Assert.Equal(1, section.StringIntDictionary["a"]);
        Assert.Equal(2, section.StringIntDictionary["b"]);
        Assert.Equal(3, section.StringIntDictionary["c"]);
    }

    // ── Save and reload round-trip ─────────────────────────────────────────────

    [Fact]
    public void SaveAndReload_RoundTrips_StringList()
    {
        WriteIni("savelist.ini", "[Collections]\n");

        var section = new CollectionSettingsImpl();
        var config = IniConfigRegistry.ForFile("savelist.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<ICollectionSettings>(section)
            .Build();

        section.StringList = new List<string> { "one", "two", "three" };
        config.Save();

        // Reload fresh
        IniConfigRegistry.Unregister("savelist.ini");
        var section2 = new CollectionSettingsImpl();
        IniConfigRegistry.ForFile("savelist.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<ICollectionSettings>(section2)
            .Build();

        Assert.Equal(new List<string> { "one", "two", "three" }, section2.StringList);
    }

    [Fact]
    public void SaveAndReload_RoundTrips_StringArray()
    {
        WriteIni("savearray.ini", "[Collections]\n");

        var section = new CollectionSettingsImpl();
        var config = IniConfigRegistry.ForFile("savearray.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<ICollectionSettings>(section)
            .Build();

        section.StringArray = new[] { "w", "x", "y", "z" };
        config.Save();

        // Reload fresh
        IniConfigRegistry.Unregister("savearray.ini");
        var section2 = new CollectionSettingsImpl();
        IniConfigRegistry.ForFile("savearray.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<ICollectionSettings>(section2)
            .Build();

        Assert.Equal(new[] { "w", "x", "y", "z" }, section2.StringArray);
    }

    [Fact]
    public void SaveAndReload_RoundTrips_CustomDelimitedListAndArray()
    {
        var path = WriteIni("savecustom.ini", "[Collections]\n");

        var section = new CollectionSettingsImpl();
        var config = IniConfigRegistry.ForFile("savecustom.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<ICollectionSettings>(section)
            .Build();

        section.PipeStringList = new List<string> { "north", "south", "east" };
        section.SemicolonIntArray = new[] { 7, 8, 9 };
        config.Save();

        var written = File.ReadAllText(path);
        Assert.Contains("PipeStringList = north|south|east", written);
        Assert.Contains("SemicolonIntArray = 7;8;9", written);

        IniConfigRegistry.Unregister("savecustom.ini");
        var section2 = new CollectionSettingsImpl();
        IniConfigRegistry.ForFile("savecustom.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<ICollectionSettings>(section2)
            .Build();

        Assert.Equal(new List<string> { "north", "south", "east" }, section2.PipeStringList);
        Assert.Equal(new[] { 7, 8, 9 }, section2.SemicolonIntArray);
    }

    [Fact]
    public void SaveAndReload_RoundTrips_Dictionary()
    {
        WriteIni("savedict.ini", "[Collections]\n");

        var section = new CollectionSettingsImpl();
        var config = IniConfigRegistry.ForFile("savedict.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<ICollectionSettings>(section)
            .Build();

        section.StringIntDictionary = new Dictionary<string, int> { ["foo"] = 99, ["bar"] = 42 };
        config.Save();

        // Reload fresh
        IniConfigRegistry.Unregister("savedict.ini");
        var section2 = new CollectionSettingsImpl();
        IniConfigRegistry.ForFile("savedict.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<ICollectionSettings>(section2)
            .Build();

        Assert.NotNull(section2.StringIntDictionary);
        Assert.Equal(99, section2.StringIntDictionary!["foo"]);
        Assert.Equal(42, section2.StringIntDictionary["bar"]);
    }
}
