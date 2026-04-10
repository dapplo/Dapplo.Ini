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

    // ── Round-trip preservation tests ─────────────────────────────────────────

    /// <summary>
    /// Keys that exist in the INI file but are not declared on the section interface
    /// (e.g. written by a newer version of the app) must survive a save/load cycle
    /// unchanged so that no user data is silently discarded.
    /// </summary>
    [Fact]
    public void Save_PreservesUnknownKeysWithinRegisteredSection()
    {
        // The INI file contains "FutureKey" which is not declared on IGeneralSettings.
        WriteIni("unknown-keys.ini", """
            [General]
            AppName = Hello
            MaxRetries = 3
            FutureKey = preserved-value
            AnotherFutureKey = 42
            """);

        var section = new GeneralSettingsImpl();
        var config = IniConfigRegistry.ForFile("unknown-keys.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<IGeneralSettings>(section)
            .Build();

        // Modify a known key and save
        section.AppName = "Modified";
        config.Save();

        // Read back the raw file content
        var written = File.ReadAllText(Path.Combine(_tempDir, "unknown-keys.ini"));

        // Known property must be updated
        Assert.Contains("AppName = Modified", written);
        // Unknown keys from the original file must be preserved
        Assert.Contains("FutureKey = preserved-value", written);
        Assert.Contains("AnotherFutureKey = 42", written);
    }

    /// <summary>
    /// INI file sections that are not registered with the current IniConfig (e.g. sections
    /// belonging to plugins that are not loaded, or sections added by a newer version of the
    /// application) must survive a save/load cycle so that no data is silently discarded.
    /// </summary>
    [Fact]
    public void Save_PreservesUnregisteredSections()
    {
        // The INI file contains [General] (registered) and [Plugin] (not registered).
        WriteIni("unregistered-section.ini", """
            [General]
            AppName = MyApp
            [Plugin]
            PluginKey = plugin-value
            PluginNumber = 99
            """);

        var section = new GeneralSettingsImpl();
        var config = IniConfigRegistry.ForFile("unregistered-section.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<IGeneralSettings>(section)
            .Build();

        // Modify the registered section and save
        section.AppName = "Updated";
        config.Save();

        // Read back the raw file content
        var written = File.ReadAllText(Path.Combine(_tempDir, "unregistered-section.ini"));

        // Registered section must be updated
        Assert.Contains("AppName = Updated", written);
        // Unregistered section must be preserved verbatim
        Assert.Contains("[Plugin]", written);
        Assert.Contains("PluginKey = plugin-value", written);
        Assert.Contains("PluginNumber = 99", written);
    }

    /// <summary>
    /// After a Reload(), unknown keys that are no longer present in the file should not be
    /// re-written on the next save (i.e. the user's explicit removal is respected).
    /// </summary>
    [Fact]
    public void Save_DoesNotRestoreUnknownKeysRemovedFromFile()
    {
        // Initial file with an unknown key
        var iniPath = WriteIni("removed-unknown-key.ini", """
            [General]
            AppName = Original
            FutureKey = will-be-removed
            """);

        var section = new GeneralSettingsImpl();
        var config = IniConfigRegistry.ForFile("removed-unknown-key.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<IGeneralSettings>(section)
            .Build();

        // Simulate external removal of the unknown key between the initial load and the reload
        File.WriteAllText(iniPath, """
            [General]
            AppName = Original
            """);

        // Reload picks up the new file (FutureKey is gone)
        config.Reload();

        // Save should not bring FutureKey back
        config.Save();
        var written = File.ReadAllText(iniPath);
        Assert.DoesNotContain("FutureKey", written);
    }

    /// <summary>
    /// After a Reload(), unregistered sections that are no longer present in the file should
    /// not be re-written on the next save.
    /// </summary>
    [Fact]
    public void Save_DoesNotRestoreUnregisteredSectionsRemovedFromFile()
    {
        var iniPath = WriteIni("removed-section.ini", """
            [General]
            AppName = Original
            [Plugin]
            PluginKey = value
            """);

        var section = new GeneralSettingsImpl();
        var config = IniConfigRegistry.ForFile("removed-section.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<IGeneralSettings>(section)
            .Build();

        // Simulate external removal of the [Plugin] section
        File.WriteAllText(iniPath, """
            [General]
            AppName = Original
            """);

        config.Reload();
        config.Save();

        var written = File.ReadAllText(iniPath);
        Assert.DoesNotContain("[Plugin]", written);
        Assert.DoesNotContain("PluginKey", written);
    }
}
