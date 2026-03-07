// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Dapplo.Ini.Config.Configuration;

namespace Dapplo.Ini.Config.Tests;

public sealed class IniConfigBuilderTests : IDisposable
{
    private readonly string _tempDir;

    public IniConfigBuilderTests()
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

    // ── Load tests ─────────────────────────────────────────────────────────────

    [Fact]
    public void Build_WithExistingFile_LoadsValues()
    {
        const string content = """
            [General]
            AppName = LoadedApp
            MaxRetries = 7
            EnableLogging = False
            Threshold = 2.71
            """;
        WriteIni("app.ini", content);

        var section = new GeneralSettingsImpl();
        var config = IniConfigRegistry.ForFile("app.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<IGeneralSettings>(section)
            .Build();

        Assert.Equal("LoadedApp", section.AppName);
        Assert.Equal(7, section.MaxRetries);
        Assert.False(section.EnableLogging);
        Assert.Equal(2.71, section.Threshold, precision: 10);
    }

    [Fact]
    public void Build_WithNoFile_UsesDefaults()
    {
        var section = new GeneralSettingsImpl();
        var config = IniConfigRegistry.ForFile("missing.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<IGeneralSettings>(section)
            .Build();

        // Default from [IniValue(DefaultValue = "MyApp")]
        Assert.Equal("MyApp", section.AppName);
        Assert.Equal(42, section.MaxRetries);
        Assert.True(section.EnableLogging);
    }

    [Fact]
    public void Build_WithDefaultsFile_AppliesBeforeUserFile()
    {
        WriteIni("defaults.ini", "[General]\nAppName = DefaultApp\nMaxRetries = 1");
        WriteIni("app.ini",      "[General]\nMaxRetries = 99");

        var section = new GeneralSettingsImpl();
        IniConfigRegistry.ForFile("app.ini")
            .AddSearchPath(_tempDir)
            .AddDefaultsFile(Path.Combine(_tempDir, "defaults.ini"))
            .RegisterSection<IGeneralSettings>(section)
            .Build();

        // MaxRetries overridden by user file; AppName comes from defaults
        Assert.Equal(99, section.MaxRetries);
        Assert.Equal("DefaultApp", section.AppName);
    }

    [Fact]
    public void Build_WithConstantsFile_OverridesUserFile()
    {
        WriteIni("app.ini",       "[General]\nAppName = UserApp");
        WriteIni("constants.ini", "[General]\nAppName = AdminApp");

        var section = new GeneralSettingsImpl();
        IniConfigRegistry.ForFile("app.ini")
            .AddSearchPath(_tempDir)
            .AddConstantsFile(Path.Combine(_tempDir, "constants.ini"))
            .RegisterSection<IGeneralSettings>(section)
            .Build();

        // Constants win over user values
        Assert.Equal("AdminApp", section.AppName);
    }

    // ── Registry tests ─────────────────────────────────────────────────────────

    [Fact]
    public void IniConfigRegistry_Get_ReturnsRegisteredConfig()
    {
        var section = new GeneralSettingsImpl();
        IniConfigRegistry.ForFile("reg.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<IGeneralSettings>(section)
            .Build();

        var config = IniConfigRegistry.Get("reg.ini");
        Assert.NotNull(config);
        Assert.Equal("reg.ini", config.FileName);
    }

    [Fact]
    public void IniConfigRegistry_Get_ThrowsWhenNotRegistered()
    {
        Assert.Throws<KeyNotFoundException>(() => IniConfigRegistry.Get("nonexistent.ini"));
    }

    [Fact]
    public void IniConfigRegistry_TryGet_ReturnsFalseWhenMissing()
    {
        var found = IniConfigRegistry.TryGet("nope.ini", out var config);
        Assert.False(found);
        Assert.Null(config);
    }

    [Fact]
    public void IniConfigRegistry_GetSection_ReturnsSection()
    {
        var section = new GeneralSettingsImpl();
        IniConfigRegistry.ForFile("sect.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<IGeneralSettings>(section)
            .Build();

        var retrieved = IniConfigRegistry.GetSection<IGeneralSettings>("sect.ini");
        Assert.NotNull(retrieved);
        Assert.Equal("MyApp", retrieved.AppName); // default
    }

    // ── Save tests ─────────────────────────────────────────────────────────────

    [Fact]
    public void Save_WritesValuesToFile_AndCanBeReloaded()
    {
        WriteIni("save.ini", "[General]\nAppName = Original");

        var section = new GeneralSettingsImpl();
        var config = IniConfigRegistry.ForFile("save.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<IGeneralSettings>(section)
            .Build();

        section.AppName = "Modified";
        config.Save();

        // Reload
        IniConfigRegistry.Unregister("save.ini");
        var section2 = new GeneralSettingsImpl();
        IniConfigRegistry.ForFile("save.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<IGeneralSettings>(section2)
            .Build();

        Assert.Equal("Modified", section2.AppName);
    }
}
