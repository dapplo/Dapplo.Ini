// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Concurrent;
using Dapplo.Ini;
using Dapplo.Ini.Attributes;
using Dapplo.Ini.Converters;
using Dapplo.Ini.Interfaces;
using Dapplo.Ini.Parsing;

namespace Dapplo.Ini.Tests;

/// <summary>Section with a property type that has no built-in converter.</summary>
[IniSection("NoConverter")]
public interface INoConverterSettings : IIniSection
{
    short Small { get; set; }
}

/// <summary>
/// Regression tests for data-loss and crash bugs: reload path resolution, atomic writes,
/// repeated section headers, culture-sensitive number parsing, setter round-trips,
/// constants protection and exceptions on background threads.
/// </summary>
[Collection("IniConfigRegistry")]
public sealed class DataSafetyTests : IDisposable
{
    private readonly string _tempDir;

    public DataSafetyTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        IniConfigRegistry.Clear();
    }

    public void Dispose()
    {
        IniConfigRegistry.Clear();
        if (Directory.Exists(_tempDir))
        {
            foreach (var file in Directory.GetFiles(_tempDir))
                File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    private string WriteIni(string fileName, string content)
    {
        var path = Path.Combine(_tempDir, fileName);
        File.WriteAllText(path, content);
        return path;
    }

    // ── Reload resolves bare-named defaults / constants files through the search paths ──

    [Fact]
    public void Reload_WithBareNamedDefaultsAndConstantsFiles_StillAppliesThem()
    {
        WriteIni("bare.ini", "[ConstantsTest]");
        WriteIni("bare-defaults.ini", "[ConstantsTest]\nUserValue = from-defaults");
        WriteIni("bare-constants.ini", "[ConstantsTest]\nAdminValue = from-constants");

        var section = new ConstantsSettingsImpl();
        using var config = IniConfigRegistry.ForFile("bare.ini")
            .AddSearchPath(_tempDir)
            .AddDefaultsFile("bare-defaults.ini")
            .AddConstantsFile("bare-constants.ini")
            .RegisterSection<IConstantsSettings>(section)
            .Build();

        config.Reload();

        Assert.Equal("from-defaults", section.UserValue);
        Assert.Equal("from-constants", section.AdminValue);
        Assert.True(section.IsConstant("AdminValue"));
    }

    [Fact]
    public async Task ReloadAsync_WithBareNamedDefaultsAndConstantsFiles_StillAppliesThem()
    {
        WriteIni("bare-async.ini", "[ConstantsTest]");
        WriteIni("bare-async-defaults.ini", "[ConstantsTest]\nUserValue = from-defaults");
        WriteIni("bare-async-constants.ini", "[ConstantsTest]\nAdminValue = from-constants");

        var section = new ConstantsSettingsImpl();
        using var config = IniConfigRegistry.ForFile("bare-async.ini")
            .AddSearchPath(_tempDir)
            .AddDefaultsFile("bare-async-defaults.ini")
            .AddConstantsFile("bare-async-constants.ini")
            .RegisterSection<IConstantsSettings>(section)
            .Build();

        await config.ReloadAsync();

        Assert.Equal("from-defaults", section.UserValue);
        Assert.Equal("from-constants", section.AdminValue);
        Assert.True(section.IsConstant("AdminValue"));
    }

    // ── Atomic writes ─────────────────────────────────────────────────────────

    [Fact]
    public void WriteFile_ReplacesExistingFile_AndLeavesNoTemporaryFiles()
    {
        var path = WriteIni("atomic.ini", "[Old]\nKey = old");
        var iniFile = IniFileParser.Parse("[New]\nKey = new");

        IniFileWriter.WriteFile(path, iniFile);

        Assert.Contains("[New]", File.ReadAllText(path));
        Assert.Single(Directory.GetFiles(_tempDir));
    }

    [Fact]
    public async Task WriteFileAsync_CreatesNewFile_AndLeavesNoTemporaryFiles()
    {
        var path = Path.Combine(_tempDir, "atomic-async.ini");
        var iniFile = IniFileParser.Parse("[New]\nKey = new");

        await IniFileWriter.WriteFileAsync(path, iniFile);

        Assert.Equal("new", IniFileParser.ParseFile(path).GetSection("New")!.GetValue("Key"));
        Assert.Single(Directory.GetFiles(_tempDir));
    }

    [Fact]
    public void WriteFile_WhenTargetCannotBeReplaced_KeepsOldContentAndCleansUp()
    {
        if (!OperatingSystem.IsWindows())
            return; // read-only files can be replaced by rename on Unix file systems

        var path = WriteIni("readonly.ini", "[Old]\nKey = old");
        File.SetAttributes(path, FileAttributes.ReadOnly);

        Assert.ThrowsAny<Exception>(() => IniFileWriter.WriteFile(path, IniFileParser.Parse("[New]\nKey = new")));

        Assert.Contains("[Old]", File.ReadAllText(path));
        Assert.Single(Directory.GetFiles(_tempDir));
    }

    // ── Parser: repeated section headers ──────────────────────────────────────

    [Fact]
    public void Parse_RepeatedSectionHeader_MergesIntoFirstSection()
    {
        var file = IniFileParser.Parse("[A]\nx = 1\n[B]\ny = 2\n[A]\nz = 3");

        Assert.Equal(2, file.Sections.Count);
        Assert.Equal("1", file.GetSection("A")!.GetValue("x"));
        Assert.Equal("3", file.GetSection("A")!.GetValue("z"));
    }

    [Fact]
    public void Parse_RepeatedSectionHeader_FirstWinsStillApplies()
    {
        var options = new IniParserOptions { DuplicateKeyHandling = DuplicateKeyHandling.FirstWins };
        var file = IniFileParser.Parse("[A]\nx = 1\n[A]\nx = 2", options);

        Assert.Equal("1", file.GetSection("A")!.GetValue("x"));
    }

    // ── Converters: no thousands separators ───────────────────────────────────

    [Theory]
    [InlineData(typeof(double))]
    [InlineData(typeof(float))]
    [InlineData(typeof(decimal))]
    public void FloatingPointConverters_RejectThousandsSeparator(Type type)
    {
        var converter = ValueConverterRegistry.GetConverter(type)!;
        Assert.ThrowsAny<FormatException>(() => converter.ConvertFromString("1,5"));
        Assert.Equal(1.5m, Convert.ToDecimal(converter.ConvertFromString("1.5")));
    }

    // ── Setters: no converter round-trip ──────────────────────────────────────

    [Fact]
    public void Setter_TypeWithoutConverter_KeepsAssignedValue()
    {
        var section = new NoConverterSettingsImpl();
        section.Small = 5;
        Assert.Equal((short)5, section.Small);
    }

    [Fact]
    public void Setter_List_KeepsTheAssignedInstance()
    {
        var section = new CollectionSettingsImpl();
        var list = new List<string> { "a" };

        section.StringList = list;
        list.Add("b");

        Assert.Same(list, section.StringList);
        Assert.Equal(new[] { "a", "b" }, section.StringList);
    }

    [Fact]
    public void Setter_NullOnEmptyWhenNullProperty_GivesEmptyValue()
    {
        var section = new EmptyWhenNullSettingsImpl();

        section.Description = null;
        section.Tags = null;
        section.Codes = null;
        section.NullableString = null;

        Assert.Equal(string.Empty, section.Description);
        Assert.NotNull(section.Tags);
        Assert.Empty(section.Tags!);
        Assert.NotNull(section.Codes);
        Assert.Null(section.NullableString);
    }

    // ── Constants: a rejected assignment changes nothing ──────────────────────

    [Fact]
    public void Setter_OnConstantKey_ThrowsAndLeavesValueAndDirtyFlagUnchanged()
    {
        WriteIni("constset.ini", "[ConstantsTest]");
        var constantsPath = WriteIni("constset-constants.ini", "[ConstantsTest]\nAdminValue = protected");

        var section = new ConstantsSettingsImpl();
        using var config = IniConfigRegistry.ForFile("constset.ini")
            .AddSearchPath(_tempDir)
            .AddConstantsFile(constantsPath)
            .RegisterSection<IConstantsSettings>(section)
            .Build();

        Assert.Throws<AccessViolationException>(() => section.AdminValue = "changed");

        Assert.Equal("protected", section.AdminValue);
        Assert.False(section.HasChanges);
    }

    // ── Background exceptions are reported, not thrown ────────────────────────

    [Fact]
    public async Task AutoSave_WhenSaveFails_ReportsErrorInsteadOfCrashing()
    {
        var listener = new ErrorListener();
        var section = new ReloadSettingsImpl();
        // No search path exists, so the config has no path to save to and Save() throws.
        using var config = IniConfigRegistry.ForFile("nowhere.ini")
            .AddSearchPath(Path.Combine(_tempDir, "does-not-exist"))
            .AutoSaveInterval(TimeSpan.FromMilliseconds(50))
            .AddListener(listener)
            .RegisterSection<IReloadSettings>(section)
            .Build();

        section.Value = "changed";

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (listener.Errors.IsEmpty && DateTime.UtcNow < deadline)
            await Task.Delay(20);

        Assert.Contains(listener.Errors, e => e.Operation == "Save" && e.Exception is InvalidOperationException);
    }

    private sealed class ErrorListener : IIniConfigListener
    {
        public ConcurrentQueue<(string Operation, Exception Exception)> Errors { get; } = new();
        public void OnFileLoaded(string filePath) { }
        public void OnFileNotFound(string fileName) { }
        public void OnSaved(string filePath) { }
        public void OnReloaded(string filePath) { }
        public void OnError(string operation, Exception exception) => Errors.Enqueue((operation, exception));
        public void OnUnknownKey(string sectionName, string key, string? rawValue) { }
        public void OnValueConversionFailed(string sectionName, string key, string? rawValue, Exception exception) { }
    }
}
