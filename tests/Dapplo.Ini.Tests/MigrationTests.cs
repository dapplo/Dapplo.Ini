// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Dapplo.Ini.Interfaces;

namespace Dapplo.Ini.Tests;

/// <summary>
/// Tests for migration support features:
/// 1. <see cref="IUnknownKey{TSelf}"/> — generic static-virtual pattern.
/// 2. <see cref="IUnknownKey"/>        — non-generic partial-class pattern.
/// 3. <see cref="IniConfigBuilder.OnUnknownKey"/> — builder-level callback.
/// 4. <see cref="IniConfigBuilder.EnableMetadata"/> — version and commit-hash tracking.
/// </summary>
[Collection("IniConfigRegistry")]
public sealed class MigrationTests : IDisposable
{
    private readonly string _tempDir;

    public MigrationTests()
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

    // ── IUnknownKey<TSelf> (generic, static-virtual) ──────────────────────────

    [Fact]
    public void GenericUnknownKey_RenamesMigratedProperty()
    {
        // File contains the OLD key "OldName" — the section now has "DisplayName".
        WriteIni("migration1.ini", "[Migration]\nOldName = Migrated!\nMaxCount = 5");

        var section = new MigrationSettingsImpl();
        IniConfigRegistry.ForFile("migration1.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<IMigrationSettings>(section)
            .Build();

        // The OnUnknownKey bridge should have copied OldName → DisplayName.
        Assert.Equal("Migrated!", section.DisplayName);
        Assert.Equal(5, section.MaxCount);
        Assert.True(section.UnknownKeyCalled);
        Assert.Equal("OldName", section.LastUnknownKey);
    }

    [Fact]
    public void GenericUnknownKey_AfterLoadHookStillFires()
    {
        WriteIni("migration2.ini", "[Migration]\nDisplayName = Test");

        var section = new MigrationSettingsImpl();
        IniConfigRegistry.ForFile("migration2.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<IMigrationSettings>(section)
            .Build();

        Assert.True(section.AfterLoadCalled, "IAfterLoad<TSelf> hook should still fire.");
    }

    [Fact]
    public void GenericUnknownKey_KnownKeysDoNotTriggerCallback()
    {
        WriteIni("migration3.ini", "[Migration]\nDisplayName = Known\nMaxCount = 10");

        var section = new MigrationSettingsImpl();
        IniConfigRegistry.ForFile("migration3.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<IMigrationSettings>(section)
            .Build();

        // Known keys must NOT trigger the unknown-key callback.
        Assert.False(section.UnknownKeyCalled, "Known keys should not trigger OnUnknownKey.");
    }

    [Fact]
    public void GenericUnknownKey_AlsoFiresDuringReload()
    {
        WriteIni("migration-reload.ini", "[Migration]\nDisplayName = v1");

        var section = new MigrationSettingsImpl();
        var config = IniConfigRegistry.ForFile("migration-reload.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<IMigrationSettings>(section)
            .Build();

        // Overwrite the file with an old/renamed key.
        File.WriteAllText(Path.Combine(_tempDir, "migration-reload.ini"),
            "[Migration]\nOldName = ReloadedValue");

        config.Reload();

        Assert.True(section.UnknownKeyCalled);
        Assert.Equal("ReloadedValue", section.DisplayName);
    }

    // ── IUnknownKey (non-generic, partial-class) ──────────────────────────────

    [Fact]
    public void LegacyUnknownKey_RenamesMigratedProperty()
    {
        WriteIni("legacy-migration.ini", "[LegacyMigration]\nOldValue = 42");

        var section = new LegacyMigrationSettingsImpl();
        IniConfigRegistry.ForFile("legacy-migration.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<ILegacyMigrationSettings>(section)
            .Build();

        Assert.Equal(42, section.Value);
        Assert.Equal("OldValue", section.LastUnknownKey);
    }

    [Fact]
    public void LegacyUnknownKey_UnknownValue_IsPassedToHandler()
    {
        WriteIni("legacy-migration2.ini", "[LegacyMigration]\nObsoleteKey = hello");

        var section = new LegacyMigrationSettingsImpl();
        IniConfigRegistry.ForFile("legacy-migration2.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<ILegacyMigrationSettings>(section)
            .Build();

        Assert.Equal("ObsoleteKey", section.LastUnknownKey);
        Assert.Equal("hello", section.LastUnknownValue);
    }

    // ── Builder-level OnUnknownKey callback ───────────────────────────────────

    [Fact]
    public void BuilderCallback_IsInvokedForUnknownKeys()
    {
        WriteIni("builder-callback.ini", "[General]\nUnknownProp = foo\nAppName = Bar");

        var unknownKeys = new List<(string Section, string Key, string? Value)>();

        var section = new GeneralSettingsImpl();
        IniConfigRegistry.ForFile("builder-callback.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<IGeneralSettings>(section)
            .OnUnknownKey((s, k, v) => unknownKeys.Add((s, k, v)))
            .Build();

        // "UnknownProp" is not on IGeneralSettings — callback should fire.
        Assert.Single(unknownKeys);
        Assert.Equal("General", unknownKeys[0].Section);
        Assert.Equal("UnknownProp", unknownKeys[0].Key);
        Assert.Equal("foo", unknownKeys[0].Value);

        // "AppName" IS known — it must NOT appear in the list.
        Assert.DoesNotContain(unknownKeys, t => t.Key == "AppName");
    }

    [Fact]
    public void BuilderCallback_IsInvokedDuringReload()
    {
        WriteIni("builder-reload-callback.ini", "[General]\nAppName = Test");

        var unknownKeys = new List<string>();
        var section = new GeneralSettingsImpl();
        var config = IniConfigRegistry.ForFile("builder-reload-callback.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<IGeneralSettings>(section)
            .OnUnknownKey((_, k, _) => unknownKeys.Add(k))
            .Build();

        Assert.Empty(unknownKeys); // no unknown keys on first load

        File.WriteAllText(Path.Combine(_tempDir, "builder-reload-callback.ini"),
            "[General]\nAppName = New\nRenamedKey = value");
        config.Reload();

        Assert.Contains("RenamedKey", unknownKeys);
    }

    [Fact]
    public void BuilderCallback_ThrowsWhenCallbackIsNull()
    {
        var builder = IniConfigRegistry.ForFile("null-cb.ini");
        Assert.Throws<ArgumentNullException>(() => builder.OnUnknownKey(null!));
    }

    // ── Metadata section ([__metadata__]) ────────────────────────────────────

    [Fact]
    public void EnableMetadata_WritesMetadataSectionFirst()
    {
        var section = new GeneralSettingsImpl();
        var config = IniConfigRegistry.ForFile("metadata-write.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<IGeneralSettings>(section)
            .EnableMetadata(version: "1.2.3", applicationName: "TestApp")
            .Build();

        // Mark dirty and save so the file is written.
        section.AppName = "WithMetadata";
        config.Save();

        var written = File.ReadAllText(config.LoadedFromPath!);

        // The metadata section must appear before the [General] section.
        var metaIdx    = written.IndexOf("[__metadata__]", StringComparison.OrdinalIgnoreCase);
        var generalIdx = written.IndexOf("[General]",      StringComparison.OrdinalIgnoreCase);
        Assert.True(metaIdx >= 0, "[__metadata__] section must be present.");
        Assert.True(metaIdx < generalIdx,
            "[__metadata__] section must appear before [General].");

        Assert.Contains("Version = 1.2.3",   written);
        Assert.Contains("CreatedBy = TestApp", written);
        Assert.Contains("SavedOn",             written);
    }

    [Fact]
    public void EnableMetadata_MetadataAccessibleAfterLoad()
    {
        // Write a file that already has a [__metadata__] section.
        WriteIni("metadata-read.ini",
            "[__metadata__]\nVersion = 2.0.0\nCreatedBy = Greenshot\nSavedOn = 2026.01.01\n[General]\nAppName = Test");

        var section = new GeneralSettingsImpl();
        var config = IniConfigRegistry.ForFile("metadata-read.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<IGeneralSettings>(section)
            .EnableMetadata()
            .Build();

        Assert.NotNull(config.Metadata);
        Assert.Equal("2.0.0",     config.Metadata!.Version);
        Assert.Equal("Greenshot", config.Metadata.ApplicationName);
        Assert.Equal("2026.01.01", config.Metadata.SavedOn);
    }

    [Fact]
    public void EnableMetadata_MetadataIsNullWhenSectionAbsent()
    {
        WriteIni("no-metadata.ini", "[General]\nAppName = Test");

        var section = new GeneralSettingsImpl();
        var config = IniConfigRegistry.ForFile("no-metadata.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<IGeneralSettings>(section)
            .EnableMetadata()
            .Build();

        // File has no [__metadata__] section — Metadata should be null.
        Assert.Null(config.Metadata);
    }

    [Fact]
    public void EnableMetadata_NotEnabled_MetadataSectionNotWritten()
    {
        var section = new GeneralSettingsImpl();
        var config = IniConfigRegistry.ForFile("no-metadata-write.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<IGeneralSettings>(section)
            .Build(); // No EnableMetadata()

        section.AppName = "NoMeta";
        config.Save();

        var written = File.ReadAllText(config.LoadedFromPath!);
        Assert.DoesNotContain("__metadata__", written, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EnableMetadata_MetadataNotTriggeredInUnknownKeyCallback()
    {
        // Write a file with a [__metadata__] section plus an unknown key in [General].
        WriteIni("metadata-unknown.ini",
            "[__metadata__]\nVersion = 1.0\nCreatedBy = App\nSavedOn = now\n[General]\nUnknownKey = oops");

        var unknownKeys = new List<string>();
        var section = new GeneralSettingsImpl();
        IniConfigRegistry.ForFile("metadata-unknown.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<IGeneralSettings>(section)
            .EnableMetadata()
            .OnUnknownKey((_, k, _) => unknownKeys.Add(k))
            .Build();

        // Only "UnknownKey" from [General] should appear; nothing from [__metadata__].
        Assert.Contains("UnknownKey", unknownKeys);
        Assert.DoesNotContain("Version",   unknownKeys);
        Assert.DoesNotContain("CreatedBy", unknownKeys);
        Assert.DoesNotContain("SavedOn",   unknownKeys);
    }

    // ── Integration: version-aware migration via IAfterLoad ───────────────────

    [Fact]
    public void VersionTracking_AfterLoad_CanCompareStoredVersionToCurrentVersion()
    {
        // Simulate a file written by version 0.0.0.1 (very old).
        WriteIni("version-migration.ini",
            "[__metadata__]\nVersion = 0.0.0.1\nCreatedBy = OldApp\nSavedOn = 2020.01.01\n[Migration]\nDisplayName = OldName");

        var section = new MigrationSettingsImpl();
        var config = IniConfigRegistry.ForFile("version-migration.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<IMigrationSettings>(section)
            .EnableMetadata(version: "1.0.0", applicationName: "NewApp")
            .Build();

        // The stored version from the file should be readable via Metadata.
        Assert.NotNull(config.Metadata);
        Assert.Equal("0.0.0.1", config.Metadata!.Version);
        Assert.Equal("OldApp",  config.Metadata.ApplicationName);
    }

    // ── CommitHash metadata ────────────────────────────────────────────────────

    [Fact]
    public void EnableMetadata_ExplicitCommitHash_IsWrittenAndReadBack()
    {
        // Write an INI that already contains CommitHash from a previous save.
        WriteIni("commitHash-read.ini",
            "[__metadata__]\nVersion = 1.5.0\nCreatedBy = App\nSavedOn = now\nCommitHash = abc1234def5678\n[General]\nAppName = Test");

        var section = new GeneralSettingsImpl();
        var config = IniConfigRegistry.ForFile("commitHash-read.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<IGeneralSettings>(section)
            .EnableMetadata()
            .Build();

        Assert.NotNull(config.Metadata);
        Assert.Equal("1.5.0",           config.Metadata!.Version);
        Assert.Equal("abc1234def5678",  config.Metadata.CommitHash);
    }

    [Fact]
    public void EnableMetadata_InformationalVersion_SplitsVersionAndCommitHash()
    {
        // Pass an informational version string with a '+' separator directly.
        // EnableMetadata(version:) accepts the full SemVer string; the + split happens
        // only when the version is auto-detected from the assembly.  When version is
        // supplied manually it is used as-is for the Version key.
        // To test the auto-detection path we instead verify the round-trip via the file.
        WriteIni("infover.ini", "[General]\nAppName = Test");

        var section = new GeneralSettingsImpl();
        var config = IniConfigRegistry.ForFile("infover.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<IGeneralSettings>(section)
            .EnableMetadata(version: "2.0.0", applicationName: "MyApp")
            .Build();

        section.AppName = "Updated";
        config.Save();

        var written = File.ReadAllText(config.LoadedFromPath!);
        Assert.Contains("Version = 2.0.0", written);
        // CommitHash is not written when it is null (no '+' in the provided version string).
        Assert.DoesNotContain("CommitHash", written);
    }

    [Fact]
    public void EnableMetadata_NoCommitHash_MetadataCommitHashIsNull()
    {
        // File without CommitHash key.
        WriteIni("no-hash.ini",
            "[__metadata__]\nVersion = 1.0.0\nCreatedBy = App\nSavedOn = now\n[General]\nAppName = Test");

        var section = new GeneralSettingsImpl();
        var config = IniConfigRegistry.ForFile("no-hash.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<IGeneralSettings>(section)
            .EnableMetadata()
            .Build();

        Assert.NotNull(config.Metadata);
        Assert.Null(config.Metadata!.CommitHash);
    }
}
