// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Dapplo.IniConfig.Configuration;

namespace Dapplo.IniConfig.Tests;

/// <summary>
/// Tests for getter-only interface properties.
/// A property declared with only <c>{ get; }</c> in the interface is read-only:
/// <list type="bullet">
///   <item>Its default value is applied on construction.</item>
///   <item>Its value is loaded from the INI file when present.</item>
///   <item>It is <em>not</em> written back to disk when the config is saved.</item>
///   <item>The generated implementation class still has a public setter so the
///         framework (and code holding a reference to the concrete class) can assign
///         values; the setter is simply absent from the interface.</item>
/// </list>
/// </summary>
[Collection("IniConfigRegistry")]
public sealed class ReadOnlyPropertyTests : IDisposable
{
    private readonly string _tempDir;

    public ReadOnlyPropertyTests()
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

    // ── Default value ──────────────────────────────────────────────────────────

    [Fact]
    public void GetterOnlyProperty_DefaultValue_IsApplied()
    {
        WriteIni("ro-default.ini", "[ReadOnly]");

        var section = new ReadOnlySettingsImpl();
        IniConfigRegistry.ForFile("ro-default.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<IReadOnlySettings>(section)
            .Build();

        Assert.Equal("1.0.0", section.Version);
    }

    // ── Load from INI ──────────────────────────────────────────────────────────

    [Fact]
    public void GetterOnlyProperty_LoadsValueFromIni()
    {
        WriteIni("ro-load.ini", "[ReadOnly]\nVersion = 2.5.0");

        var section = new ReadOnlySettingsImpl();
        IniConfigRegistry.ForFile("ro-load.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<IReadOnlySettings>(section)
            .Build();

        Assert.Equal("2.5.0", section.Version);
    }

    // ── Not saved to INI ──────────────────────────────────────────────────────

    [Fact]
    public void GetterOnlyProperty_IsNotWrittenOnSave()
    {
        var iniPath = WriteIni("ro-save.ini", "[ReadOnly]\nVersion = 9.9.9\nName = Existing");

        var section = new ReadOnlySettingsImpl();
        var config = IniConfigRegistry.ForFile("ro-save.ini")
            .AddSearchPath(_tempDir)
            .SetWritablePath(iniPath)
            .RegisterSection<IReadOnlySettings>(section)
            .Build();

        // Modify the writable property so the file is actually written.
        section.Name = "Modified";
        config.Save();

        var written = File.ReadAllText(iniPath);

        // The writable property should be saved.
        Assert.Contains("Modified", written);
        // The getter-only property should NOT appear in the saved output.
        Assert.DoesNotContain("Version", written);
    }

    // ── Implementation class setter ────────────────────────────────────────────

    [Fact]
    public void GetterOnlyProperty_ImplementationClass_HasPublicSetter()
    {
        // This test compiles only if ReadOnlySettingsImpl has a public setter for Version.
        // If the setter were absent the line below would not compile.
        var section = new ReadOnlySettingsImpl();
        section.Version = "3.0.0";
        Assert.Equal("3.0.0", section.Version);
    }

    [Fact]
    public void GetterOnlyProperty_InterfaceType_DoesNotExposeSet()
    {
        // Access through the interface should not allow setting — enforced at compile time.
        // This test documents the expected runtime behaviour (value visible via interface getter).
        var section = new ReadOnlySettingsImpl();
        section.Version = "4.0.0";

        IReadOnlySettings iface = section;
        Assert.Equal("4.0.0", iface.Version);
    }

    // ── Mixed section (getter-only + read-write) ───────────────────────────────

    [Fact]
    public void MixedSection_ReadWriteProperty_StillSaved()
    {
        WriteIni("ro-mixed.ini", "[ReadOnly]\nVersion = 5.0.0\nName = Original");

        var section = new ReadOnlySettingsImpl();
        var config = IniConfigRegistry.ForFile("ro-mixed.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<IReadOnlySettings>(section)
            .Build();

        Assert.Equal("5.0.0", section.Version);
        Assert.Equal("Original", section.Name);

        section.Name = "Updated";
        config.Save();

        var written = File.ReadAllText(config.LoadedFromPath!);
        Assert.Contains("Updated", written);
        Assert.DoesNotContain("Version", written);
    }

    // ── Reload ────────────────────────────────────────────────────────────────

    [Fact]
    public void GetterOnlyProperty_Reload_UpdatesValue()
    {
        var iniPath = WriteIni("ro-reload.ini", "[ReadOnly]\nVersion = 1.0.0");

        var section = new ReadOnlySettingsImpl();
        var config = IniConfigRegistry.ForFile("ro-reload.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<IReadOnlySettings>(section)
            .Build();

        Assert.Equal("1.0.0", section.Version);

        // Update the file on disk and reload.
        File.WriteAllText(iniPath, "[ReadOnly]\nVersion = 2.0.0");
        config.Reload();

        Assert.Equal("2.0.0", section.Version);
    }
}
