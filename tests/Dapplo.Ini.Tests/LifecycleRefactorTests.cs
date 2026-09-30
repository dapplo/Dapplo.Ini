// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.ComponentModel;
using Dapplo.Ini;
using Dapplo.Ini.Attributes;
using Dapplo.Ini.Configuration;
using Dapplo.Ini.Interfaces;
using Dapplo.Ini.Parsing;

namespace Dapplo.Ini.Tests;

/// <summary>Section whose IAfterLoad hook migrates one value and re-assigns another unchanged.</summary>
[IniSection("Normalizing")]
public interface INormalizingSettings : IIniSection, IAfterLoad<INormalizingSettings>
{
    string? Legacy { get; set; }

    [IniValue(DefaultValue = "same")]
    string? Stable { get; set; }

    static new void OnAfterLoad(INormalizingSettings self)
    {
        if (self.Legacy == "old")
            self.Legacy = "migrated";
        self.Stable = self.Stable; // unchanged: must not make the section dirty
    }
}

/// <summary>Empty base section interface, to test key resolution for derived interfaces.</summary>
public interface IResolveBaseSettings : IIniSection
{
}

/// <summary>Derived section interface; a concrete instance must register under this type.</summary>
[IniSection("Resolve")]
public interface IResolveDerivedSettings : IResolveBaseSettings
{
    string? Value { get; set; }
}

/// <summary>Transactional section with change notifications.</summary>
[IniSection("TxNpc")]
public interface ITxNpcSettings : IIniSection, ITransactional, INotifyPropertyChanged
{
    [IniValue(DefaultValue = "a", Transactional = true)]
    string? Name { get; set; }
}

/// <summary>
/// Tests for the lifecycle refactoring: InitialLoadTask, dirty tracking around hooks and saves,
/// re-entrance rules, registry hygiene, section key resolution and transactions.
/// </summary>
[Collection("IniConfigRegistry")]
public sealed class LifecycleRefactorTests : IDisposable
{
    private readonly string _tempDir;

    public LifecycleRefactorTests()
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

    // ── InitialLoadTask ───────────────────────────────────────────────────────

