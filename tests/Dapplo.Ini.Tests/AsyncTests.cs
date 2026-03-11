// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Dapplo.Ini;

namespace Dapplo.Ini.Tests;

/// <summary>
/// Tests for the async support added to IniConfig:
/// 1. BuildAsync — async build with DI-friendly InitialLoadTask
/// 2. SaveAsync  — async file writing with async lifecycle hooks
/// 3. ReloadAsync — async file reading with async lifecycle hooks
/// 4. IAfterLoadAsync / IBeforeSaveAsync / IAfterSaveAsync hooks
/// </summary>
[Collection("IniConfigRegistry")]
public sealed class AsyncTests : IDisposable
{
    private readonly string _tempDir;

    public AsyncTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        IniConfigRegistry.Clear();
    }

    public void Dispose()
    {
        IniConfigRegistry.Clear();
        try
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, recursive: true);
        }
        catch (IOException) { /* ignore transient file locks from async operations */ }
    }

    private string WriteIni(string fileName, string content)
    {
        var path = Path.Combine(_tempDir, fileName);
        File.WriteAllText(path, content);
        return path;
    }

    // ── BuildAsync ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task BuildAsync_WithExistingFile_LoadsValues()
    {
        const string content = """
            [General]
            AppName = AsyncLoaded
            MaxRetries = 5
            """;
        WriteIni("async.ini", content);

        var section = new GeneralSettingsImpl();
        var config = await IniConfigRegistry.ForFile("async.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<IGeneralSettings>(section)
            .BuildAsync();

        Assert.Equal("AsyncLoaded", section.AppName);
        Assert.Equal(5, section.MaxRetries);
    }

    [Fact]
    public async Task BuildAsync_WithNoFile_UsesDefaults()
    {
        var section = new GeneralSettingsImpl();
        await IniConfigRegistry.ForFile("async-missing.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<IGeneralSettings>(section)
            .BuildAsync();

        Assert.Equal("MyApp", section.AppName);
        Assert.Equal(42, section.MaxRetries);
    }

    [Fact]
    public async Task BuildAsync_InitialLoadTask_IsCompletedAfterBuild()
    {
        WriteIni("task.ini", "[General]\nAppName = TaskTest");

        var section = new GeneralSettingsImpl();
        var config = await IniConfigRegistry.ForFile("task.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<IGeneralSettings>(section)
            .BuildAsync();

        // After awaiting BuildAsync the task must be completed
        Assert.True(config.InitialLoadTask.IsCompleted);
    }

    [Fact]
    public async Task BuildAsync_InitialLoadTask_CanBeAwaitedByDiConsumer()
    {
        WriteIni("di.ini", "[General]\nAppName = DiLoaded");

        var section = new GeneralSettingsImpl();
        var builder = IniConfigRegistry.ForFile("di.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<IGeneralSettings>(section);

        // Simulate DI: start async build without awaiting, then use the config reference
        // that was registered in the registry before loading completed.
        var buildTask = builder.BuildAsync();

        // The config is already in the registry (registered before I/O starts)
        var config = IniConfigRegistry.Get("di.ini");
        Assert.NotNull(config);

        // Await the initial load task so we know values are ready
        await config.InitialLoadTask;

        Assert.Equal("DiLoaded", section.AppName);
        Assert.True(config.InitialLoadTask.IsCompleted);

        // Also ensure the build task itself completes
        await buildTask;
    }

    [Fact]
    public async Task BuildAsync_WithDefaultsFile_AppliesBeforeUserFile()
    {
        WriteIni("defaults.ini", "[General]\nAppName = DefaultApp\nMaxRetries = 1");
        WriteIni("async-layered.ini", "[General]\nMaxRetries = 99");

        var section = new GeneralSettingsImpl();
        await IniConfigRegistry.ForFile("async-layered.ini")
            .AddSearchPath(_tempDir)
            .AddDefaultsFile(Path.Combine(_tempDir, "defaults.ini"))
            .RegisterSection<IGeneralSettings>(section)
            .BuildAsync();

        Assert.Equal(99, section.MaxRetries);
        Assert.Equal("DefaultApp", section.AppName);
    }

    [Fact]
    public async Task BuildAsync_RegistersInRegistry()
    {
        WriteIni("async-reg.ini", "[General]\nAppName = Reg");

        var section = new GeneralSettingsImpl();
        await IniConfigRegistry.ForFile("async-reg.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<IGeneralSettings>(section)
            .BuildAsync();

        var config = IniConfigRegistry.Get("async-reg.ini");
        Assert.NotNull(config);
        Assert.Equal("async-reg.ini", config.FileName);
    }

    // ── SaveAsync ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task SaveAsync_WritesValuesToFile()
    {
        WriteIni("async-save.ini", "[General]\nAppName = Original");

        var section = new GeneralSettingsImpl();
        var config = await IniConfigRegistry.ForFile("async-save.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<IGeneralSettings>(section)
            .BuildAsync();

        section.AppName = "AsyncSaved";
        await config.SaveAsync();

        var written = File.ReadAllText(Path.Combine(_tempDir, "async-save.ini"));
        Assert.Contains("AsyncSaved", written);
    }

    [Fact]
    public async Task SaveAsync_ClearsDirtyFlag()
    {
        WriteIni("async-dirty.ini", "[General]\nAppName = Initial");

        var section = new GeneralSettingsImpl();
        var config = await IniConfigRegistry.ForFile("async-dirty.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<IGeneralSettings>(section)
            .BuildAsync();

        section.AppName = "Modified";
        Assert.True(section.HasChanges);

        await config.SaveAsync();

        Assert.False(section.HasChanges);
    }

    [Fact]
    public async Task SaveAsync_ThrowsWhenPathUnknown()
    {
        var section = new GeneralSettingsImpl();
        // No search path and no SetWritablePath, so LoadedFromPath will be null
        var iniConfig = await IniConfigRegistry.ForFile("no-path-async.ini")
            .RegisterSection<IGeneralSettings>(section)
            .BuildAsync();

        // LoadedFromPath is null because no search paths / writable path were configured
        Assert.Null(iniConfig.LoadedFromPath);

        await Assert.ThrowsAsync<InvalidOperationException>(() => iniConfig.SaveAsync());
    }

    // ── ReloadAsync ────────────────────────────────────────────────────────────

    [Fact]
    public async Task ReloadAsync_UpdatesValues()
    {
        WriteIni("async-reload.ini", "[General]\nAppName = First");

        var section = new GeneralSettingsImpl();
        var config = await IniConfigRegistry.ForFile("async-reload.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<IGeneralSettings>(section)
            .BuildAsync();

        Assert.Equal("First", section.AppName);

        // Update file on disk and reload
        File.WriteAllText(Path.Combine(_tempDir, "async-reload.ini"),
            "[General]\nAppName = Second");

        await config.ReloadAsync();

        Assert.Equal("Second", section.AppName);
    }

    [Fact]
    public async Task ReloadAsync_RaisesReloadedEvent()
    {
        WriteIni("async-event.ini", "[General]\nAppName = Initial");

        var section = new GeneralSettingsImpl();
        var config = await IniConfigRegistry.ForFile("async-event.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<IGeneralSettings>(section)
            .BuildAsync();

        var tcs = new TaskCompletionSource<bool>();
        config.Reloaded += (_, _) => tcs.TrySetResult(true);

        await config.ReloadAsync();

        Assert.True(tcs.Task.IsCompleted);
    }

    [Fact]
    public async Task ReloadAsync_ClearsDirtyFlag()
    {
        WriteIni("async-reload-dirty.ini", "[General]\nAppName = Initial");

        var section = new GeneralSettingsImpl();
        var config = await IniConfigRegistry.ForFile("async-reload-dirty.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<IGeneralSettings>(section)
            .BuildAsync();

        section.AppName = "Modified";
        Assert.True(section.HasChanges);

        await config.ReloadAsync();

        Assert.False(section.HasChanges);
    }

    // ── Async lifecycle hooks (non-generic partial-class pattern) ──────────────

    [Fact]
    public async Task AsyncAfterLoad_IsCalledAfterBuildAsync()
    {
        WriteIni("async-lifecycle.ini", "[AsyncLifecycle]\nValue = hello");

        var section = new AsyncLifecycleSettingsImpl();
        await IniConfigRegistry.ForFile("async-lifecycle.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<IAsyncLifecycleSettings>(section)
            .BuildAsync();

        Assert.True(section.AfterLoadAsyncCalled);
    }

    [Fact]
    public async Task AsyncAfterLoad_IsCalledAfterReloadAsync()
    {
        WriteIni("async-reload-hook.ini", "[AsyncLifecycle]\nValue = hello");

        var section = new AsyncLifecycleSettingsImpl();
        var config = await IniConfigRegistry.ForFile("async-reload-hook.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<IAsyncLifecycleSettings>(section)
            .BuildAsync();

        section.AfterLoadAsyncCalled = false; // reset after initial load

        await config.ReloadAsync();

        Assert.True(section.AfterLoadAsyncCalled);
    }

    [Fact]
    public async Task AsyncBeforeSave_And_AsyncAfterSave_AreCalledOnSaveAsync()
    {
        WriteIni("async-save-hooks.ini", "[AsyncLifecycle]\nValue = initial");

        var section = new AsyncLifecycleSettingsImpl();
        var config = await IniConfigRegistry.ForFile("async-save-hooks.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<IAsyncLifecycleSettings>(section)
            .BuildAsync();

        await config.SaveAsync();

        Assert.True(section.BeforeSaveAsyncCalled);
        Assert.True(section.AfterSaveAsyncCalled);
    }

    [Fact]
    public async Task AsyncBeforeSave_ReturningFalse_CancelsSaveAsync()
    {
        var iniFile = WriteIni("async-cancel-save.ini", "[AsyncCancelSave]\nValue = original");

        var section = new AsyncCancelSaveSettingsImpl { Value = "changed" };
        var config = await IniConfigRegistry.ForFile("async-cancel-save.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<IAsyncCancelSaveSettings>(section)
            .BuildAsync();

        await config.SaveAsync();

        // OnBeforeSaveAsync returns false, so the file must be unchanged
        var contents = File.ReadAllText(iniFile);
        Assert.Contains("original", contents);
    }

    // ── Sync hooks still called when async hook not implemented ────────────────

    [Fact]
    public async Task SyncAfterLoad_IsCalledByBuildAsync_WhenAsyncHookNotImplemented()
    {
        WriteIni("sync-in-async-build.ini", "[LegacyLifecycle]\nValue = hello");

        var section = new LegacyLifecycleSettingsImpl();
        await IniConfigRegistry.ForFile("sync-in-async-build.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<ILegacyLifecycleSettings>(section)
            .BuildAsync();

        // Sync IAfterLoad should be called when no async hook is present
        Assert.True(section.AfterLoadCalled);
    }

    [Fact]
    public async Task SyncBeforeSave_And_AfterSave_AreCalledBySaveAsync_WhenAsyncHooksNotImplemented()
    {
        WriteIni("sync-in-async-save.ini", "[LegacyLifecycle]\nValue = initial");

        var section = new LegacyLifecycleSettingsImpl();
        var config = await IniConfigRegistry.ForFile("sync-in-async-save.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<ILegacyLifecycleSettings>(section)
            .BuildAsync();

        await config.SaveAsync();

        Assert.True(section.BeforeSaveCalled);
        Assert.True(section.AfterSaveCalled);
    }

    [Fact]
    public async Task SyncAfterLoad_IsCalledByReloadAsync_WhenAsyncHookNotImplemented()
    {
        WriteIni("sync-reload-async.ini", "[LegacyLifecycle]\nValue = hello");

        var section = new LegacyLifecycleSettingsImpl();
        var config = await IniConfigRegistry.ForFile("sync-reload-async.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<ILegacyLifecycleSettings>(section)
            .BuildAsync();

        // The sync IAfterLoad hook should be called during the initial async build
        Assert.True(section.AfterLoadCalled);

        // Reload should also call it (still true)
        await config.ReloadAsync();
        Assert.True(section.AfterLoadCalled);
    }

    // ── IValueSourceAsync ──────────────────────────────────────────────────────

    [Fact]
    public async Task AsyncValueSource_OverridesFileValue_DuringBuildAsync()
    {
        WriteIni("async-source.ini", "[ReloadSection]\nValue = from-file");

        var source = new AsyncDictionaryValueSource();
        source.SetValue("ReloadSection", "Value", "from-async-source");

        var section = new ReloadSettingsImpl();
        await IniConfigRegistry.ForFile("async-source.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<IReloadSettings>(section)
            .AddValueSource(source)
            .BuildAsync();

        // Async external source wins over file value
        Assert.Equal("from-async-source", section.Value);
    }

    [Fact]
    public async Task AsyncValueSource_IsAppliedOnReloadAsync()
    {
        WriteIni("async-source2.ini", "[ReloadSection]\nValue = file-value");

        var source = new AsyncDictionaryValueSource();
        source.SetValue("ReloadSection", "Value", "async-v1");

        var section = new ReloadSettingsImpl();
        var config = await IniConfigRegistry.ForFile("async-source2.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<IReloadSettings>(section)
            .AddValueSource(source)
            .BuildAsync();

        Assert.Equal("async-v1", section.Value);

        source.SetValue("ReloadSection", "Value", "async-v2");
        await config.ReloadAsync();

        Assert.Equal("async-v2", section.Value);
    }

    [Fact]
    public async Task AsyncValueSource_WhenNoValueForKey_FileValueIsUsed()
    {
        WriteIni("async-source3.ini", "[ReloadSection]\nValue = from-file");

        // Source provides a value for a different section/key
        var source = new AsyncDictionaryValueSource();
        source.SetValue("OtherSection", "OtherKey", "something");

        var section = new ReloadSettingsImpl();
        await IniConfigRegistry.ForFile("async-source3.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<IReloadSettings>(section)
            .AddValueSource(source)
            .BuildAsync();

        // Source doesn't override this key → file value wins
        Assert.Equal("from-file", section.Value);
    }

    [Fact]
    public async Task AsyncValueSource_AppliedAfterSyncValueSource()
    {
        WriteIni("async-source-order.ini", "[ReloadSection]\nValue = from-file");

        var syncSource = new DictionaryValueSource();
        syncSource.SetValue("ReloadSection", "Value", "sync-value");

        var asyncSource = new AsyncDictionaryValueSource();
        asyncSource.SetValue("ReloadSection", "Value", "async-value");

        var section = new ReloadSettingsImpl();
        await IniConfigRegistry.ForFile("async-source-order.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<IReloadSettings>(section)
            .AddValueSource(syncSource)
            .AddValueSource(asyncSource)
            .BuildAsync();

        // Async source applied after sync source — async wins
        Assert.Equal("async-value", section.Value);
    }
}
