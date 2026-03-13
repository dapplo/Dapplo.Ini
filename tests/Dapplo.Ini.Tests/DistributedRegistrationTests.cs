// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Dapplo.Ini;
using Dapplo.Ini.Interfaces;

namespace Dapplo.Ini.Tests;

/// <summary>
/// Tests for distributed / plugin-style section registrations.
/// Verifies that sections can be registered on an already-built <see cref="IniConfig"/>
/// and that they receive the correct layered values (defaults → user file → constants → value sources).
/// </summary>
[Collection("IniConfigRegistry")]
public sealed class DistributedRegistrationTests : IDisposable
{
    private readonly string _tempDir;

    public DistributedRegistrationTests()
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

    // ── RegisterSection on IniConfig ────────────────────────────────────────────

    [Fact]
    public void RegisterSection_AfterBuild_LoadsValuesFromFile()
    {
        WriteIni("plugin.ini", "[General]\nAppName = PluginApp\nMaxRetries = 99");

        var config = IniConfigRegistry.ForFile("plugin.ini")
            .AddSearchPath(_tempDir)
            .Build();

        // Simulate a plugin registering its section after the host has already called Build().
        var pluginSection = new GeneralSettingsImpl();
        config.RegisterSection<IGeneralSettings>(pluginSection);

        Assert.Equal("PluginApp", pluginSection.AppName);
        Assert.Equal(99, pluginSection.MaxRetries);
    }

    [Fact]
    public void RegisterSection_AfterBuild_UsesDefaults_WhenKeyMissing()
    {
        WriteIni("plugin.ini", "[General]\nAppName = PluginApp");

        var config = IniConfigRegistry.ForFile("plugin.ini")
            .AddSearchPath(_tempDir)
            .Build();

        var pluginSection = new GeneralSettingsImpl();
        config.RegisterSection<IGeneralSettings>(pluginSection);

        // MaxRetries was not in the file — should fall back to the compiled default (42).
        Assert.Equal("PluginApp", pluginSection.AppName);
        Assert.Equal(42, pluginSection.MaxRetries);
    }

    [Fact]
    public void RegisterSection_AfterBuild_ReturnsSection_ForFluentChaining()
    {
        WriteIni("plugin.ini", "[General]\nAppName = Chained");

        var config = IniConfigRegistry.ForFile("plugin.ini")
            .AddSearchPath(_tempDir)
            .Build();

        var section = config.RegisterSection<IGeneralSettings>(new GeneralSettingsImpl());

        Assert.NotNull(section);
        Assert.Equal("Chained", section.AppName);
    }

    [Fact]
    public void RegisterSection_AfterBuild_AvailableViaGetSection()
    {
        WriteIni("plugin.ini", "[General]\nAppName = ViaGet");

        var config = IniConfigRegistry.ForFile("plugin.ini")
            .AddSearchPath(_tempDir)
            .Build();

        config.RegisterSection<IGeneralSettings>(new GeneralSettingsImpl());

        var retrieved = config.GetSection<IGeneralSettings>();
        Assert.Equal("ViaGet", retrieved.AppName);
    }

    [Fact]
    public void RegisterSection_AfterBuild_DoesNotMarkSectionDirty()
    {
        WriteIni("plugin.ini", "[General]\nAppName = Clean");

        var config = IniConfigRegistry.ForFile("plugin.ini")
            .AddSearchPath(_tempDir)
            .Build();

        config.RegisterSection<IGeneralSettings>(new GeneralSettingsImpl());

        // Loading should not produce unsaved changes.
        Assert.False(config.HasPendingChanges());
    }

    [Fact]
    public void RegisterSection_AfterBuild_AppliesDefaultFile()
    {
        var defaultsPath = WriteIni("defaults.ini", "[General]\nAppName = DefaultApp\nMaxRetries = 5");
        WriteIni("plugin.ini", "[General]\nAppName = UserApp");

        var config = IniConfigRegistry.ForFile("plugin.ini")
            .AddSearchPath(_tempDir)
            .AddDefaultsFile(defaultsPath)
            .Build();

        var section = config.RegisterSection<IGeneralSettings>(new GeneralSettingsImpl());

        // User file wins over defaults for AppName; MaxRetries comes from defaults.
        Assert.Equal("UserApp", section.AppName);
        Assert.Equal(5, section.MaxRetries);
    }

    [Fact]
    public void RegisterSection_AfterBuild_AppliesConstantsFile()
    {
        var constantsPath = WriteIni("constants.ini", "[General]\nAppName = ForcedApp");
        WriteIni("plugin.ini", "[General]\nAppName = UserApp");

        var config = IniConfigRegistry.ForFile("plugin.ini")
            .AddSearchPath(_tempDir)
            .AddConstantsFile(constantsPath)
            .Build();

        var section = config.RegisterSection<IGeneralSettings>(new GeneralSettingsImpl());

        // Constants always win.
        Assert.Equal("ForcedApp", section.AppName);
    }

    [Fact]
    public void RegisterSection_AfterBuild_AppliesValueSource()
    {
        WriteIni("plugin.ini", "[General]\nAppName = UserApp");

        var source = new DictionaryValueSource();
        source.SetValue("General", "AppName", "SourceApp");

        var config = IniConfigRegistry.ForFile("plugin.ini")
            .AddSearchPath(_tempDir)
            .AddValueSource(source)
            .Build();

        var section = config.RegisterSection<IGeneralSettings>(new GeneralSettingsImpl());

        Assert.Equal("SourceApp", section.AppName);
    }

