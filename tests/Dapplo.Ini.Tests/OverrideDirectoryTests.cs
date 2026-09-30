// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Concurrent;
using Dapplo.Ini;
using Dapplo.Ini.Interfaces;

namespace Dapplo.Ini.Tests;

/// <summary>
/// Tests for <see cref="IniConfigBuilder.SetOverrideDirectory"/> and the <see cref="IniConfigBuilder.AddAppDataPath"/>
/// write fallback, over real temporary directories. The AppData directory is simulated with
/// <c>AddAppDataDirectory</c> so the user's real application-data folder is never touched.
/// </summary>
[Collection("IniConfigRegistry")]
public sealed class OverrideDirectoryTests : IDisposable
{
    private const string FileName = "override-test.ini";
    private readonly string _root;
    private readonly string _startupDir;
    private readonly string _appDataDir;
    private readonly string _overrideDir;

    public OverrideDirectoryTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "DapploIniOverride_" + Guid.NewGuid().ToString("N"));
        _startupDir = Path.Combine(_root, "startup");
        _appDataDir = Path.Combine(_root, "appdata");
        _overrideDir = Path.Combine(_root, "override");
        Directory.CreateDirectory(_startupDir);
        IniConfigRegistry.Clear();
    }

    public void Dispose()
    {
        IniConfigRegistry.Clear();
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private static void Write(string directory, string fileName, string content)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, fileName), content);
    }

    private IniConfigBuilder Builder(string? overrideDirectory, ErrorListener? listener = null)
    {
        var builder = IniConfigRegistry.ForFile(FileName)
            .SetOverrideDirectory(overrideDirectory)
            .AddSearchPath(_startupDir)
            .AddAppDataDirectory(_appDataDir);
        if (listener != null)
            builder.AddListener(listener);
        return builder;
    }

    // ── Main file ─────────────────────────────────────────────────────────────

    [Fact]
    public void Override_EmptyWhileAppDataHasFile_ReadsAndSavesOverride_AppDataUntouched()
    {
        Directory.CreateDirectory(_overrideDir);
        const string appDataContent = "[General]\nAppName = FromAppData\n";
        Write(_appDataDir, FileName, appDataContent);

        var section = new GeneralSettingsImpl();
        var config = Builder(_overrideDir).RegisterSection<IGeneralSettings>(section).Build();

        Assert.Equal("MyApp", section.AppName);
        Assert.Equal(_overrideDir, config.OverrideDirectory);
        var expectedPath = Path.Combine(_overrideDir, FileName);
        Assert.Equal(expectedPath, config.LoadedFromPath);

        section.AppName = "Saved";
        config.Save();

        Assert.Contains("AppName = Saved", File.ReadAllText(expectedPath));
        Assert.Equal(appDataContent, File.ReadAllText(Path.Combine(_appDataDir, FileName)));
    }

    [Fact]
    public void Override_HasFile_ReadsAndSavesIt()
    {
        Write(_overrideDir, FileName, "[General]\nAppName = FromOverride\n");
        Write(_startupDir, FileName, "[General]\nAppName = FromStartup\n");

        var section = new GeneralSettingsImpl();
        var config = Builder(_overrideDir).RegisterSection<IGeneralSettings>(section).Build();

        Assert.Equal("FromOverride", section.AppName);
        Assert.Equal(Path.Combine(_overrideDir, FileName), config.LoadedFromPath);

        section.AppName = "Changed";
        config.Save();
        Assert.Contains("AppName = Changed", File.ReadAllText(Path.Combine(_overrideDir, FileName)));
        Assert.Contains("AppName = FromStartup", File.ReadAllText(Path.Combine(_startupDir, FileName)));
    }

    [Fact]
    public void Override_Reload_KeepsReadingOverrideFile()
    {
        Write(_overrideDir, FileName, "[General]\nAppName = First\n");
        Write(_appDataDir, FileName, "[General]\nAppName = FromAppData\n");

        var section = new GeneralSettingsImpl();
        var config = Builder(_overrideDir).RegisterSection<IGeneralSettings>(section).Build();
        Write(_overrideDir, FileName, "[General]\nAppName = Second\n");
        config.Reload();

        Assert.Equal("Second", section.AppName);
    }

    [Fact]
    public void Override_Missing_IsCreatedAndUsedAsSaveTarget()
    {
        var nested = Path.Combine(_overrideDir, "nested", "deeper");
        Write(_appDataDir, FileName, "[General]\nAppName = FromAppData\n");

        var section = new GeneralSettingsImpl();
        var config = Builder(nested).RegisterSection<IGeneralSettings>(section).Build();

        Assert.True(Directory.Exists(nested));
        Assert.Equal("MyApp", section.AppName);
        config.Save();
        Assert.True(File.Exists(Path.Combine(nested, FileName)));
        // Only the INI file is left behind, not the write probe.
        Assert.Single(Directory.GetFiles(nested));
    }

    [Fact]
    public void Override_PathIsMadeAbsolute()
    {
        var unnormalized = Path.Combine(_root, "x", "..", "override");

        var config = Builder(unnormalized).RegisterSection<IGeneralSettings>(new GeneralSettingsImpl()).Build();

        Assert.Equal(_overrideDir, config.OverrideDirectory);
        Assert.True(Path.IsPathRooted(config.OverrideDirectory));
    }

    [Fact]
    public void Override_IsAFile_ReportsErrorAndFallsBackToSearchPaths()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(_overrideDir, "not a directory");
        Write(_appDataDir, FileName, "[General]\nAppName = FromAppData\n");
        var listener = new ErrorListener();

        var section = new GeneralSettingsImpl();
        var config = Builder(_overrideDir, listener).RegisterSection<IGeneralSettings>(section).Build();

        Assert.Null(config.OverrideDirectory);
        Assert.Equal("FromAppData", section.AppName);
        Assert.Equal(Path.Combine(_appDataDir, FileName), config.LoadedFromPath);
        var error = Assert.Single(listener.Errors);
        Assert.Equal("OverrideDirectory", error.Operation);
        Assert.Contains(_overrideDir, error.Exception.Message);
    }

    [Fact]
    public void Override_InvalidPath_ReportsErrorAndFallsBackToSearchPaths()
    {
        Write(_startupDir, FileName, "[General]\nAppName = FromStartup\n");
        var listener = new ErrorListener();

        var section = new GeneralSettingsImpl();
        var config = Builder(Path.Combine(_root, "in\0valid"), listener).RegisterSection<IGeneralSettings>(section).Build();

        Assert.Null(config.OverrideDirectory);
        Assert.Equal("FromStartup", section.AppName);
        Assert.Contains(listener.Errors, e => e.Operation == "OverrideDirectory");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Override_NullOrWhitespace_IsNoOp(string? overrideDirectory)
    {
        Write(_appDataDir, FileName, "[General]\nAppName = FromAppData\n");
        var listener = new ErrorListener();

        var section = new GeneralSettingsImpl();
        var config = Builder(overrideDirectory, listener).RegisterSection<IGeneralSettings>(section).Build();

        Assert.Null(config.OverrideDirectory);
        Assert.Equal("FromAppData", section.AppName);
        Assert.Equal(Path.Combine(_appDataDir, FileName), config.LoadedFromPath);
        Assert.Empty(listener.Errors);
    }

    [Fact]
    public void Override_NullAfterValue_KeepsValue()
    {
        var config = IniConfigRegistry.ForFile(FileName)
            .SetOverrideDirectory(_overrideDir)
            .SetOverrideDirectory(null)
            .AddSearchPath(_startupDir)
            .RegisterSection<IGeneralSettings>(new GeneralSettingsImpl())
            .Build();

        Assert.Equal(_overrideDir, config.OverrideDirectory);
    }

    [Fact]
    public void Override_WinsOverSetWritablePath()
    {
        var writable = Path.Combine(_startupDir, "explicit.ini");

        var config = Builder(_overrideDir)
            .SetWritablePath(writable)
            .RegisterSection<IGeneralSettings>(new GeneralSettingsImpl())
            .Build();

        Assert.Equal(Path.Combine(_overrideDir, FileName), config.LoadedFromPath);
    }

    // ── Defaults and constants files ──────────────────────────────────────────

    [Fact]
    public void Override_DefaultsFile_OverrideDirectoryIsCheckedFirst()
    {
        Write(_overrideDir, "defaults.ini", "[General]\nAppName = OverrideDefault\n");
        Write(_startupDir, "defaults.ini", "[General]\nAppName = StartupDefault\nMaxRetries = 5\n");

        var section = new GeneralSettingsImpl();
        Builder(_overrideDir).AddDefaultsFile("defaults.ini").RegisterSection<IGeneralSettings>(section).Build();

        Assert.Equal("OverrideDefault", section.AppName);
        // First match wins: the startup defaults file is not read at all.
        Assert.Equal(42, section.MaxRetries);
    }

    [Fact]
    public void Override_DefaultsFile_FallsBackToSearchPaths()
    {
        Directory.CreateDirectory(_overrideDir);
        Write(_startupDir, "defaults.ini", "[General]\nAppName = StartupDefault\n");

        var section = new GeneralSettingsImpl();
        Builder(_overrideDir).AddDefaultsFile("defaults.ini").RegisterSection<IGeneralSettings>(section).Build();

        Assert.Equal("StartupDefault", section.AppName);
    }

    [Fact]
    public void Override_ConstantsFile_IsNeverReadFromOverrideDirectory()
    {
        Write(_overrideDir, "fixed.ini", "[General]\nAppName = UserLocked\n");

        var section = new GeneralSettingsImpl();
        Builder(_overrideDir).AddConstantsFile("fixed.ini").RegisterSection<IGeneralSettings>(section).Build();

        Assert.Equal("MyApp", section.AppName);
        Assert.False(section.IsConstant(nameof(IGeneralSettings.AppName)));
    }

    [Fact]
    public void Override_ConstantsFile_AdminCopyInSearchPathWins()
    {
        Write(_overrideDir, "fixed.ini", "[General]\nAppName = UserLocked\nMaxRetries = 1\n");
        Write(_startupDir, "fixed.ini", "[General]\nAppName = AdminLocked\n");
        Write(_overrideDir, FileName, "[General]\nAppName = UserValue\nMaxRetries = 7\n");

        var section = new GeneralSettingsImpl();
        Builder(_overrideDir).AddConstantsFile("fixed.ini").RegisterSection<IGeneralSettings>(section).Build();

        Assert.Equal("AdminLocked", section.AppName);
        Assert.True(section.IsConstant(nameof(IGeneralSettings.AppName)));
        Assert.Equal(7, section.MaxRetries);
        Assert.False(section.IsConstant(nameof(IGeneralSettings.MaxRetries)));
    }

    // ── AppData write fallback ────────────────────────────────────────────────

    [Fact]
    public void NoFileAnywhere_StartupBeforeAppData_SavesToAppData()
    {
        var config = Builder(null).RegisterSection<IGeneralSettings>(new GeneralSettingsImpl()).Build();

        var expectedPath = Path.Combine(_appDataDir, FileName);
        Assert.Equal(expectedPath, config.LoadedFromPath);
        config.Save();
        Assert.True(File.Exists(expectedPath));
        Assert.False(File.Exists(Path.Combine(_startupDir, FileName)));
    }

    [Fact]
    public void FileInStartupPath_IsStillReadAndSavedThere()
    {
        Write(_startupDir, FileName, "[General]\nAppName = FromStartup\n");

        var section = new GeneralSettingsImpl();
        var config = Builder(null).RegisterSection<IGeneralSettings>(section).Build();

        Assert.Equal("FromStartup", section.AppName);
        Assert.Equal(Path.Combine(_startupDir, FileName), config.LoadedFromPath);
    }

    [Fact]
    public void NoFileAnywhere_SetWritablePathWinsOverAppData()
    {
        var writable = Path.Combine(_startupDir, "explicit.ini");

        var config = Builder(null)
            .SetWritablePath(writable)
            .RegisterSection<IGeneralSettings>(new GeneralSettingsImpl())
            .Build();

        Assert.Equal(writable, config.LoadedFromPath);
    }

    [Fact]
    public void NoFileAnywhere_WithoutAppData_UsesFirstExistingSearchPath()
    {
        var missing = Path.Combine(_root, "missing");

        var config = IniConfigRegistry.ForFile(FileName)
            .AddSearchPath(missing)
            .AddSearchPath(_startupDir)
            .RegisterSection<IGeneralSettings>(new GeneralSettingsImpl())
            .Build();

        Assert.Equal(Path.Combine(_startupDir, FileName), config.LoadedFromPath);
    }

    private sealed class ErrorListener : IniConfigListenerBase
    {
        public ConcurrentQueue<(string Operation, Exception Exception)> Errors { get; } = new();
        public override void OnError(string operation, Exception exception) => Errors.Enqueue((operation, exception));
    }
}
