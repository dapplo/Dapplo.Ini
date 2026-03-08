// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Dapplo.IniConfig.Configuration;

namespace Dapplo.IniConfig.Tests;

public sealed class LifecycleHookTests : IDisposable
{
    private readonly string _tempDir;

    public LifecycleHookTests()
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

    private void WriteIni(string fileName, string content)
        => File.WriteAllText(Path.Combine(_tempDir, fileName), content);

    // ── Legacy (non-generic) pattern ──────────────────────────────────────────

    [Fact]
    public void Legacy_AfterLoad_IsCalledAfterBuild()
    {
        WriteIni("life.ini", "[LegacyLifecycle]\nValue = hello");

        var section = new LegacyLifecycleSettingsImpl();
        IniConfigRegistry.ForFile("life.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<ILegacyLifecycleSettings>(section)
            .Build();

        Assert.True(section.AfterLoadCalled);
    }

    [Fact]
    public void Legacy_BeforeSave_And_AfterSave_AreCalledOnSave()
    {
        WriteIni("life2.ini", "[LegacyLifecycle]\nValue = initial");

        var section = new LegacyLifecycleSettingsImpl();
        var config = IniConfigRegistry.ForFile("life2.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<ILegacyLifecycleSettings>(section)
            .Build();

        config.Save();

        Assert.True(section.BeforeSaveCalled);
        Assert.True(section.AfterSaveCalled);
    }

    // ── New generic static-virtual pattern ────────────────────────────────────

    [Fact]
    public void Generic_AfterLoad_IsCalledAfterBuild()
    {
        WriteIni("life3.ini", "[LifecycleSettings]\nValue = hello");

        var section = new LifecycleSettingsImpl();
        IniConfigRegistry.ForFile("life3.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<ILifecycleSettings>(section)
            .Build();

        // ILifecycleSettings.OnAfterLoad is overridden in the interface to set AfterLoadCalled = true
        Assert.True(section.AfterLoadCalled);
    }

    [Fact]
    public void Generic_BeforeSave_And_AfterSave_AreCalledOnSave()
    {
        WriteIni("life4.ini", "[LifecycleSettings]\nValue = initial");

        var section = new LifecycleSettingsImpl();
        var config = IniConfigRegistry.ForFile("life4.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<ILifecycleSettings>(section)
            .Build();

        config.Save();

        // ILifecycleSettings.OnBeforeSave / OnAfterSave are overridden directly in the interface
        Assert.True(section.BeforeSaveCalled);
        Assert.True(section.AfterSaveCalled);
    }

    [Fact]
    public void Generic_BeforeSave_ReturningFalse_CancelsSave()
    {
        var iniFile = Path.Combine(_tempDir, "life5.ini");
        File.WriteAllText(iniFile, "[CancelSave]\nValue = original");

        var section = new CancelSaveSettingsImpl { Value = "changed" };
        var config = IniConfigRegistry.ForFile("life5.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<ICancelSaveSettings>(section)
            .Build();

        config.Save();

        // ICancelSaveSettings.OnBeforeSave always returns false, so the file must be unchanged
        var contents = File.ReadAllText(iniFile);
        Assert.Contains("original", contents);
    }
}
