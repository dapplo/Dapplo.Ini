// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Concurrent;
using System.Runtime.Serialization;
using Dapplo.Ini;
using Dapplo.Ini.Attributes;
using Dapplo.Ini.Interfaces;
using Dapplo.Ini.Parsing;

namespace Dapplo.Ini.Tests;

// ── Sections used by the late registration tests ─────────────────────────────

/// <summary>Host section: decides which plugins to load.</summary>
[IniSection("Host")]
public interface IHostSettings : IIniSection
{
    [IniValue(DefaultValue = "true")]
    bool LoadPlugins { get; set; }

    List<string>? ExcludePlugins { get; set; }
}

/// <summary>A plugin's section, registered after the host config was loaded.</summary>
[IniSection("Plugin")]
public interface IPluginSettings : IIniSection, IAfterLoad<IPluginSettings>
{
    [IniValue(DefaultValue = "compiled")]
    string? FromDefaults { get; set; }

    [IniValue(DefaultValue = "compiled")]
    string? FromUser { get; set; }

    [IniValue(DefaultValue = "compiled")]
    string? FromConstants { get; set; }

    [IniValue(DefaultValue = "compiled")]
    string? FromSource { get; set; }

    [IniValue(DefaultValue = "42")]
    int Number { get; set; }

    [IgnoreDataMember]
    bool AfterLoadCalled { get; set; }

    static new void OnAfterLoad(IPluginSettings self) => self.AfterLoadCalled = true;
}

/// <summary>Another section type that (wrongly) uses the section name of <see cref="IPluginSettings"/>.</summary>
[IniSection("Plugin")]
public interface IPluginNameClashSettings : IIniSection
{
    string? Value { get; set; }
}

[IniSection("Concurrent1")] public interface IConcurrent1Settings : IIniSection { string? Value { get; set; } }
[IniSection("Concurrent2")] public interface IConcurrent2Settings : IIniSection { string? Value { get; set; } }
[IniSection("Concurrent3")] public interface IConcurrent3Settings : IIniSection { string? Value { get; set; } }
[IniSection("Concurrent4")] public interface IConcurrent4Settings : IIniSection { string? Value { get; set; } }
[IniSection("Concurrent5")] public interface IConcurrent5Settings : IIniSection { string? Value { get; set; } }
[IniSection("Concurrent6")] public interface IConcurrent6Settings : IIniSection { string? Value { get; set; } }

/// <summary>
/// Tests for <see cref="IniConfigBuilder.AllowLateSectionRegistration"/>,
/// <see cref="IniConfigBuilder.PreserveUnknownSections"/>, <see cref="IniConfig.TryGetSection{T}"/>
/// and section defaults before load.
/// </summary>
[Collection("IniConfigRegistry")]
public sealed class LateSectionRegistrationTests : IDisposable
{
    private readonly string _tempDir;

    public LateSectionRegistrationTests()
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

    private IniConfigBuilder PluginHostBuilder(string fileName, DictionaryValueSource? source = null)
    {
        WriteIni(fileName + "-defaults.ini", "[Plugin]\nFromDefaults = defaults\nFromUser = defaults");
        WriteIni(fileName + "-constants.ini", "[Plugin]\nFromConstants = constants");
        var builder = IniConfigRegistry.ForFile(fileName)
            .AddSearchPath(_tempDir)
            .AddDefaultsFile(fileName + "-defaults.ini")
            .AddConstantsFile(fileName + "-constants.ini")
            .RegisterSection<IHostSettings>(new HostSettingsImpl());
        if (source != null)
            builder.AddValueSource(source);
        return builder;
    }

    // ── Option off ────────────────────────────────────────────────────────────

