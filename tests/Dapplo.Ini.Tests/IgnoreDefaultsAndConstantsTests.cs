// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace Dapplo.Ini.Tests;

/// <summary>
/// Tests for:
/// <list type="bullet">
///   <item><c>[IniSection(IgnoreDefaults = true)]</c> — the entire section is skipped when
///   applying defaults files.</item>
///   <item><c>[IniSection(IgnoreConstants = true)]</c> — the entire section is skipped when
///   applying constants files (no lock-out either).</item>
///   <item><c>[IniValue(IgnoreDefaults = true)]</c> — individual property is skipped when
///   applying defaults files.</item>
///   <item><c>[IniValue(IgnoreConstants = true)]</c> — individual property is skipped when
///   applying constants files.</item>
///   <item>Metadata is only read from the main user file, never from defaults or constants.</item>
/// </list>
/// </summary>
[Collection("IniConfigRegistry")]
public sealed class IgnoreDefaultsAndConstantsTests : IDisposable
{
    private readonly string _tempDir;

    public IgnoreDefaultsAndConstantsTests()
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

    // ── Section-level IgnoreDefaults ──────────────────────────────────────────

    [Fact]
    public void SectionIgnoreDefaults_ValueInDefaultsFile_IsNotApplied()
    {
        // The defaults file sets a value for the section, but [IgnoreDefaults=true] must prevent it.
        var defaultsPath = WriteIni("sid-defaults.ini", "[IgnoreDefaultsSection]\nValue = from-defaults");
        WriteIni("sid.ini", "[IgnoreDefaultsSection]");

        var section = new IgnoreDefaultsSectionSettingsImpl();
        IniConfigRegistry.ForFile("sid.ini")
            .AddSearchPath(_tempDir)
            .AddDefaultsFile(defaultsPath)
            .RegisterSection<IIgnoreDefaultsSectionSettings>(section)
            .Build();

        // The defaults file value must NOT be applied; compiled default must remain.
        Assert.Equal("compiled-default", section.Value);
    }

    [Fact]
    public void SectionIgnoreDefaults_ValueInUserFile_IsStillApplied()
    {
        // [IgnoreDefaults] only blocks defaults files; the main user file must still work.
        var defaultsPath = WriteIni("sid2-defaults.ini", "[IgnoreDefaultsSection]\nValue = from-defaults");
        WriteIni("sid2.ini", "[IgnoreDefaultsSection]\nValue = from-user");

        var section = new IgnoreDefaultsSectionSettingsImpl();
        IniConfigRegistry.ForFile("sid2.ini")
            .AddSearchPath(_tempDir)
            .AddDefaultsFile(defaultsPath)
            .RegisterSection<IIgnoreDefaultsSectionSettings>(section)
            .Build();

        Assert.Equal("from-user", section.Value);
    }

    [Fact]
    public void SectionIgnoreDefaults_ValueInConstantsFile_IsStillApplied()
    {
        // [IgnoreDefaults] does NOT block constants files.
        var defaultsPath = WriteIni("sid3-defaults.ini", "[IgnoreDefaultsSection]\nValue = from-defaults");
        var constantsPath = WriteIni("sid3-constants.ini", "[IgnoreDefaultsSection]\nValue = from-constants");
        WriteIni("sid3.ini", "[IgnoreDefaultsSection]");

        var section = new IgnoreDefaultsSectionSettingsImpl();
        IniConfigRegistry.ForFile("sid3.ini")
            .AddSearchPath(_tempDir)
            .AddDefaultsFile(defaultsPath)
            .AddConstantsFile(constantsPath)
            .RegisterSection<IIgnoreDefaultsSectionSettings>(section)
            .Build();

        // Defaults are skipped, but constants still win.
        Assert.Equal("from-constants", section.Value);
        Assert.True(section.IsConstant("Value"));
    }

    // ── Section-level IgnoreConstants ─────────────────────────────────────────

    [Fact]
    public void SectionIgnoreConstants_ValueInConstantsFile_IsNotApplied()
    {
        var constantsPath = WriteIni("sic-constants.ini", "[IgnoreConstantsSection]\nValue = from-constants");
        WriteIni("sic.ini", "[IgnoreConstantsSection]\nValue = from-user");

        var section = new IgnoreConstantsSectionSettingsImpl();
        IniConfigRegistry.ForFile("sic.ini")
            .AddSearchPath(_tempDir)
            .AddConstantsFile(constantsPath)
            .RegisterSection<IIgnoreConstantsSectionSettings>(section)
            .Build();

        // Constants file is skipped for this section; user file value remains.
        Assert.Equal("from-user", section.Value);
        Assert.False(section.IsConstant("Value"));
    }

