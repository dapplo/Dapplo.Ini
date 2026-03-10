// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using Dapplo.IniConfig.Configuration;

namespace Dapplo.IniConfig.Tests;

/// <summary>
/// Tests for the features added to close the gap with Dapplo.Config:
/// 1. Change tracking (HasChanges / HasPendingChanges)
/// 2. Configurable encoding (WithEncoding)
/// 3. SaveOnExit
/// 4. AutoSaveInterval
/// </summary>
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
}