    [Fact]
    public void AddSection_AfterLoad_WithoutOption_Throws()
    {
        using var config = IniConfigRegistry.ForFile("late-off.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<IHostSettings>(new HostSettingsImpl())
            .Build();

        var ex = Assert.Throws<InvalidOperationException>(() => config.AddSection<IPluginSettings>(new PluginSettingsImpl()));
        Assert.Contains(nameof(IniConfigBuilder.AllowLateSectionRegistration), ex.Message);
        Assert.False(config.TryGetSection<IPluginSettings>(out _));
    }

    [Fact]
    public void Save_WithoutOptions_DropsUnregisteredSections()
    {
        var path = WriteIni("drop.ini", "[Host]\nLoadPlugins = True\n\n[Plugin]\nFromUser = keep me");
        using var config = IniConfigRegistry.ForFile("drop.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<IHostSettings>(new HostSettingsImpl())
            .Build();

        config.Save();

        Assert.Null(IniFileParser.ParseFile(path).GetSection("Plugin"));
    }

    // ── Option on: the late section's life cycle ─────────────────────────────

    [Fact]
    public void AddSection_AfterLoad_AppliesAllLayersValueSourcesAndHook_WithoutReadingFiles()
    {
        WriteIni("late.ini", "[Host]\nLoadPlugins = True\n\n[Plugin]\nFromUser = user\nFromConstants = user");
        var source = new DictionaryValueSource();
        source.SetValue("Plugin", "FromSource", "source");
        using var config = PluginHostBuilder("late.ini", source)
            .AllowLateSectionRegistration()
            .Build();
        Assert.Equal(1, config.LayerReadCount);

        var plugin = config.AddSection<IPluginSettings>(new PluginSettingsImpl());

        Assert.Equal("defaults", plugin.FromDefaults);
        Assert.Equal("user", plugin.FromUser);
        Assert.Equal("constants", plugin.FromConstants);
        Assert.Equal("source", plugin.FromSource);
        Assert.Equal(42, plugin.Number);
        Assert.True(plugin.AfterLoadCalled);
        Assert.False(plugin.HasChanges);
        Assert.Equal(1, config.LayerReadCount);
        Assert.Same(plugin, config.GetSection<IPluginSettings>());
    }

    [Fact]
    public void AddSection_AfterLoad_ConstantKeyIsProtected()
    {
        WriteIni("late-const.ini", "[Host]");
        using var config = PluginHostBuilder("late-const.ini")
            .AllowLateSectionRegistration()
            .Build();

        var plugin = config.AddSection<IPluginSettings>(new PluginSettingsImpl());

        Assert.True(plugin.IsConstant("FromConstants"));
        Assert.Throws<AccessViolationException>(() => plugin.FromConstants = "changed");
        Assert.Equal("constants", plugin.FromConstants);
    }

    [Fact]
    public void HostSettings_DecideWhichPluginsRegister()
    {
        WriteIni("decide.ini", "[Host]\nLoadPlugins = False");
        using var config = PluginHostBuilder("decide.ini")
            .AllowLateSectionRegistration()
            .Build();

        // The host setting is loaded before any plugin registers — the chicken-and-egg case.
        var host = config.GetSection<IHostSettings>();
        Assert.False(host.LoadPlugins);
        if (host.LoadPlugins)
            config.AddSection<IPluginSettings>(new PluginSettingsImpl());

        Assert.False(config.TryGetSection<IPluginSettings>(out _));
    }

    [Fact]
    public void Reload_UpdatesHostAndLateSections_AndLaterRegistrationsSeeNewData()
    {
        var path = WriteIni("late-reload.ini", "[Host]\nLoadPlugins = True\n\n[Plugin]\nFromUser = first");
        using var config = PluginHostBuilder("late-reload.ini")
            .AllowLateSectionRegistration()
            .Build();
        var plugin = config.AddSection<IPluginSettings>(new PluginSettingsImpl());

        File.WriteAllText(path, "[Host]\nLoadPlugins = False\n\n[Plugin]\nFromUser = second\n\n[Concurrent1]\nValue = after reload");
        config.Reload();

        Assert.False(config.GetSection<IHostSettings>().LoadPlugins);
        Assert.Equal("second", plugin.FromUser);
        Assert.Equal("constants", plugin.FromConstants);
        var later = config.AddSection<IConcurrent1Settings>(new Concurrent1SettingsImpl());
        Assert.Equal("after reload", later.Value);
    }

    [Fact]
    public void AddSection_SameTypeAfterLoad_Throws()
    {
        WriteIni("dup.ini", "[Host]");
        using var config = PluginHostBuilder("dup.ini").AllowLateSectionRegistration().Build();
        config.AddSection<IPluginSettings>(new PluginSettingsImpl());

        Assert.Throws<InvalidOperationException>(() => config.AddSection<IPluginSettings>(new PluginSettingsImpl()));
    }

    [Fact]
    public void AddSection_DifferentTypeWithSameSectionName_Throws()
    {
        WriteIni("clash.ini", "[Host]");
        using var config = PluginHostBuilder("clash.ini").AllowLateSectionRegistration().Build();
        config.AddSection<IPluginSettings>(new PluginSettingsImpl());

        Assert.Throws<InvalidOperationException>(() => config.AddSection<IPluginNameClashSettings>(new PluginNameClashSettingsImpl()));
    }

    [Fact]
    public void AddSection_SameTypeBeforeLoad_StillReplaces()
    {
        var config = IniConfigRegistry.ForFile("replace.ini").AddSearchPath(_tempDir).Create();
        config.AddSection<IPluginSettings>(new PluginSettingsImpl());
        var second = config.AddSection<IPluginSettings>(new PluginSettingsImpl());
        config.Load();

        Assert.Same(second, config.GetSection<IPluginSettings>());
    }

    // ── Save keeps what nobody registered ─────────────────────────────────────

    [Fact]
    public void Save_WithLateRegistration_KeepsUnregisteredSectionsInPlaceWithComments()
    {
        var path = WriteIni("keep.ini",
            "; host comment\n[Host]\nLoadPlugins = True\n\n; plugin comment\n[Plugin]\n; key comment\nFromUser = keep me\nUnknownKey = also kept\n\n[Other]\nX = 1");
        var host = new HostSettingsImpl();
        using var config = IniConfigRegistry.ForFile("keep.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<IHostSettings>(host)
            .AllowLateSectionRegistration()
            .Build();

        host.LoadPlugins = false;
        config.Save();

        var saved = IniFileParser.ParseFile(path);
        Assert.Equal(new[] { "Host", "Plugin", "Other" }, saved.Sections.Select(s => s.Name));
        Assert.Equal("False", saved.GetSection("Host")!.GetValue("LoadPlugins"));
        Assert.Equal("keep me", saved.GetSection("Plugin")!.GetValue("FromUser"));
        Assert.Equal("also kept", saved.GetSection("Plugin")!.GetValue("UnknownKey"));
        Assert.Equal(new[] { "key comment" }, saved.GetSection("Plugin")!.GetEntry("FromUser")!.Comments);
        Assert.Equal(new[] { "plugin comment" }, saved.GetSection("Plugin")!.Comments);
        Assert.Equal(new[] { "host comment" }, saved.GetSection("Host")!.Comments);
    }

    [Fact]
    public void Save_WithPreserveUnknownSectionsOnly_KeepsUnregisteredSections()
    {
        var path = WriteIni("preserve.ini", "[Host]\nLoadPlugins = True\n\n[Plugin]\nFromUser = keep me");
        using var config = IniConfigRegistry.ForFile("preserve.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<IHostSettings>(new HostSettingsImpl())
            .PreserveUnknownSections()
            .Build();

        config.Save();

        Assert.Equal("keep me", IniFileParser.ParseFile(path).GetSection("Plugin")!.GetValue("FromUser"));
        // Preservation alone does not enable late registration.
        Assert.Throws<InvalidOperationException>(() => config.AddSection<IPluginSettings>(new PluginSettingsImpl()));
    }

    [Fact]
    public void Save_RegisteredSection_DropsUndeclaredKeys_SoMigrationsCleanUp()
    {
        var path = WriteIni("migrate.ini", "[Host]\nLoadPlugins = True\nOldKey = gone after save");
        using var config = IniConfigRegistry.ForFile("migrate.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<IHostSettings>(new HostSettingsImpl())
            .PreserveUnknownSections()
            .Build();

        config.Save();

        Assert.Null(IniFileParser.ParseFile(path).GetSection("Host")!.GetValue("OldKey"));
    }

    [Fact]
    public void AddSection_AfterSave_SeesTheDataThatIsOnDisk()
    {
        WriteIni("after-save.ini", "[Host]\nLoadPlugins = True\n\n[Concurrent2]\nValue = preserved through the save");
        var host = new HostSettingsImpl();
        using var config = IniConfigRegistry.ForFile("after-save.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<IHostSettings>(host)
            .AllowLateSectionRegistration()
            .Build();
        host.LoadPlugins = false;
        config.Save();

        var later = config.AddSection<IConcurrent2Settings>(new Concurrent2SettingsImpl());

        Assert.Equal("preserved through the save", later.Value);
        Assert.Equal(1, config.LayerReadCount);
    }

    [Fact]
    public void Save_WithMetadataAndGlobalKeys_KeepsGlobalSectionFirst()
    {
        var path = WriteIni("global.ini", "TopLevel = 1\n[Host]\nLoadPlugins = True");
        using var config = IniConfigRegistry.ForFile("global.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<IHostSettings>(new HostSettingsImpl())
            .PreserveUnknownSections()
            .EnableMetadata("1.0", "test")
            .Build();

        config.Save();

        var saved = IniFileParser.ParseFile(path);
        Assert.Equal("1", saved.GetSection(string.Empty)!.GetValue("TopLevel"));
        Assert.Null(saved.GetSection("__metadata__")!.GetValue("TopLevel"));
    }

    // ── TryGetSection ─────────────────────────────────────────────────────────

    [Fact]
    public void TryGetSection_ReturnsRegisteredAndReportsMissing()
    {
        using var config = IniConfigRegistry.ForFile("try.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<IHostSettings>(new HostSettingsImpl())
            .Build();

        Assert.True(config.TryGetSection<IHostSettings>(out var host));
        Assert.NotNull(host);
        Assert.False(config.TryGetSection<IPluginSettings>(out var plugin));
        Assert.Null(plugin);

        Assert.True(IniConfigRegistry.TryGetSection<IHostSettings>("try.ini", out _));
        Assert.False(IniConfigRegistry.TryGetSection<IPluginSettings>("try.ini", out _));
        Assert.False(IniConfigRegistry.TryGetSection<IHostSettings>("missing.ini", out _));
        Assert.True(IniConfigRegistry.TryGetSection<IHostSettings>(out _));
        Assert.False(IniConfigRegistry.TryGetSection<IPluginSettings>(out _));
    }

    // ── Defaults before load ──────────────────────────────────────────────────

    [Fact]
    public void Sections_BeforeLoad_ReturnTheirDefaultValues()
    {
        var host = new HostSettingsImpl();
        var config = IniConfigRegistry.ForFile("before.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<IHostSettings>(host)
            .Create();
        var plugin = config.AddSection<IPluginSettings>(new PluginSettingsImpl());

        Assert.True(host.LoadPlugins);
        Assert.Equal(42, plugin.Number);
        Assert.Equal("compiled", plugin.FromUser);
    }

    // ── Listener ──────────────────────────────────────────────────────────────

    [Fact]
    public void SectionListener_IsNotifiedForRegistrationsBeforeAndAfterLoad()
    {
        WriteIni("listen.ini", "[Host]");
        var listener = new SectionListener();
        var config = PluginHostBuilder("listen.ini")
            .AllowLateSectionRegistration()
            .AddListener(listener)
            .Create();
        config.AddSection<IConcurrent1Settings>(new Concurrent1SettingsImpl());
        config.Load();
        config.AddSection<IPluginSettings>(new PluginSettingsImpl());

        Assert.Equal(new[] { ("Concurrent1", false), ("Plugin", true) }, listener.Added);
    }

    // ── Async ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AddSectionAsync_AfterLoad_AppliesAsyncSourcesAndAsyncHook()
    {
        WriteIni("late-async.ini", "[Host]");
        var asyncSource = new AsyncDictionaryValueSource();
        asyncSource.SetValue("AsyncLifecycle", "Value", "from async source");
        var config = await IniConfigRegistry.ForFile("late-async.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<IHostSettings>(new HostSettingsImpl())
            .AddValueSource(asyncSource)
            .AllowLateSectionRegistration()
            .BuildAsync();

        var section = await config.AddSectionAsync<IAsyncLifecycleSettings>(new AsyncLifecycleSettingsImpl());

        Assert.Equal("from async source", section.Value);
        Assert.True(((AsyncLifecycleSettingsImpl)section).AfterLoadAsyncCalled);
        Assert.False(section.HasChanges);
        Assert.Equal(1, config.LayerReadCount);
        config.Dispose();
    }

    [Fact]
    public async Task AddSectionAsync_Cancelled_DoesNotRegister()
    {
        WriteIni("late-cancel.ini", "[Host]");
        using var config = PluginHostBuilder("late-cancel.ini").AllowLateSectionRegistration().Build();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => config.AddSectionAsync<IPluginSettings>(new PluginSettingsImpl(), cts.Token));

        Assert.False(config.TryGetSection<IPluginSettings>(out _));
    }

    // ── Concurrency ───────────────────────────────────────────────────────────

    [Fact]
    public async Task LateRegistrationFromManyThreads_WhileSaving_RegistersAndSavesEverything()
    {
        var path = WriteIni("concurrent.ini", "[Host]\nLoadPlugins = True");
        var host = new HostSettingsImpl();
        using var config = IniConfigRegistry.ForFile("concurrent.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<IHostSettings>(host)
            .AllowLateSectionRegistration()
            .Build();

        using var stop = new CancellationTokenSource();
        var saver = Task.Run(() =>
        {
            var i = 0;
            while (!stop.IsCancellationRequested)
            {
                host.ExcludePlugins = new List<string> { "p" + i++ };
                config.Save();
            }
        });

        var registrations = new Func<IIniSection>[]
        {
            () => config.AddSection<IConcurrent1Settings>(new Concurrent1SettingsImpl { }),
            () => config.AddSection<IConcurrent2Settings>(new Concurrent2SettingsImpl()),
            () => config.AddSection<IConcurrent3Settings>(new Concurrent3SettingsImpl()),
            () => config.AddSection<IConcurrent4Settings>(new Concurrent4SettingsImpl()),
            () => config.AddSection<IConcurrent5Settings>(new Concurrent5SettingsImpl()),
            () => config.AddSection<IConcurrent6Settings>(new Concurrent6SettingsImpl()),
        };
        var added = new ConcurrentBag<IIniSection>();
        await Task.WhenAll(registrations.Select(register => Task.Run(() =>
        {
            var section = register();
            section.SetRawValue("Value", section.SectionName);
            added.Add(section);
        })));

        stop.Cancel();
        await saver;
        config.Save();

        Assert.Equal(7, config.GetSections().Count());
        var saved = IniFileParser.ParseFile(path);
        foreach (var section in added)
            Assert.Equal(section.SectionName, saved.GetSection(section.SectionName)!.GetValue("Value"));
    }

    private sealed class SectionListener : IIniConfigListener, IIniConfigSectionListener
    {
        public List<(string, bool)> Added { get; } = new();
        public void OnSectionAdded(string sectionName, bool loaded) => Added.Add((sectionName, loaded));
        public void OnFileLoaded(string filePath) { }
        public void OnFileNotFound(string fileName) { }
        public void OnSaved(string filePath) { }
        public void OnReloaded(string filePath) { }
        public void OnError(string operation, Exception exception) { }
        public void OnUnknownKey(string sectionName, string key, string? rawValue) { }
        public void OnValueConversionFailed(string sectionName, string key, string? rawValue, Exception exception) { }
    }
}
