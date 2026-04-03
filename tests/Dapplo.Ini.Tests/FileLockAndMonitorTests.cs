// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Dapplo.Ini;
using Dapplo.Ini.Interfaces;

namespace Dapplo.Ini.Tests;

/// <summary>
/// Tests for <see cref="IniConfigBuilder.LockFile"/> and file-change monitoring
/// (<see cref="IniConfigBuilder.MonitorFile"/>).
/// </summary>
[Collection("IniConfigRegistry")]
public sealed class FileLockAndMonitorTests : IDisposable
{
    private readonly string _tempDir;

    public FileLockAndMonitorTests()
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

    // ── File lock tests ───────────────────────────────────────────────────────

    [Fact]
    public void LockFile_PreventsOtherProcessesFromWriting()
    {
        WriteIni("locked.ini", "[ReloadSection]\nValue = v1");

        var section = new ReloadSettingsImpl();
        using var config = IniConfigRegistry.ForFile("locked.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<IReloadSettings>(section)
            .LockFile()
            .Build();

        // While lock is held, another writer should be blocked (FileShare.Read is exclusive for writes)
        var filePath = config.LoadedFromPath!;
        Assert.Throws<IOException>(() =>
        {
            using var writer = new FileStream(filePath, FileMode.Open, FileAccess.Write, FileShare.None);
        });
    }

    [Fact]
    public void LockFile_IsReleasedAfterDispose()
    {
        WriteIni("locked2.ini", "[ReloadSection]\nValue = v1");

        var section = new ReloadSettingsImpl();
        var config = IniConfigRegistry.ForFile("locked2.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<IReloadSettings>(section)
            .LockFile()
            .Build();

        config.Dispose();

        // After Dispose the lock must be released — another writer can open the file
        var filePath = config.LoadedFromPath!;
        var ex = Record.Exception(() =>
        {
            using var writer = new FileStream(filePath, FileMode.Open, FileAccess.Write, FileShare.None);
        });
        Assert.Null(ex);
    }

    // ── File monitoring tests ─────────────────────────────────────────────────

    [Fact]
    public async Task MonitorFile_WithNoCallback_ReloadsAutomatically()
    {
        WriteIni("monitor.ini", "[ReloadSection]\nValue = original");

        var section = new ReloadSettingsImpl();
        using var config = IniConfigRegistry.ForFile("monitor.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<IReloadSettings>(section)
            .MonitorFile() // no callback → always reload
            .Build();

        Assert.Equal("original", section.Value);

        var reloaded = new TaskCompletionSource<bool>();
        config.Reloaded += (_, _) => reloaded.TrySetResult(true);

        // Modify the file externally
        await Task.Delay(100); // let watcher start
        File.WriteAllText(config.LoadedFromPath!, "[ReloadSection]\nValue = updated");

        // Wait up to 3 seconds for the automatic reload
        var completed = await Task.WhenAny(reloaded.Task, Task.Delay(3000));
        Assert.True(completed == reloaded.Task, "Reload was not triggered within 3 seconds.");
        Assert.Equal("updated", section.Value);
    }

    [Fact]
    public async Task MonitorFile_WithIgnoreCallback_DoesNotReload()
    {
        WriteIni("monitor2.ini", "[ReloadSection]\nValue = original");

        var section = new ReloadSettingsImpl();
        using var config = IniConfigRegistry.ForFile("monitor2.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<IReloadSettings>(section)
            .MonitorFile(_ => ReloadDecision.Ignore) // always ignore
            .Build();

        Assert.Equal("original", section.Value);

        var reloaded = new TaskCompletionSource<bool>();
        config.Reloaded += (_, _) => reloaded.TrySetResult(true);

        await Task.Delay(100);
        File.WriteAllText(config.LoadedFromPath!, "[ReloadSection]\nValue = changed");

        // Wait a short time — no reload should happen
        var completed = await Task.WhenAny(reloaded.Task, Task.Delay(1500));
        Assert.True(completed != reloaded.Task, "Reload should not have been triggered.");
        Assert.Equal("original", section.Value);
    }

    [Fact]
    public async Task MonitorFile_WithPostponeCallback_ReloadsOnRequest()
    {
        WriteIni("monitor3.ini", "[ReloadSection]\nValue = original");

        var section = new ReloadSettingsImpl();
        using var config = IniConfigRegistry.ForFile("monitor3.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<IReloadSettings>(section)
            .MonitorFile(_ => ReloadDecision.Postpone)
            .Build();

        Assert.Equal("original", section.Value);

        await Task.Delay(100);
        File.WriteAllText(config.LoadedFromPath!, "[ReloadSection]\nValue = postponed");

        // Wait briefly — no automatic reload
        await Task.Delay(500);
        Assert.Equal("original", section.Value);

        // Consumer requests the postponed reload
        config.RequestPostponedReload();
        Assert.Equal("postponed", section.Value);
    }

    [Fact]
    public void LockFile_And_Save_WorkTogether()
    {
        WriteIni("locked-save.ini", "[ReloadSection]\nValue = original");

        var section = new ReloadSettingsImpl();
        using var config = IniConfigRegistry.ForFile("locked-save.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<IReloadSettings>(section)
            .LockFile()
            .Build();

        // Verify the file is locked (another process cannot write to it)
        var filePath = config.LoadedFromPath!;
        Assert.Throws<IOException>(() =>
        {
            using var writer = new FileStream(filePath, FileMode.Open, FileAccess.Write, FileShare.None);
        });

        // Save should succeed even while the lock is held
        section.Value = "saved";
        var ex = Record.Exception(() => config.Save());
        Assert.Null(ex);

        // Verify the value was actually written to disk
        var written = File.ReadAllText(filePath);
        Assert.Contains("saved", written);

        // Verify the lock is still held after save (another process still cannot write)
        Assert.Throws<IOException>(() =>
        {
            using var writer = new FileStream(filePath, FileMode.Open, FileAccess.Write, FileShare.None);
        });
    }

    [Fact]
    public async Task LockFile_And_AutoSave_WorkTogether()
    {
        WriteIni("locked-autosave.ini", "[ReloadSection]\nValue = original");

        var section = new ReloadSettingsImpl();
        using var config = IniConfigRegistry.ForFile("locked-autosave.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<IReloadSettings>(section)
            .LockFile()
            .AutoSaveInterval(TimeSpan.FromMilliseconds(200))
            .Build();

        // Modify the value — auto-save should pick it up shortly
        section.Value = "autosaved";

        // Wait for the auto-save timer to fire (up to 3 seconds)
        var filePath = config.LoadedFromPath!;
        string written = string.Empty;
        for (int i = 0; i < 30; i++)
        {
            await Task.Delay(100);
            written = File.ReadAllText(filePath);
            if (written.Contains("autosaved"))
                break;
        }

        Assert.Contains("autosaved", written);
    }

    // ── Mutual exclusivity tests ──────────────────────────────────────────────

    [Fact]
    public void LockFile_And_MonitorFile_ThrowsInvalidOperationException()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
        {
            IniConfigRegistry.ForFile("conflict.ini")
                .AddSearchPath(_tempDir)
                .LockFile()
                .MonitorFile()
                .Create();
        });
        Assert.Contains("mutually exclusive", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MonitorFile_And_LockFile_ThrowsInvalidOperationException()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
        {
            IniConfigRegistry.ForFile("conflict2.ini")
                .AddSearchPath(_tempDir)
                .MonitorFile()
                .LockFile()
                .Create();
        });
        Assert.Contains("mutually exclusive", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Save_DoesNotTriggerMonitorReload()
    {
        WriteIni("monitor4.ini", "[ReloadSection]\nValue = original");

        var section = new ReloadSettingsImpl();
        using var config = IniConfigRegistry.ForFile("monitor4.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<IReloadSettings>(section)
            .MonitorFile() // automatic reload on external change
            .Build();

        int reloadCount = 0;
        config.Reloaded += (_, _) => reloadCount++;

        // Our own Save() should not trigger a reload
        section.Value = "saved";
        config.Save();

        // Give watcher a moment to fire (it shouldn't)
        Thread.Sleep(500);

        Assert.Equal(0, reloadCount);
        Assert.Equal("saved", section.Value);
    }
}