    [Fact]
    public async Task InitialLoadTask_AfterCreate_CompletesWhenLoadAsyncHasRun()
    {
        WriteIni("task-create.ini", "[ReloadSection]\nValue = loaded");
        var section = new ReloadSettingsImpl();
        var config = IniConfigRegistry.ForFile("task-create.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<IReloadSettings>(section)
            .Create();

        Assert.False(config.InitialLoadTask.IsCompleted);
        Assert.False(config.IsLoaded);

        await config.LoadAsync();

        Assert.True(config.InitialLoadTask.IsCompleted);
        Assert.True(config.IsLoaded);
        Assert.Equal("loaded", section.Value);
    }

    [Fact]
    public void InitialLoadTask_AfterCreateAndLoad_IsCompleted()
    {
        var config = IniConfigRegistry.ForFile("task-sync.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<IReloadSettings>(new ReloadSettingsImpl())
            .Create();

        config.Load();

        Assert.True(config.InitialLoadTask.IsCompleted);
    }

    // ── Dirty tracking ────────────────────────────────────────────────────────

    [Fact]
    public void AfterLoadHook_ChangesStayDirty_UnchangedAssignmentsDoNot()
    {
        WriteIni("normalize.ini", "[Normalizing]\nLegacy = old");
        var section = new NormalizingSettingsImpl();
        using var config = IniConfigRegistry.ForFile("normalize.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<INormalizingSettings>(section)
            .Build();

        Assert.Equal("migrated", section.Legacy);
        Assert.True(section.HasChanges);
        Assert.True(config.HasPendingChanges());
    }

    [Fact]
    public void AfterLoadHook_WithoutChanges_LeavesSectionClean()
    {
        WriteIni("normalize-clean.ini", "[Normalizing]\nLegacy = new");
        var section = new NormalizingSettingsImpl();
        using var config = IniConfigRegistry.ForFile("normalize-clean.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<INormalizingSettings>(section)
            .Build();

        Assert.False(section.HasChanges);
    }

    [Fact]
    public void Setter_AssigningTheSameValue_DoesNotMarkDirty()
    {
        var section = new ReloadSettingsImpl();
        using var config = IniConfigRegistry.ForFile("same-value.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<IReloadSettings>(section)
            .Build();

        section.Value = section.Value;

        Assert.False(section.HasChanges);
    }

    [Fact]
    public void MarkSaved_WithOlderVersion_KeepsLaterChangesDirty()
    {
        var section = new ReloadSettingsImpl();
        section.Value = "first";
        var versionAtSave = section.ChangeVersion;
        section.Value = "changed while saving";

        section.MarkSaved(versionAtSave);

        Assert.True(section.HasChanges);
        section.MarkSaved(section.ChangeVersion);
        Assert.False(section.HasChanges);
    }

    // ── Load twice ────────────────────────────────────────────────────────────

    [Fact]
    public void Load_CalledTwice_RereadsTheFile()
    {
        var path = WriteIni("twice.ini", "[ReloadSection]\nValue = one");
        var section = new ReloadSettingsImpl();
        using var config = IniConfigRegistry.ForFile("twice.ini")
            .AddSearchPath(_tempDir)
            .AutoSaveInterval(TimeSpan.FromMinutes(10))
            .RegisterSection<IReloadSettings>(section)
            .Build();

        File.WriteAllText(path, "[ReloadSection]\nValue = two");
        config.Load();

        Assert.Equal("two", section.Value);
        Assert.True(config.IsLoaded);
    }

    // ── Re-entrance ───────────────────────────────────────────────────────────

    [Fact]
    public void Reload_FromListenerDuringLoad_ThrowsInsteadOfDeadlocking()
    {
        WriteIni("reentrant.ini", "[ReloadSection]\nValue = x");
        var listener = new CallbackListener();
        var config = IniConfigRegistry.ForFile("reentrant.ini")
            .AddSearchPath(_tempDir)
            .AddListener(listener)
            .RegisterSection<IReloadSettings>(new ReloadSettingsImpl())
            .Create();
        listener.FileLoaded = () => config.Reload();

        Assert.Throws<InvalidOperationException>(() => config.Load());
    }

    [Fact]
    public void Save_FromListenerDuringLoad_WritesTheFile()
    {
        var path = WriteIni("reentrant-save.ini", "[ReloadSection]\nValue = x");
        var listener = new CallbackListener();
        var config = IniConfigRegistry.ForFile("reentrant-save.ini")
            .AddSearchPath(_tempDir)
            .AddListener(listener)
            .RegisterSection<IReloadSettings>(new ReloadSettingsImpl())
            .Create();
        listener.FileLoaded = () =>
        {
            config.GetSection<IReloadSettings>().Value = "saved from listener";
            config.Save();
        };

        config.Load();

        Assert.Contains("saved from listener", File.ReadAllText(path));
    }

    // ── Concurrency ───────────────────────────────────────────────────────────

    [Fact]
    public async Task SaveReloadAndSetters_InParallel_DoNotThrow()
    {
        var path = WriteIni("parallel.ini", "[ReloadSection]\nValue = start");
        var section = new ReloadSettingsImpl();
        using var config = IniConfigRegistry.ForFile("parallel.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<IReloadSettings>(section)
            .Build();

        var until = DateTime.UtcNow.AddMilliseconds(750);
        var tasks = new[]
        {
            Task.Run(() => { var i = 0; while (DateTime.UtcNow < until) section.Value = "v" + i++; }),
            Task.Run(() => { while (DateTime.UtcNow < until) config.Save(); }),
            Task.Run(() => { while (DateTime.UtcNow < until) config.Reload(); }),
            Task.Run(async () => { while (DateTime.UtcNow < until) await config.SaveAsync(); }),
        };
        await Task.WhenAll(tasks);

        var parsed = IniFileParser.ParseFile(path);
        Assert.NotNull(parsed.GetSection("ReloadSection"));
    }

    // ── Registry hygiene ──────────────────────────────────────────────────────

    [Fact]
    public void Build_WhenLoadFails_RemovesTheRegistration()
    {
        WriteIni("broken.ini", "[ReloadSection]\nValue = a\nValue = b");

        Assert.ThrowsAny<Exception>(() => IniConfigRegistry.ForFile("broken.ini")
            .AddSearchPath(_tempDir)
            .WithDuplicateKeyHandling(DuplicateKeyHandling.ThrowError)
            .RegisterSection<IReloadSettings>(new ReloadSettingsImpl())
            .Build());

        Assert.False(IniConfigRegistry.TryGet("broken.ini", out _));
    }

    [Fact]
    public async Task BuildAsync_WhenLoadFails_FaultsInitialLoadTaskAndRemovesTheRegistration()
    {
        WriteIni("broken-async.ini", "[ReloadSection]\nValue = a\nValue = b");
        var builder = IniConfigRegistry.ForFile("broken-async.ini")
            .AddSearchPath(_tempDir)
            .WithDuplicateKeyHandling(DuplicateKeyHandling.ThrowError)
            .RegisterSection<IReloadSettings>(new ReloadSettingsImpl());

        await Assert.ThrowsAnyAsync<Exception>(() => builder.BuildAsync());

        Assert.False(IniConfigRegistry.TryGet("broken-async.ini", out _));
    }

    [Fact]
    public void Register_SameFileAgain_DisposesThePreviousConfig()
    {
        if (!OperatingSystem.IsWindows())
            return; // file locks are advisory on Unix

        var path = WriteIni("locked.ini", "[ReloadSection]\nValue = x");
        IniConfigRegistry.ForFile("locked.ini")
            .AddSearchPath(_tempDir)
            .LockFile()
            .RegisterSection<IReloadSettings>(new ReloadSettingsImpl())
            .Build();
        Assert.ThrowsAny<IOException>(() => File.WriteAllText(path, "locked"));

        using var second = IniConfigRegistry.ForFile("locked.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<IReloadSettings>(new ReloadSettingsImpl())
            .Build();

        File.WriteAllText(path, "[ReloadSection]\nValue = unlocked"); // lock of the first config was released
    }

    // ── Section key resolution ────────────────────────────────────────────────

    [Fact]
    public void AddSection_WithConcreteType_RegistersUnderMostDerivedInterface()
    {
        var config = IniConfigRegistry.ForFile("resolve.ini")
            .AddSearchPath(_tempDir)
            .Create();

        var section = config.AddSection(new ResolveDerivedSettingsImpl());
        config.Load();

        Assert.Same(section, config.GetSection<IResolveDerivedSettings>());
    }

    // ── Transactions ──────────────────────────────────────────────────────────

    [Fact]
    public void Commit_MarksSectionDirty_AndUpdatesRawValue()
    {
        var section = new UserSettingsImpl();
        using var config = IniConfigRegistry.ForFile("tx.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<IUserSettings>(section)
            .Build();

        section.Begin();
        section.Username = "bob";
        section.Commit();

        Assert.True(section.HasChanges);
        Assert.Equal("bob", section.GetRawValue("Username"));
    }

    [Fact]
    public void Transaction_SetThenRevertToOriginal_CommitsTheOriginal()
    {
        var section = new TxNpcSettingsImpl();
        using var config = IniConfigRegistry.ForFile("tx-revert.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<ITxNpcSettings>(section)
            .Build();

        section.Begin();
        section.Name = "b";
        section.Name = "a";
        section.Commit();

        Assert.Equal("a", section.Name);
    }

    [Fact]
    public void Transaction_RaisesPropertyChangedOnCommit_NotBefore()
    {
        var section = new TxNpcSettingsImpl();
        using var config = IniConfigRegistry.ForFile("tx-npc.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<ITxNpcSettings>(section)
            .Build();
        var raised = new List<string?>();
        section.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        section.Begin();
        section.Name = "b";
        Assert.Empty(raised);

        section.Commit();
        Assert.Equal(new[] { nameof(ITxNpcSettings.Name) }, raised);
        Assert.Equal("b", section.Name);
    }

    // ── Review follow-ups ─────────────────────────────────────────────────────

    [Fact]
    public async Task WorkStartedDuringLoad_ThatOutlivesIt_UsesTheGateNormally()
    {
        WriteIni("leak.ini", "[ReloadSection]\nValue = x");
        var listener = new CallbackListener();
        var config = IniConfigRegistry.ForFile("leak.ini")
            .AddSearchPath(_tempDir)
            .AddListener(listener)
            .RegisterSection<IReloadSettings>(new ReloadSettingsImpl())
            .Create();
        Task? later = null;
        // The started task inherits the load's execution context; once the load is over it must not
        // behave as if it still held the gate.
        listener.FileLoaded = () => later = Task.Run(async () =>
        {
            await Task.Delay(100);
            config.Reload();
        });

        config.Load();
        await later!;
    }

    [Fact]
    public void PropertyChangedHandlerDuringReload_CanSave()
    {
        var path = WriteIni("npc-save.ini", "[General]\nAppName = before");
        var section = new GeneralSettingsImpl();
        using var config = IniConfigRegistry.ForFile("npc-save.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<IGeneralSettings>(section)
            .Build();
        section.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(IGeneralSettings.AppName) && section.AppName == "after")
            {
                section.MaxRetries = 7;
                config.Save();
            }
        };

        File.WriteAllText(path, "[General]\nAppName = after");
        config.Reload();

        Assert.Contains("MaxRetries = 7", File.ReadAllText(path));
        Assert.False(section.HasChanges);
    }

    [Fact]
    public async Task PostLoadSetupFailure_FaultsInitialLoadTask()
    {
        var config = IniConfigRegistry.ForFile("setup-fails.ini")
            .SetWritablePath(Path.Combine(_tempDir, "missing-folder", "setup-fails.ini"))
            .MonitorFile()
            .RegisterSection<IReloadSettings>(new ReloadSettingsImpl())
            .Create();

        await Assert.ThrowsAnyAsync<Exception>(() => config.LoadAsync());

        Assert.True(config.InitialLoadTask.IsFaulted);
        Assert.False(config.IsLoaded);
    }

    [Fact]
    public void Commit_AfterReload_KeepsReloadedValuesOfUntouchedProperties()
    {
        var path = WriteIni("tx-reload.ini", "[UserSettings]\nUsername = alice\nPassword = old");
        var section = new UserSettingsImpl();
        using var config = IniConfigRegistry.ForFile("tx-reload.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<IUserSettings>(section)
            .Build();

        section.Begin();
        section.Username = "bob";
        File.WriteAllText(path, "[UserSettings]\nUsername = alice\nPassword = new");
        config.Reload();
        section.Commit();

        Assert.Equal("bob", section.Username);
        Assert.Equal("new", section.Password);
    }

    private sealed class CallbackListener : IIniConfigListener
    {
        public Action? FileLoaded { get; set; }
        public void OnFileLoaded(string filePath) => FileLoaded?.Invoke();
        public void OnFileNotFound(string fileName) { }
        public void OnSaved(string filePath) { }
        public void OnReloaded(string filePath) { }
        public void OnError(string operation, Exception exception) { }
        public void OnUnknownKey(string sectionName, string key, string? rawValue) { }
        public void OnValueConversionFailed(string sectionName, string key, string? rawValue, Exception exception) { }
    }
}
