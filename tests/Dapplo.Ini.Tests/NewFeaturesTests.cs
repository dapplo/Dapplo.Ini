// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using Dapplo.Ini;

namespace Dapplo.Ini.Tests;

/// <summary>
/// Tests for the features added to close the gap with Dapplo.Config:
/// 1. Change tracking (HasChanges / HasPendingChanges)
/// 2. Configurable encoding (WithEncoding)
/// 3. SaveOnExit
/// 4. AutoSaveInterval
/// </summary>
[Collection("IniConfigRegistry")]
public sealed class NewFeaturesTests : IDisposable
{
    private readonly string _tempDir;

    public NewFeaturesTests()
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

    // ── Change tracking ────────────────────────────────────────────────────────

    [Fact]
    public void HasChanges_IsFalse_AfterBuild()
    {
        WriteIni("dirty.ini", "[General]\nAppName = Loaded");

        var section = new GeneralSettingsImpl();
        IniConfigRegistry.ForFile("dirty.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<IGeneralSettings>(section)
            .Build();

        // Initial load should not mark sections as dirty
        Assert.False(section.HasChanges);
    }

    [Fact]
    public void HasChanges_IsTrue_AfterSettingProperty()
    {
        WriteIni("dirty2.ini", "[General]\nAppName = Loaded");

        var section = new GeneralSettingsImpl();
        IniConfigRegistry.ForFile("dirty2.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<IGeneralSettings>(section)
            .Build();

        section.AppName = "Modified";

        Assert.True(section.HasChanges);
    }

    [Fact]
    public void HasPendingChanges_ReturnsFalse_WhenNoSectionIsDirty()
    {
        WriteIni("pending.ini", "[General]\nAppName = Loaded");

        var section = new GeneralSettingsImpl();
        var config = IniConfigRegistry.ForFile("pending.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<IGeneralSettings>(section)
            .Build();

        Assert.False(config.HasPendingChanges());
    }

    [Fact]
    public void HasPendingChanges_ReturnsTrue_AfterModification()
    {
        WriteIni("pending2.ini", "[General]\nAppName = Loaded");

        var section = new GeneralSettingsImpl();
        var config = IniConfigRegistry.ForFile("pending2.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<IGeneralSettings>(section)
            .Build();

        section.AppName = "Changed";

        Assert.True(config.HasPendingChanges());
    }

    [Fact]
    public void HasChanges_IsCleared_AfterSave()
    {
        WriteIni("save-dirty.ini", "[General]\nAppName = Initial");

        var section = new GeneralSettingsImpl();
        var config = IniConfigRegistry.ForFile("save-dirty.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<IGeneralSettings>(section)
            .Build();

        section.AppName = "Modified";
        Assert.True(section.HasChanges);

        config.Save();

        Assert.False(section.HasChanges);
        Assert.False(config.HasPendingChanges());
    }

    [Fact]
    public void HasChanges_IsCleared_AfterReload()
    {
        WriteIni("reload-dirty.ini", "[General]\nAppName = Initial");

        var section = new GeneralSettingsImpl();
        var config = IniConfigRegistry.ForFile("reload-dirty.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<IGeneralSettings>(section)
            .Build();

        section.AppName = "Modified";
        Assert.True(section.HasChanges);

        config.Reload();

        // After reload the section is back to the loaded state — no pending changes
        Assert.False(section.HasChanges);
        Assert.False(config.HasPendingChanges());
    }

    // ── Configurable encoding ──────────────────────────────────────────────────

    [Fact]
    public void WithEncoding_WritesFileWithSpecifiedEncoding()
    {
        var path = Path.Combine(_tempDir, "latin1.ini");

        var section = new GeneralSettingsImpl();
        var config = IniConfigRegistry.ForFile("latin1.ini")
            .AddSearchPath(_tempDir)
            .SetWritablePath(path)
            .WithEncoding(Encoding.Latin1)
            .RegisterSection<IGeneralSettings>(section)
            .Build();

        // Mark dirty so Save() actually writes
        section.AppName = "Caf\u00e9";
        config.Save();

        // Read the raw bytes and verify they are Latin-1 encoded
        var rawBytes = File.ReadAllBytes(path);
        var asLatin1 = Encoding.Latin1.GetString(rawBytes);
        Assert.Contains("Caf\u00e9", asLatin1);

        // The same bytes interpreted as UTF-8 would be different (é = 0xE9 in Latin-1, but
        // in UTF-8 that byte sequence is invalid / different).  Just ensure the Latin-1 round-trip works.
        var rawContent = Encoding.Latin1.GetString(rawBytes);
        Assert.Contains("Caf\u00e9", rawContent);
    }

    [Fact]
    public void WithEncoding_ReadsFileWithSpecifiedEncoding()
    {
        // Write a Latin-1 encoded file manually
        var path = Path.Combine(_tempDir, "latin1read.ini");
        var latin1Content = "[General]\nAppName = Caf\u00e9";
        File.WriteAllText(path, latin1Content, Encoding.Latin1);

        var section = new GeneralSettingsImpl();
        IniConfigRegistry.ForFile("latin1read.ini")
            .AddSearchPath(_tempDir)
            .WithEncoding(Encoding.Latin1)
            .RegisterSection<IGeneralSettings>(section)
            .Build();

        Assert.Equal("Caf\u00e9", section.AppName);
    }

    [Fact]
    public void WithEncoding_ThrowsWhenEncodingIsNull()
    {
        var builder = IniConfigRegistry.ForFile("enc.ini");
        Assert.Throws<ArgumentNullException>(() => builder.WithEncoding(null!));
    }

    // ── SaveOnExit ─────────────────────────────────────────────────────────────

    [Fact]
    public void SaveOnExit_RegistrationAndDispose_DoNotThrow()
    {
        WriteIni("exit.ini", "[General]\nAppName = Test");

        var section = new GeneralSettingsImpl();
        var config = IniConfigRegistry.ForFile("exit.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<IGeneralSettings>(section)
            .SaveOnExit()
            .Build();

        // Disposing should unregister the ProcessExit handler without throwing
        config.Dispose();
    }

    // ── AutoSaveInterval ───────────────────────────────────────────────────────

    [Fact]
    public async Task AutoSaveInterval_SavesWhenChangesArePending()
    {
        WriteIni("autosave.ini", "[General]\nAppName = Original");

        var section = new GeneralSettingsImpl();
        var config = IniConfigRegistry.ForFile("autosave.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<IGeneralSettings>(section)
            .AutoSaveInterval(TimeSpan.FromMilliseconds(100))
            .Build();

        // Modify a value to make the section dirty
        section.AppName = "AutoSaved";

        // Wait long enough for the timer to fire
        await Task.Delay(TimeSpan.FromMilliseconds(500));

        // The timer should have triggered Save() by now
        Assert.False(config.HasPendingChanges(), "Timer should have cleared pending changes.");

        // Verify the file was actually written
        var written = File.ReadAllText(Path.Combine(_tempDir, "autosave.ini"));
        Assert.Contains("AutoSaved", written);

        config.Dispose();
    }

    [Fact]
    public void AutoSaveInterval_DisposesTimerCleanly()
    {
        WriteIni("autosave2.ini", "[General]\nAppName = Test");

        var section = new GeneralSettingsImpl();
        var config = IniConfigRegistry.ForFile("autosave2.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<IGeneralSettings>(section)
            .AutoSaveInterval(TimeSpan.FromSeconds(60))
            .Build();

        // Dispose should stop the timer without throwing
        config.Dispose();
    }

    // ── PauseAutoSave / ResumeAutoSave ─────────────────────────────────────────

    [Fact]
    public async Task PauseAutoSave_PreventsSave_WhilePaused()
    {
        WriteIni("pause-autosave.ini", "[General]\nAppName = Original");

        var section = new GeneralSettingsImpl();
        using var config = IniConfigRegistry.ForFile("pause-autosave.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<IGeneralSettings>(section)
            .AutoSaveInterval(TimeSpan.FromMilliseconds(50))
            .Build();

        config.PauseAutoSave();
        section.AppName = "ShouldNotBeSaved";

        // Wait much longer than the timer interval — it should NOT fire while paused.
        await Task.Delay(300);

        Assert.True(config.HasPendingChanges(), "Changes should remain unsaved while auto-save is paused.");
        // Confirm the file on disk was not touched while paused.
        var diskContentWhilePaused = File.ReadAllText(Path.Combine(_tempDir, "pause-autosave.ini"));
        Assert.Contains("Original", diskContentWhilePaused);
        Assert.DoesNotContain("ShouldNotBeSaved", diskContentWhilePaused);

        config.ResumeAutoSave();

        // After resuming, auto-save should pick up the pending change.
        await Task.Delay(300);

        Assert.False(config.HasPendingChanges(), "Auto-save should have saved after resume.");
        // Confirm the file on disk was actually updated after resume.
        var diskContentAfterResume = File.ReadAllText(Path.Combine(_tempDir, "pause-autosave.ini"));
        Assert.Contains("ShouldNotBeSaved", diskContentAfterResume);
    }

    [Fact]
    public async Task PauseAutoSave_IsNestable()
    {
        WriteIni("pause-nested.ini", "[General]\nAppName = Original");

        var section = new GeneralSettingsImpl();
        using var config = IniConfigRegistry.ForFile("pause-nested.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<IGeneralSettings>(section)
            .AutoSaveInterval(TimeSpan.FromMilliseconds(50))
            .Build();

        // Nested pauses: must call Resume twice before auto-save fires again.
        config.PauseAutoSave();
        config.PauseAutoSave();
        section.AppName = "StillPaused";

        config.ResumeAutoSave(); // still paused (count = 1)
        await Task.Delay(300);
        Assert.True(config.HasPendingChanges(), "Still paused after one Resume.");
        var diskWhileStillPaused = File.ReadAllText(Path.Combine(_tempDir, "pause-nested.ini"));
        Assert.Contains("Original", diskWhileStillPaused);

        config.ResumeAutoSave(); // fully resumed (count = 0)
        await Task.Delay(300);
        Assert.False(config.HasPendingChanges(), "Auto-save should fire after both Resumes.");
        var diskAfterFullResume = File.ReadAllText(Path.Combine(_tempDir, "pause-nested.ini"));
        Assert.Contains("StillPaused", diskAfterFullResume);
    }

    [Fact]
    public void ResumeAutoSave_UnbalancedCall_DoesNotGoNegative()
    {
        WriteIni("resume-unbalanced.ini", "[General]\nAppName = Test");

        var section = new GeneralSettingsImpl();
        using var config = IniConfigRegistry.ForFile("resume-unbalanced.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<IGeneralSettings>(section)
            .Build();

        // Calling Resume without a prior Pause should be a safe no-op.
        config.ResumeAutoSave();
        config.ResumeAutoSave();
        // No exception thrown; auto-save is not broken.
    }

    // ── Save re-entrance ───────────────────────────────────────────────────────

    [Fact]
    public void Save_ConcurrentCalls_DoNotDeadlock()
    {
        WriteIni("concurrent-save.ini", "[General]\nAppName = Initial");

        var section = new GeneralSettingsImpl();
        using var config = IniConfigRegistry.ForFile("concurrent-save.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<IGeneralSettings>(section)
            .Build();

        section.AppName = "Changed";

        // Fire two concurrent saves — exactly one should run; neither should throw or deadlock.
        var t1 = Task.Run(() => config.Save());
        var t2 = Task.Run(() => config.Save());
        Task.WaitAll(t1, t2);

        // File must be well-formed (written by whichever call won the CAS).
        var content = File.ReadAllText(config.LoadedFromPath!);
        Assert.Contains("Changed", content);
    }
}
