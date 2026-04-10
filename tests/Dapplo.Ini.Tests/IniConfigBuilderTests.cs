// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Dapplo.Ini;

namespace Dapplo.Ini.Tests;

[Collection("IniConfigRegistry")]
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

    [Fact]
    public void Build_WithBareFilenameDefaultsFile_SearchesThroughSearchPaths()
    {
        WriteIni("defaults.ini", "[General]\nAppName = DefaultApp\nMaxRetries = 1");
        WriteIni("app.ini",      "[General]\nMaxRetries = 99");

        var section = new GeneralSettingsImpl();
        IniConfigRegistry.ForFile("app.ini")
            .AddSearchPath(_tempDir)
            .AddDefaultsFile("defaults.ini")   // bare filename — resolved via search paths
            .RegisterSection<IGeneralSettings>(section)
            .Build();

        Assert.Equal(99, section.MaxRetries);
        Assert.Equal("DefaultApp", section.AppName);
    }

    [Fact]
    public void Build_WithBareFilenameConstantsFile_SearchesThroughSearchPaths()
    {
        WriteIni("app.ini",       "[General]\nAppName = UserApp");
        WriteIni("constants.ini", "[General]\nAppName = AdminApp");

        var section = new GeneralSettingsImpl();
        IniConfigRegistry.ForFile("app.ini")
            .AddSearchPath(_tempDir)
            .AddConstantsFile("constants.ini")  // bare filename — resolved via search paths
            .RegisterSection<IGeneralSettings>(section)
            .Build();

        Assert.Equal("AdminApp", section.AppName);
    }

    [Fact]
    public void Build_WithBareFilenameDefaultsFile_MissingFile_DoesNotThrow()
    {
        WriteIni("app.ini", "[General]\nAppName = UserApp");

        var section = new GeneralSettingsImpl();
        IniConfigRegistry.ForFile("app.ini")
            .AddSearchPath(_tempDir)
            .AddDefaultsFile("nonexistent-defaults.ini")
            .RegisterSection<IGeneralSettings>(section)
            .Build();

        // Falls back to compiled default when defaults file is absent
        Assert.Equal("UserApp", section.AppName);
    }

    [Fact]
    public void Build_WithBareFilenameDefaultsFile_MultipleSearchPaths_FindsInSecondPath()
    {
        var secondDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(secondDir);
        try
        {
            // defaults file only exists in the second search path
            File.WriteAllText(Path.Combine(secondDir, "defaults.ini"), "[General]\nAppName = SecondPathApp");
            WriteIni("app.ini", "[General]\nMaxRetries = 5");

            var section = new GeneralSettingsImpl();
            IniConfigRegistry.ForFile("app.ini")
                .AddSearchPath(_tempDir)    // first path — no defaults.ini here
                .AddSearchPath(secondDir)   // second path — defaults.ini lives here
                .AddDefaultsFile("defaults.ini")
                .RegisterSection<IGeneralSettings>(section)
                .Build();

            Assert.Equal("SecondPathApp", section.AppName);
        }
        finally
        {
            Directory.Delete(secondDir, recursive: true);
        }
    }

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

    [Fact]
    public void IniConfigRegistry_Get_NoArg_SingleRegistration_ReturnsConfig()
    {
        var section = new GeneralSettingsImpl();
        IniConfigRegistry.ForFile("single.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<IGeneralSettings>(section)
            .Build();

        var config = IniConfigRegistry.Get();
        Assert.NotNull(config);
        Assert.Equal("single.ini", config.FileName);
    }

    [Fact]
    public void IniConfigRegistry_Get_NoArg_NoRegistration_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => IniConfigRegistry.Get());
    }

    [Fact]
    public void IniConfigRegistry_Get_NoArg_MultipleRegistrations_Throws()
    {
        IniConfigRegistry.ForFile("multi1.ini").AddSearchPath(_tempDir).Build();
        IniConfigRegistry.ForFile("multi2.ini").AddSearchPath(_tempDir).Build();

        Assert.Throws<InvalidOperationException>(() => IniConfigRegistry.Get());
    }

    [Fact]
    public void IniConfigRegistry_GetSection_NoArg_SingleRegistration_ReturnsSection()
    {
        var section = new GeneralSettingsImpl();
        IniConfigRegistry.ForFile("singlesect.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<IGeneralSettings>(section)
            .Build();

        var retrieved = IniConfigRegistry.GetSection<IGeneralSettings>();
        Assert.Same(section, retrieved);
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

    [Fact]
    public void Save_WritesDescriptionAsComments_InIniFile()
    {
        // IGeneralSettings has:
        //   [IniSection("General", Description = "General application settings")]
        //   [IniValue(DefaultValue = "MyApp", Description = "Application name", ...)]
        var section = new GeneralSettingsImpl();
        var config = IniConfigRegistry.ForFile("comments.ini")
            .AddSearchPath(_tempDir)
            .SetWritablePath(Path.Combine(_tempDir, "comments.ini"))
            .RegisterSection<IGeneralSettings>(section)
            .Build();

        config.Save();

        var written = File.ReadAllText(Path.Combine(_tempDir, "comments.ini"));

        // Section description should appear as a comment above [General]
        Assert.Contains("; General application settings", written);
        // Property description should appear as a comment above AppName
        Assert.Contains("; Application name", written);
    }

    // ── AddAppDataPath tests ───────────────────────────────────────────────────

    [Fact]
    public void AddAppDataPath_CreatesDirectory_AndSetsWriteTarget()
    {
        // Use a unique app name so the directory is isolated.
        var appName = "DappLoTestApp_" + Guid.NewGuid().ToString("N");
        var expectedDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            appName);

        try
        {
            var section = new GeneralSettingsImpl();
            var config = IniConfigRegistry.ForFile("appsettings.ini")
                .AddAppDataPath(appName)
                .RegisterSection<IGeneralSettings>(section)
                .Build();

            // Directory should have been created
            Assert.True(Directory.Exists(expectedDir));

            // LoadedFromPath should point into that directory
            Assert.NotNull(config.LoadedFromPath);
            Assert.Equal(
                Path.Combine(expectedDir, "appsettings.ini"),
                config.LoadedFromPath,
                StringComparer.OrdinalIgnoreCase);

            // Defaults should be applied because no file existed
            Assert.Equal("MyApp", section.AppName);
        }
        finally
        {
            IniConfigRegistry.Unregister("appsettings.ini");
            if (Directory.Exists(expectedDir))
                Directory.Delete(expectedDir, recursive: true);
        }
    }

    [Fact]
    public void AddAppDataPath_ThrowsWhenApplicationNameIsEmpty()
    {
        var builder = IniConfigRegistry.ForFile("x.ini");
        Assert.Throws<ArgumentException>(() => builder.AddAppDataPath(""));
        Assert.Throws<ArgumentException>(() => builder.AddAppDataPath("   "));
    }

    // ── SetWritablePath tests ──────────────────────────────────────────────────

    [Fact]
    public void SetWritablePath_UsedAsWriteTarget_WhenFileDoesNotExist()
    {
        var writeTarget = Path.Combine(_tempDir, "explicit-target.ini");

        var section = new GeneralSettingsImpl();
        var config = IniConfigRegistry.ForFile("missing.ini")
            .AddSearchPath(_tempDir)
            .SetWritablePath(writeTarget)
            .RegisterSection<IGeneralSettings>(section)
            .Build();

        // LoadedFromPath must be the explicit path, not the search-path fallback
        Assert.Equal(writeTarget, config.LoadedFromPath, StringComparer.OrdinalIgnoreCase);

        // Save should create the file at the explicit path
        config.Save();
        Assert.True(File.Exists(writeTarget));
    }

    [Fact]
    public void SetWritablePath_ThrowsWhenPathIsEmpty()
    {
        var builder = IniConfigRegistry.ForFile("x.ini");
        Assert.Throws<ArgumentException>(() => builder.SetWritablePath(""));
        Assert.Throws<ArgumentException>(() => builder.SetWritablePath("   "));
    }

    // ── Value preservation tests ───────────────────────────────────────────────

    /// <summary>
    /// A boolean property with DefaultValue=true that is explicitly set to false must
    /// survive a full save/reload cycle.  This validates that ResetToDefaults() (which
    /// resets to the compile-time default "true") is always overridden by the saved file
    /// value when the file is re-read.
    /// </summary>
    [Fact]
    public void Save_BoolDefaultTrue_SetToFalse_SurvivesSaveReload()
    {
        // Start with the default (true).
        WriteIni("bool-roundtrip.ini", "[General]\nAppName = MyApp");

        var section = new GeneralSettingsImpl();
        var config = IniConfigRegistry.ForFile("bool-roundtrip.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<IGeneralSettings>(section)
            .Build();

        // Confirm default.
        Assert.True(section.EnableLogging);

        // Explicitly set to false and save.
        section.EnableLogging = false;
        config.Save();

        // Reload from disk (simulates application restart).
        IniConfigRegistry.Unregister("bool-roundtrip.ini");
        var section2 = new GeneralSettingsImpl();
        IniConfigRegistry.ForFile("bool-roundtrip.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<IGeneralSettings>(section2)
            .Build();

        // The saved value must survive; it must NOT have been reset to the default true.
        Assert.False(section2.EnableLogging);
    }

    /// <summary>
    /// A constants file that defines only a subset of a section's properties must
    /// override exactly those properties and leave all others at their user-file values.
    /// The undefined properties in the constants file must NOT be reset to defaults.
    /// </summary>
    [Fact]
    public void Build_ConstantsFileWithPartialSection_OnlyOverridesDefinedKeys()
    {
        // User file sets all four General properties.
        WriteIni("app.ini", """
            [General]
            AppName = UserApp
            MaxRetries = 7
            EnableLogging = False
            Threshold = 1.5
            """);

        // Constants file overrides only AppName — the other three stay with user values.
        WriteIni("constants.ini", "[General]\nAppName = AdminApp");

        var section = new GeneralSettingsImpl();
        IniConfigRegistry.ForFile("app.ini")
            .AddSearchPath(_tempDir)
            .AddConstantsFile(Path.Combine(_tempDir, "constants.ini"))
            .RegisterSection<IGeneralSettings>(section)
            .Build();

        // Constants override
        Assert.Equal("AdminApp", section.AppName);

        // Properties NOT in constants file keep their user-file values (not reset to defaults).
        Assert.Equal(7, section.MaxRetries);
        Assert.False(section.EnableLogging);
        Assert.Equal(1.5, section.Threshold, precision: 10);
    }

    /// <summary>
    /// Reload() must correctly re-apply all section values from disk. Properties that
    /// were changed in memory since the last load must reflect the file state after reload,
    /// not the in-memory state.
    /// </summary>
    [Fact]
    public void Reload_ResetsInMemoryChangesToFileValues()
    {
        WriteIni("reload-values.ini", """
            [General]
            AppName = FileApp
            EnableLogging = False
            """);

        var section = new GeneralSettingsImpl();
        var config = IniConfigRegistry.ForFile("reload-values.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<IGeneralSettings>(section)
            .Build();

        Assert.Equal("FileApp", section.AppName);
        Assert.False(section.EnableLogging);

        // Change in-memory without saving.
        section.AppName = "MemoryApp";
        section.EnableLogging = true;

        // Reload must restore the file values.
        config.Reload();

        Assert.Equal("FileApp", section.AppName);
        Assert.False(section.EnableLogging);
    }
}

