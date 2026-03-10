// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Dapplo.IniConfig.Configuration;

namespace Dapplo.IniConfig.Tests;

/// <summary>
/// Tests for <see cref="IniConfig.Reload"/> (in-place reload, singleton guarantee).
/// </summary>
[Collection("IniConfigRegistry")]
public sealed class ReloadTests : IDisposable
{
    private readonly string _tempDir;

    public ReloadTests()
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

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Reload_PicksUpFileChanges_WhileKeepingSameObjectReference()
    {
        WriteIni("reload.ini", "[ReloadSection]\nValue = first");

        var section = new ReloadSettingsImpl();
        var config = IniConfigRegistry.ForFile("reload.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<IReloadSettings>(section)
            .Build();

        Assert.Equal("first", section.Value);

        // Mutate the file on disk
        WriteIni("reload.ini", "[ReloadSection]\nValue = second");

        config.Reload();

        // Same section object — singleton guarantee
        var retrieved = config.GetSection<IReloadSettings>();
        Assert.Same(section, retrieved);
        Assert.Equal("second", section.Value);
    }

    [Fact]
    public void Reload_RestoresDefaults_WhenKeyRemovedFromFile()
    {
        WriteIni("reload2.ini", "[ReloadSection]\nValue = custom");

        var section = new ReloadSettingsImpl();
        var config = IniConfigRegistry.ForFile("reload2.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<IReloadSettings>(section)
            .Build();

        Assert.Equal("custom", section.Value);

        // Remove the key from the file
        WriteIni("reload2.ini", "[ReloadSection]");
        config.Reload();

        // Default value should be restored
        Assert.Equal("initial", section.Value);
    }

    [Fact]
    public void Reload_RaisesReloadedEvent()
    {
        WriteIni("reload3.ini", "[ReloadSection]\nValue = v1");

        var section = new ReloadSettingsImpl();
        var config = IniConfigRegistry.ForFile("reload3.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<IReloadSettings>(section)
            .Build();

        int eventCount = 0;
        config.Reloaded += (_, _) => eventCount++;

        WriteIni("reload3.ini", "[ReloadSection]\nValue = v2");
        config.Reload();

        Assert.Equal(1, eventCount);
        Assert.Equal("v2", section.Value);
    }

    [Fact]
    public void Reload_AppliesConstantFiles_AfterUserFile()
    {
        WriteIni("reload4.ini",     "[ReloadSection]\nValue = user");
        WriteIni("constants4.ini",  "[ReloadSection]\nValue = admin");

        var section = new ReloadSettingsImpl();
        var config = IniConfigRegistry.ForFile("reload4.ini")
            .AddSearchPath(_tempDir)
            .AddConstantsFile(Path.Combine(_tempDir, "constants4.ini"))
            .RegisterSection<IReloadSettings>(section)
            .Build();

        // Initially loaded: constant wins
        Assert.Equal("admin", section.Value);

        // Change user file; constant still wins after reload
        WriteIni("reload4.ini", "[ReloadSection]\nValue = user2");
        config.Reload();

        Assert.Equal("admin", section.Value);
    }
}
