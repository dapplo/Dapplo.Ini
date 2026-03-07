// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Dapplo.Ini.Config.Configuration;

namespace Dapplo.Ini.Config.Tests;

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

    [Fact]
    public void AfterLoad_IsCalledAfterBuild()
    {
        WriteIni("life.ini", "[LifecycleSettings]\nValue = hello");

        var section = new LifecycleSettingsImpl();
        IniConfigRegistry.ForFile("life.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<ILifecycleSettings>(section)
            .Build();

        Assert.True(section.AfterLoadCalled);
    }

    [Fact]
    public void BeforeSave_And_AfterSave_AreCalledOnSave()
    {
        WriteIni("life2.ini", "[LifecycleSettings]\nValue = initial");

        var section = new LifecycleSettingsImpl();
        var config = IniConfigRegistry.ForFile("life2.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<ILifecycleSettings>(section)
            .Build();

        config.Save();

        Assert.True(section.BeforeSaveCalled);
        Assert.True(section.AfterSaveCalled);
    }
}