    [Fact]
    public void SectionIgnoreConstants_CanStillModifyValue()
    {
        // Because [IgnoreConstants=true] prevents the key being locked, modification is allowed.
        var constantsPath = WriteIni("sic2-constants.ini", "[IgnoreConstantsSection]\nValue = from-constants");
        WriteIni("sic2.ini", "[IgnoreConstantsSection]");

        var section = new IgnoreConstantsSectionSettingsImpl();
        IniConfigRegistry.ForFile("sic2.ini")
            .AddSearchPath(_tempDir)
            .AddConstantsFile(constantsPath)
            .RegisterSection<IIgnoreConstantsSectionSettings>(section)
            .Build();

        // Must not throw — the value was never locked.
        section.Value = "runtime-change";
        Assert.Equal("runtime-change", section.Value);
    }

    [Fact]
    public void SectionIgnoreConstants_ValueInDefaultsFile_IsStillApplied()
    {
        // [IgnoreConstants] does NOT block defaults files.
        var defaultsPath = WriteIni("sic3-defaults.ini", "[IgnoreConstantsSection]\nValue = from-defaults");
        WriteIni("sic3.ini", "[IgnoreConstantsSection]");

        var section = new IgnoreConstantsSectionSettingsImpl();
        IniConfigRegistry.ForFile("sic3.ini")
            .AddSearchPath(_tempDir)
            .AddDefaultsFile(defaultsPath)
            .RegisterSection<IIgnoreConstantsSectionSettings>(section)
            .Build();

        Assert.Equal("from-defaults", section.Value);
    }

    // ── Property-level IgnoreDefaults ─────────────────────────────────────────

    [Fact]
    public void PropertyIgnoreDefaults_MarkedProperty_IsNotLoadedFromDefaultsFile()
    {
        var defaultsPath = WriteIni("pid-defaults.ini",
            "[MixedIgnoreSection]\nValueA = default-a\nValueB = default-b");
        WriteIni("pid.ini", "[MixedIgnoreSection]");

        var section = new MixedIgnoreSettingsImpl();
        IniConfigRegistry.ForFile("pid.ini")
            .AddSearchPath(_tempDir)
            .AddDefaultsFile(defaultsPath)
            .RegisterSection<IMixedIgnoreSettings>(section)
            .Build();

        // ValueA has no IgnoreDefaults — it should be loaded from the defaults file.
        Assert.Equal("default-a", section.ValueA);
        // ValueB has IgnoreDefaults=true — defaults file must be skipped; compiled default stays.
        Assert.Equal("compiled-b", section.ValueB);
    }

    [Fact]
    public void PropertyIgnoreDefaults_MarkedProperty_IsStillLoadedFromUserFile()
    {
        var defaultsPath = WriteIni("pid2-defaults.ini",
            "[MixedIgnoreSection]\nValueB = default-b");
        WriteIni("pid2.ini", "[MixedIgnoreSection]\nValueB = user-b");

        var section = new MixedIgnoreSettingsImpl();
        IniConfigRegistry.ForFile("pid2.ini")
            .AddSearchPath(_tempDir)
            .AddDefaultsFile(defaultsPath)
            .RegisterSection<IMixedIgnoreSettings>(section)
            .Build();

        // User file always applies regardless of IgnoreDefaults.
        Assert.Equal("user-b", section.ValueB);
    }

    // ── Property-level IgnoreConstants ───────────────────────────────────────

    [Fact]
    public void PropertyIgnoreConstants_MarkedProperty_IsNotLoadedFromConstantsFile()
    {
        var constantsPath = WriteIni("pic-constants.ini",
            "[MixedIgnoreSection]\nValueA = const-a\nValueC = const-c");
        WriteIni("pic.ini", "[MixedIgnoreSection]");

        var section = new MixedIgnoreSettingsImpl();
        IniConfigRegistry.ForFile("pic.ini")
            .AddSearchPath(_tempDir)
            .AddConstantsFile(constantsPath)
            .RegisterSection<IMixedIgnoreSettings>(section)
            .Build();

        // ValueA has no IgnoreConstants — constants file applied normally.
        Assert.Equal("const-a", section.ValueA);
        Assert.True(section.IsConstant("ValueA"));

        // ValueC has IgnoreConstants=true — constants file skipped for this property.
        Assert.Equal("compiled-c", section.ValueC);
        Assert.False(section.IsConstant("ValueC"));
    }

