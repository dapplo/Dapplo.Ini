// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Dapplo.Ini;
using Dapplo.Ini.Interfaces;

namespace Dapplo.Ini.Tests;

/// <summary>
/// Tests for the <see cref="IValueSource"/> external-value-source concept and
/// <see cref="IniConfigBuilder.AddValueSource"/>.
/// </summary>
[Collection("IniConfigRegistry")]
public sealed class ValueSourceTests : IDisposable
{
    private readonly string _tempDir;

    public ValueSourceTests()
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
    public void ValueSource_OverridesFileValue()
    {
        WriteIni("source.ini", "[ReloadSection]\nValue = from-file");

        var source = new DictionaryValueSource();
        source.SetValue("ReloadSection", "Value", "from-source");

        var section = new ReloadSettingsImpl();
        IniConfigRegistry.ForFile("source.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<IReloadSettings>(section)
            .AddValueSource(source)
            .Build();

        // External source wins over file
        Assert.Equal("from-source", section.Value);
    }

    [Fact]
    public void ValueSource_IsAppliedOnReload()
    {
        WriteIni("source2.ini", "[ReloadSection]\nValue = file-value");

        var source = new DictionaryValueSource();
        source.SetValue("ReloadSection", "Value", "source-v1");

        var section = new ReloadSettingsImpl();
        var config = IniConfigRegistry.ForFile("source2.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<IReloadSettings>(section)
            .AddValueSource(source)
            .Build();

        Assert.Equal("source-v1", section.Value);

        // Update the source value and reload
        source.SetValue("ReloadSection", "Value", "source-v2");
        config.Reload();

        Assert.Equal("source-v2", section.Value);
    }

    [Fact]
    public void ValueSource_WhenNoValueForKey_FileValueIsUsed()
    {
        WriteIni("source3.ini", "[ReloadSection]\nValue = from-file");

        // Source provides a value for a different section/key
        var source = new DictionaryValueSource();
        source.SetValue("OtherSection", "OtherKey", "something");

        var section = new ReloadSettingsImpl();
        IniConfigRegistry.ForFile("source3.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<IReloadSettings>(section)
            .AddValueSource(source)
            .Build();

        // Source doesn't override this key → file value wins
        Assert.Equal("from-file", section.Value);
    }

    [Fact]
    public void ValueChangedEventArgs_Ctor_SetsProperties()
    {
        var args = new ValueChangedEventArgs("General", "AppName");
        Assert.Equal("General", args.SectionName);
        Assert.Equal("AppName", args.Key);
    }

    [Fact]
    public void ValueChangedEventArgs_DefaultCtor_HasNullProperties()
    {
        var args = new ValueChangedEventArgs();
        Assert.Null(args.SectionName);
        Assert.Null(args.Key);
    }
}