    [Fact]
    public void RegisterSection_AfterBuild_FiresAfterLoadHook()
    {
        WriteIni("plugin.ini", "[LifecycleSettings]\nValue = test");

        var config = IniConfigRegistry.ForFile("plugin.ini")
            .AddSearchPath(_tempDir)
            .Build();

        var section = new LifecycleSettingsImpl();
        config.RegisterSection<ILifecycleSettings>(section);

        Assert.True(section.AfterLoadCalled);
    }

    [Fact]
    public void RegisterSection_WithNoFile_UsesDefaults()
    {
        // No INI file exists — section should receive compiled-in defaults.
        var config = IniConfigRegistry.ForFile("nonexistent.ini")
            .AddSearchPath(_tempDir)
            .Build();

        var section = config.RegisterSection<IGeneralSettings>(new GeneralSettingsImpl());

        Assert.Equal("MyApp", section.AppName);
        Assert.Equal(42, section.MaxRetries);
        Assert.True(section.EnableLogging);
    }

    // ── IniConfigRegistry.RegisterSection convenience overload ─────────────────

    [Fact]
    public void RegistryRegisterSection_LoadsValuesFromFile()
    {
        WriteIni("plugin.ini", "[General]\nAppName = RegistryPlugin");

        IniConfigRegistry.ForFile("plugin.ini")
            .AddSearchPath(_tempDir)
            .Build();

        var section = IniConfigRegistry.RegisterSection<IGeneralSettings>(
            "plugin.ini", new GeneralSettingsImpl());

        Assert.Equal("RegistryPlugin", section.AppName);
    }

    [Fact]
    public void RegistryRegisterSection_ThrowsWhenFileNotRegistered()
    {
        var ex = Assert.Throws<KeyNotFoundException>(
            () => IniConfigRegistry.RegisterSection<IGeneralSettings>(
                "missing.ini", new GeneralSettingsImpl()));

        Assert.Contains("missing.ini", ex.Message);
    }

    // ── Async overloads ────────────────────────────────────────────────────────

    [Fact]
    public async Task RegisterSectionAsync_AfterBuild_LoadsValuesFromFile()
    {
        WriteIni("plugin.ini", "[General]\nAppName = AsyncPlugin\nMaxRetries = 77");

        var config = IniConfigRegistry.ForFile("plugin.ini")
            .AddSearchPath(_tempDir)
            .Build();

        var section = await config.RegisterSectionAsync<IGeneralSettings>(new GeneralSettingsImpl());

        Assert.Equal("AsyncPlugin", section.AppName);
        Assert.Equal(77, section.MaxRetries);
    }

    [Fact]
    public async Task RegisterSectionAsync_AfterBuild_AppliesAsyncValueSource()
    {
        WriteIni("plugin.ini", "[General]\nAppName = UserApp");

        var source = new AsyncDictionaryValueSource();
        source.SetValue("General", "AppName", "AsyncSourceApp");

        var config = IniConfigRegistry.ForFile("plugin.ini")
            .AddSearchPath(_tempDir)
            .AddValueSource(source)
            .Build();

        var section = await config.RegisterSectionAsync<IGeneralSettings>(new GeneralSettingsImpl());

        Assert.Equal("AsyncSourceApp", section.AppName);
    }

    [Fact]
    public async Task RegisterSectionAsync_AfterBuild_FiresAfterLoadAsyncHook()
    {
        WriteIni("plugin.ini", "[AsyncLifecycle]\nValue = asynctest");

        var config = IniConfigRegistry.ForFile("plugin.ini")
            .AddSearchPath(_tempDir)
            .Build();

        var section = new AsyncLifecycleSettingsImpl();
        await config.RegisterSectionAsync<IAsyncLifecycleSettings>(section);

        // The async after-load hook sets AfterLoadAsyncCalled = true.
        Assert.True(section.AfterLoadAsyncCalled);
    }

    [Fact]
    public async Task RegistryRegisterSectionAsync_LoadsValuesFromFile()
    {
        WriteIni("plugin.ini", "[General]\nAppName = AsyncRegistryPlugin");

        IniConfigRegistry.ForFile("plugin.ini")
            .AddSearchPath(_tempDir)
            .Build();

        var section = await IniConfigRegistry.RegisterSectionAsync<IGeneralSettings>(
            "plugin.ini", new GeneralSettingsImpl());

        Assert.Equal("AsyncRegistryPlugin", section.AppName);
    }

    // ── Multiple plugins sharing the same IniConfig ────────────────────────────

    [Fact]
    public void RegisterSection_MultipleSections_IndependentRegistration()
    {
        WriteIni("shared.ini",
            "[General]\nAppName = SharedHost\n" +
            "[UserSettings]\nUsername = shareduser");

        var config = IniConfigRegistry.ForFile("shared.ini")
            .AddSearchPath(_tempDir)
            .Build();

        // Plugin A registers General settings
        var generalSection = config.RegisterSection<IGeneralSettings>(new GeneralSettingsImpl());

        // Plugin B registers User settings
        var userSection = config.RegisterSection<IUserSettings>(new UserSettingsImpl());

        Assert.Equal("SharedHost", generalSection.AppName);
        Assert.Equal("shareduser", userSection.Username);
    }
}
