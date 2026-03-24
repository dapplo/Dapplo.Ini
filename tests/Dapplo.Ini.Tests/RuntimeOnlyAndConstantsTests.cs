// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace Dapplo.Ini.Tests;

/// <summary>
/// Tests for:
/// <list type="bullet">
///   <item><c>[IniValue(RuntimeOnly = true)]</c> — properties that are never loaded from or
///   saved to the INI file, but still participate in default-value initialisation.</item>
///   <item>Constants-file protection — keys loaded from a file registered via
///   <c>AddConstantsFile</c> are protected against change; an
///   <see cref="AccessViolationException"/> is thrown when the caller attempts to modify them.</item>
/// </list>
/// </summary>
[Collection("IniConfigRegistry")]
public sealed class RuntimeOnlyAndConstantsTests : IDisposable
{
    private readonly string _tempDir;

    public RuntimeOnlyAndConstantsTests()
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

    // ── RuntimeOnly: default value ─────────────────────────────────────────────

    [Fact]
    public void RuntimeOnly_DefaultValue_IsApplied()
    {
        WriteIni("ro.ini", "[RuntimeOnly]");

        var section = new RuntimeOnlySettingsImpl();
        IniConfigRegistry.ForFile("ro.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<IRuntimeOnlySettings>(section)
            .Build();

        Assert.Equal("runtime-default", section.Session);
        Assert.Equal(99, section.SessionCount);
    }

    // ── RuntimeOnly: not loaded from INI ──────────────────────────────────────

    [Fact]
    public void RuntimeOnly_ValueInIni_IsNotLoaded()
    {
        // Even though the INI file contains a value for Session, it should be ignored.
        WriteIni("ro-load.ini", "[RuntimeOnly]\nSession = from-file\nSessionCount = 42");

        var section = new RuntimeOnlySettingsImpl();
        IniConfigRegistry.ForFile("ro-load.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<IRuntimeOnlySettings>(section)
            .Build();

        // The file value must NOT be applied — default must remain.
        Assert.Equal("runtime-default", section.Session);
        Assert.Equal(99, section.SessionCount);
    }

    // ── RuntimeOnly: not saved to INI ─────────────────────────────────────────

    [Fact]
    public void RuntimeOnly_IsNotWrittenOnSave()
    {
        var iniPath = WriteIni("ro-save.ini", "[RuntimeOnly]\nPersisted = existing");

        var section = new RuntimeOnlySettingsImpl();
        var config = IniConfigRegistry.ForFile("ro-save.ini")
            .AddSearchPath(_tempDir)
            .SetWritablePath(iniPath)
            .RegisterSection<IRuntimeOnlySettings>(section)
            .Build();

        // Change both properties.
        section.Persisted = "modified";
        section.Session = "changed-at-runtime";
        config.Save();

        var written = File.ReadAllText(iniPath);

        // The regular property should be saved.
        Assert.Contains("modified", written);
        // The RuntimeOnly property must NOT appear in the file.
        Assert.DoesNotContain("Session", written);
        Assert.DoesNotContain("SessionCount", written);
    }

    // ── RuntimeOnly: reset to default on Reload ────────────────────────────────

    [Fact]
    public void RuntimeOnly_Reload_ResetsToDefault()
    {
        var iniPath = WriteIni("ro-reload.ini", "[RuntimeOnly]\nPersisted = initial");

        var section = new RuntimeOnlySettingsImpl();
        var config = IniConfigRegistry.ForFile("ro-reload.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<IRuntimeOnlySettings>(section)
            .Build();

        // Change the RuntimeOnly property at runtime.
        section.Session = "runtime-value";
        Assert.Equal("runtime-value", section.Session);

        // Reload should reset it back to the default (not keep the runtime value,
        // and not load any value from the file since it's RuntimeOnly).
        config.Reload();

        Assert.Equal("runtime-default", section.Session);
        Assert.Equal(99, section.SessionCount);
    }

    // ── RuntimeOnly: regular property still works normally ────────────────────

    [Fact]
    public void RuntimeOnly_RegularProperty_SavesAndLoads()
    {
        var iniPath = WriteIni("ro-regular.ini", "[RuntimeOnly]\nPersisted = hello");

        var section = new RuntimeOnlySettingsImpl();
        var config = IniConfigRegistry.ForFile("ro-regular.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<IRuntimeOnlySettings>(section)
            .Build();

        Assert.Equal("hello", section.Persisted);

        section.Persisted = "world";
        config.Save();

        var written = File.ReadAllText(iniPath);
        Assert.Contains("world", written);
    }

    // ── Constants: value is applied from constants file ────────────────────────

    [Fact]
    public void Constants_ValueFromConstantsFile_IsApplied()
    {
        WriteIni("ct.ini", "[ConstantsTest]\nUserValue = from-user");
        var constantsPath = WriteIni("ct-constants.ini", "[ConstantsTest]\nAdminValue = from-admin");

        var section = new ConstantsSettingsImpl();
        IniConfigRegistry.ForFile("ct.ini")
            .AddSearchPath(_tempDir)
            .AddConstantsFile(constantsPath)
            .RegisterSection<IConstantsSettings>(section)
            .Build();

        Assert.Equal("from-user", section.UserValue);
        Assert.Equal("from-admin", section.AdminValue);
    }

    // ── Constants: IsConstant returns true for locked keys ─────────────────────

    [Fact]
    public void Constants_IsConstant_ReturnsTrueForLockedKey()
    {
        WriteIni("ct2.ini", "[ConstantsTest]");
        var constantsPath = WriteIni("ct2-constants.ini", "[ConstantsTest]\nAdminValue = locked");

        var section = new ConstantsSettingsImpl();
        IniConfigRegistry.ForFile("ct2.ini")
            .AddSearchPath(_tempDir)
            .AddConstantsFile(constantsPath)
            .RegisterSection<IConstantsSettings>(section)
            .Build();

        Assert.True(section.IsConstant("AdminValue"));
        Assert.False(section.IsConstant("UserValue"));
    }

    // ── Constants: setting a constant throws AccessViolationException ──────────

    [Fact]
    public void Constants_SetConstantProperty_ThrowsAccessViolationException()
    {
        WriteIni("ct3.ini", "[ConstantsTest]");
        var constantsPath = WriteIni("ct3-constants.ini", "[ConstantsTest]\nAdminValue = protected");

        var section = new ConstantsSettingsImpl();
        IniConfigRegistry.ForFile("ct3.ini")
            .AddSearchPath(_tempDir)
            .AddConstantsFile(constantsPath)
            .RegisterSection<IConstantsSettings>(section)
            .Build();

        // Attempting to change the constant must throw.
        Assert.Throws<AccessViolationException>(() => section.AdminValue = "changed");
    }

    // ── Constants: non-constant property can still be changed ─────────────────

    [Fact]
    public void Constants_NonConstantProperty_CanBeChanged()
    {
        WriteIni("ct4.ini", "[ConstantsTest]\nUserValue = original");
        var constantsPath = WriteIni("ct4-constants.ini", "[ConstantsTest]\nAdminValue = admin");

        var section = new ConstantsSettingsImpl();
        IniConfigRegistry.ForFile("ct4.ini")
            .AddSearchPath(_tempDir)
            .AddConstantsFile(constantsPath)
            .RegisterSection<IConstantsSettings>(section)
            .Build();

        // The non-constant property can be freely changed.
        section.UserValue = "modified";
        Assert.Equal("modified", section.UserValue);
    }

    // ── Constants: constants file overrides user file ──────────────────────────

    [Fact]
    public void Constants_OverridesUserFile()
    {
        WriteIni("ct5.ini", "[ConstantsTest]\nUserValue = user\nAdminValue = user-override-attempt");
        var constantsPath = WriteIni("ct5-constants.ini", "[ConstantsTest]\nAdminValue = admin-wins");

        var section = new ConstantsSettingsImpl();
        IniConfigRegistry.ForFile("ct5.ini")
            .AddSearchPath(_tempDir)
            .AddConstantsFile(constantsPath)
            .RegisterSection<IConstantsSettings>(section)
            .Build();

        // The constants file wins over the user file.
        Assert.Equal("admin-wins", section.AdminValue);
        Assert.True(section.IsConstant("AdminValue"));
    }

    // ── Constants: protection is re-established after Reload ──────────────────

    [Fact]
    public void Constants_Reload_ReEstablishesProtection()
    {
        var iniPath = WriteIni("ct6.ini", "[ConstantsTest]");
        var constantsPath = WriteIni("ct6-constants.ini", "[ConstantsTest]\nAdminValue = locked");

        var section = new ConstantsSettingsImpl();
        var config = IniConfigRegistry.ForFile("ct6.ini")
            .AddSearchPath(_tempDir)
            .AddConstantsFile(constantsPath)
            .RegisterSection<IConstantsSettings>(section)
            .Build();

        // Verify protection before reload.
        Assert.True(section.IsConstant("AdminValue"));
        Assert.Throws<AccessViolationException>(() => section.AdminValue = "changed");

        // Reload should re-apply constants.
        config.Reload();

        Assert.True(section.IsConstant("AdminValue"));
        Assert.Equal("locked", section.AdminValue);
        Assert.Throws<AccessViolationException>(() => section.AdminValue = "changed-again");
    }

    // ── Constants: IsConstant via IIniSection interface ───────────────────────

    [Fact]
    public void Constants_IsConstant_ViaInterface()
    {
        WriteIni("ct7.ini", "[ConstantsTest]");
        var constantsPath = WriteIni("ct7-constants.ini", "[ConstantsTest]\nAdminValue = admin");

        var section = new ConstantsSettingsImpl();
        IniConfigRegistry.ForFile("ct7.ini")
            .AddSearchPath(_tempDir)
            .AddConstantsFile(constantsPath)
            .RegisterSection<IConstantsSettings>(section)
            .Build();

        // Access through the IIniSection interface.
        Interfaces.IIniSection iface = section;
        Assert.True(iface.IsConstant("AdminValue"));
        Assert.False(iface.IsConstant("UserValue"));
    }
}
