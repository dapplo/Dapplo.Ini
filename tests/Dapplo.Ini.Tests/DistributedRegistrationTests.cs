// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Dapplo.Ini;
using Dapplo.Ini.Interfaces;

namespace Dapplo.Ini.Tests;

/// <summary>
/// Tests for distributed / plugin-style section registrations using the two-phase pattern:
///   Phase 1 — <see cref="IniConfigBuilder.Create"/> creates the config (no I/O).
///   Phase 2 — plugins call <see cref="IniConfig.AddSection{T}"/> (no I/O).
///   Phase 3 — <see cref="IniConfig.Load"/> / <see cref="IniConfig.LoadAsync"/> reads all
///              files once for all registered sections.
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

    // ── Two-phase: Create + AddSection + Load ───────────────────────────────────

    [Fact]
    public void CreateThenLoad_WithExistingFile_LoadsValuesForAllSections()
    {
        WriteIni("plugin.ini",
            "[General]\nAppName = HostApp\nMaxRetries = 7\n" +
            "[UserSettings]\nUsername = alice");

        var hostSection   = new GeneralSettingsImpl();
        var pluginSection = new UserSettingsImpl();

        // Phase 1 — host creates the config, registers its own section; no I/O yet
        var config = IniConfigRegistry.ForFile("plugin.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<IGeneralSettings>(hostSection)
            .Create();

        // Phase 2 — plugin pre-init registers its section; still no I/O
        config.AddSection<IUserSettings>(pluginSection);

        // Phase 3 — single load reads all sections at once
        config.Load();

        Assert.Equal("HostApp", hostSection.AppName);
        Assert.Equal(7, hostSection.MaxRetries);
        Assert.Equal("alice", pluginSection.Username);
    }

    [Fact]
    public void CreateThenLoad_WithNoFile_UsesDefaults()
    {
        var hostSection   = new GeneralSettingsImpl();
        var pluginSection = new UserSettingsImpl();

        var config = IniConfigRegistry.ForFile("nonexistent.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<IGeneralSettings>(hostSection)
            .Create();

        config.AddSection<IUserSettings>(pluginSection);
        config.Load();

        Assert.Equal("MyApp",  hostSection.AppName);
        Assert.Equal(42,       hostSection.MaxRetries);
        Assert.Equal("admin",  pluginSection.Username);
    }

    [Fact]
    public void CreateThenLoad_DoesNotMarkSectionsDirty()
    {
        WriteIni("plugin.ini", "[General]\nAppName = Clean");

        var config = IniConfigRegistry.ForFile("plugin.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<IGeneralSettings>(new GeneralSettingsImpl())
            .Create();

        config.AddSection<IUserSettings>(new UserSettingsImpl());
        config.Load();

        Assert.False(config.HasPendingChanges());
    }

    [Fact]
    public void CreateThenLoad_AppliesDefaultAndConstantFiles()
    {
        var defaultsPath  = WriteIni("defaults.ini",  "[General]\nAppName = DefaultApp\nMaxRetries = 5");
        var constantsPath = WriteIni("constants.ini", "[General]\nAppName = ForcedApp");
        WriteIni("plugin.ini", "[General]\nAppName = UserApp");

        var section = new GeneralSettingsImpl();
        var config = IniConfigRegistry.ForFile("plugin.ini")
            .AddSearchPath(_tempDir)
            .AddDefaultsFile(defaultsPath)
            .AddConstantsFile(constantsPath)
            .RegisterSection<IGeneralSettings>(section)
            .Create();

        config.Load();

        // Constants win; MaxRetries comes from defaults (user file doesn't have it)
        Assert.Equal("ForcedApp", section.AppName);
        Assert.Equal(5, section.MaxRetries);
    }

    [Fact]
    public void CreateThenLoad_AppliesValueSource()
    {
        WriteIni("plugin.ini", "[General]\nAppName = UserApp");

        var source = new DictionaryValueSource();
        source.SetValue("General", "AppName", "SourceApp");

        var section = new GeneralSettingsImpl();
        var config = IniConfigRegistry.ForFile("plugin.ini")
            .AddSearchPath(_tempDir)
            .AddValueSource(source)
            .RegisterSection<IGeneralSettings>(section)
            .Create();

        config.Load();

        Assert.Equal("SourceApp", section.AppName);
    }

    [Fact]
    public void CreateThenLoad_FiresAfterLoadHook()
    {
        WriteIni("plugin.ini", "[LifecycleSettings]\nValue = test");

        var section = new LifecycleSettingsImpl();
        var config = IniConfigRegistry.ForFile("plugin.ini")
            .AddSearchPath(_tempDir)
            .Create();

        config.AddSection<ILifecycleSettings>(section);
        config.Load();

        Assert.True(section.AfterLoadCalled);
    }

    [Fact]
    public void AddSection_BeforeLoad_ConfigIsReachableViaRegistry()
    {
        WriteIni("plugin.ini", "[General]\nAppName = Registry");

        // Create registers in the registry immediately (no I/O yet)
        IniConfigRegistry.ForFile("plugin.ini")
            .AddSearchPath(_tempDir)
            .Create();

        // Plugin retrieves the config from the registry and adds its section
        var section = new GeneralSettingsImpl();
        IniConfigRegistry.Get("plugin.ini").AddSection<IGeneralSettings>(section);

        // Then load
        IniConfigRegistry.Get("plugin.ini").Load();

        Assert.Equal("Registry", section.AppName);
    }

    // ── IniConfigRegistry.AddSection convenience overload ──────────────────────

    [Fact]
    public void RegistryAddSection_WorksBeforeLoad()
    {
        WriteIni("plugin.ini", "[General]\nAppName = RegistryPlugin");

        IniConfigRegistry.ForFile("plugin.ini")
            .AddSearchPath(_tempDir)
            .Create();

        // Plugin uses registry convenience method
        var section = new GeneralSettingsImpl();
        IniConfigRegistry.AddSection<IGeneralSettings>("plugin.ini", section);
        IniConfigRegistry.Get("plugin.ini").Load();

        Assert.Equal("RegistryPlugin", section.AppName);
    }

    [Fact]
    public void RegistryAddSection_ThrowsWhenFileNotRegistered()
    {
        var ex = Assert.Throws<KeyNotFoundException>(
            () => IniConfigRegistry.AddSection<IGeneralSettings>("missing.ini", new GeneralSettingsImpl()));

        Assert.Contains("missing.ini", ex.Message);
    }

    // ── AddSection is accessible via GetSection after Load ──────────────────────

    [Fact]
    public void AddSection_IsAvailableViaGetSection_AfterLoad()
    {
        WriteIni("plugin.ini", "[General]\nAppName = ViaGet");

        var config = IniConfigRegistry.ForFile("plugin.ini")
            .AddSearchPath(_tempDir)
            .Create();

        config.AddSection<IGeneralSettings>(new GeneralSettingsImpl());
        config.Load();

        var retrieved = config.GetSection<IGeneralSettings>();
        Assert.Equal("ViaGet", retrieved.AppName);
    }

    // ── Async Load ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateThenLoadAsync_LoadsValuesForAllSections()
    {
        WriteIni("plugin.ini",
            "[General]\nAppName = AsyncHost\nMaxRetries = 77\n" +
            "[UserSettings]\nUsername = bob");

        var hostSection   = new GeneralSettingsImpl();
        var pluginSection = new UserSettingsImpl();

        var config = IniConfigRegistry.ForFile("plugin.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<IGeneralSettings>(hostSection)
            .Create();

        config.AddSection<IUserSettings>(pluginSection);

        await config.LoadAsync();

        Assert.Equal("AsyncHost", hostSection.AppName);
        Assert.Equal(77, hostSection.MaxRetries);
        Assert.Equal("bob", pluginSection.Username);
    }

    [Fact]
    public async Task CreateThenLoadAsync_AppliesAsyncValueSource()
    {
        WriteIni("plugin.ini", "[General]\nAppName = UserApp");

        var source = new AsyncDictionaryValueSource();
        source.SetValue("General", "AppName", "AsyncSourceApp");

        var section = new GeneralSettingsImpl();
        var config = IniConfigRegistry.ForFile("plugin.ini")
            .AddSearchPath(_tempDir)
            .AddValueSource(source)
            .RegisterSection<IGeneralSettings>(section)
            .Create();

        await config.LoadAsync();

        Assert.Equal("AsyncSourceApp", section.AppName);
    }

    [Fact]
    public async Task CreateThenLoadAsync_FiresAfterLoadAsyncHook()
    {
        WriteIni("plugin.ini", "[AsyncLifecycle]\nValue = asynctest");

        var config = IniConfigRegistry.ForFile("plugin.ini")
            .AddSearchPath(_tempDir)
            .Create();

        var section = new AsyncLifecycleSettingsImpl();
        config.AddSection<IAsyncLifecycleSettings>(section);

        await config.LoadAsync();

        Assert.True(section.AfterLoadAsyncCalled);
    }

    // ── Build() remains unchanged ───────────────────────────────────────────────

    [Fact]
    public void Build_StillWorksAsBeforeForHostOnlySections()
    {
        WriteIni("plugin.ini", "[General]\nAppName = BuiltApp\nMaxRetries = 3");

        var section = new GeneralSettingsImpl();
        IniConfigRegistry.ForFile("plugin.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<IGeneralSettings>(section)
            .Build();

        Assert.Equal("BuiltApp", section.AppName);
        Assert.Equal(3, section.MaxRetries);
    }
}