    [Fact]
    public void PropertyIgnoreConstants_MarkedProperty_CanBeModified()
    {
        var constantsPath = WriteIni("pic2-constants.ini",
            "[MixedIgnoreSection]\nValueC = const-c");
        WriteIni("pic2.ini", "[MixedIgnoreSection]");

        var section = new MixedIgnoreSettingsImpl();
        IniConfigRegistry.ForFile("pic2.ini")
            .AddSearchPath(_tempDir)
            .AddConstantsFile(constantsPath)
            .RegisterSection<IMixedIgnoreSettings>(section)
            .Build();

        // ValueC was not locked, so modification must succeed without throwing.
        section.ValueC = "runtime-change";
        Assert.Equal("runtime-change", section.ValueC);
    }

    // ── Metadata: only from main user file ────────────────────────────────────

    [Fact]
    public void Metadata_NotReadFromDefaultsFile()
    {
        // The defaults file has a [__metadata__] section; the user file does not.
        var defaultsPath = WriteIni("meta-defaults.ini",
            "[__metadata__]\nVersion = defaults-version\n\n[ConstantsTest]\nUserValue = dv");
        WriteIni("meta.ini", "[ConstantsTest]\nUserValue = uv");

        var section = new ConstantsSettingsImpl();
        var config = IniConfigRegistry.ForFile("meta.ini")
            .AddSearchPath(_tempDir)
            .AddDefaultsFile(defaultsPath)
            .RegisterSection<IConstantsSettings>(section)
            .Build();

        // Metadata must be null because the main user file has no [__metadata__] section.
        Assert.Null(config.Metadata);
    }

    [Fact]
    public void Metadata_NotReadFromConstantsFile()
    {
        // The constants file has a [__metadata__] section; the user file does not.
        var constantsPath = WriteIni("meta-constants.ini",
            "[__metadata__]\nVersion = constants-version\n\n[ConstantsTest]\nAdminValue = cv");
        WriteIni("meta2.ini", "[ConstantsTest]");

        var section = new ConstantsSettingsImpl();
        var config = IniConfigRegistry.ForFile("meta2.ini")
            .AddSearchPath(_tempDir)
            .AddConstantsFile(constantsPath)
            .RegisterSection<IConstantsSettings>(section)
            .Build();

        // Metadata must be null because the main user file has no [__metadata__] section.
        Assert.Null(config.Metadata);
    }

    [Fact]
    public void Metadata_IsReadFromUserFile()
    {
        // Ensure that metadata IS still read when the user file contains it.
        WriteIni("meta3.ini",
            "[__metadata__]\nVersion = 1.2.3\nCreatedBy = TestApp\n\n[ConstantsTest]");

        var section = new ConstantsSettingsImpl();
        var config = IniConfigRegistry.ForFile("meta3.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<IConstantsSettings>(section)
            .Build();

        Assert.NotNull(config.Metadata);
        Assert.Equal("1.2.3", config.Metadata!.Version);
        Assert.Equal("TestApp", config.Metadata.ApplicationName);
    }

    [Fact]
    public void Metadata_UserFileWins_WhenDefaultsFileAlsoHasMetadata()
    {
        // Both files have [__metadata__]; the user file's metadata must win.
        var defaultsPath = WriteIni("meta4-defaults.ini",
            "[__metadata__]\nVersion = defaults-v\n\n[ConstantsTest]");
        WriteIni("meta4.ini",
            "[__metadata__]\nVersion = user-v\n\n[ConstantsTest]");

        var section = new ConstantsSettingsImpl();
        var config = IniConfigRegistry.ForFile("meta4.ini")
            .AddSearchPath(_tempDir)
            .AddDefaultsFile(defaultsPath)
            .RegisterSection<IConstantsSettings>(section)
            .Build();

        Assert.NotNull(config.Metadata);
        Assert.Equal("user-v", config.Metadata!.Version);
    }
}
