// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace Dapplo.Ini.Tests;

/// <summary>
/// Integration tests for <c>[IniValue(EmptyWhenNull = true)]</c>,
/// <c>[IniSection(EmptyWhenNull = true)]</c>, and
/// <c>IniConfigBuilder.EmptyWhenNull()</c>.
/// Verifies that absent/null INI values produce empty strings, lists, and arrays
/// rather than <c>null</c> when the flag is set at any of the three levels.
/// </summary>
[Collection("IniConfigRegistry")]
public sealed class EmptyWhenNullTests : IDisposable
{
    private readonly string _tempDir;

    public EmptyWhenNullTests()
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

    // ── Property-level [IniValue(EmptyWhenNull=true)] ─────────────────────────

    [Fact]
    public void Build_WithNoFile_StringProperty_EmptyWhenNull_ReturnsEmptyString()
    {
        var section = new EmptyWhenNullSettingsImpl();
        IniConfigRegistry.ForFile("ewn_nofile.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<IEmptyWhenNullSettings>(section)
            .Build();

        Assert.Equal(string.Empty, section.Description);
    }

    [Fact]
    public void Build_WithNoFile_StringProperty_WithDefault_ReturnsDefault()
    {
        var section = new EmptyWhenNullSettingsImpl();
        IniConfigRegistry.ForFile("ewn_default.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<IEmptyWhenNullSettings>(section)
            .Build();

        // [IniValue(DefaultValue = "hello", EmptyWhenNull = true)] — default wins
        Assert.Equal("hello", section.Greeting);
    }

    [Fact]
    public void Build_WithNoFile_ListProperty_EmptyWhenNull_ReturnsEmptyList()
    {
        var section = new EmptyWhenNullSettingsImpl();
        IniConfigRegistry.ForFile("ewn_list.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<IEmptyWhenNullSettings>(section)
            .Build();

        Assert.NotNull(section.Tags);
        Assert.Empty(section.Tags!);
    }

    [Fact]
    public void Build_WithNoFile_IListProperty_EmptyWhenNull_ReturnsEmptyList()
    {
        var section = new EmptyWhenNullSettingsImpl();
        IniConfigRegistry.ForFile("ewn_ilist.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<IEmptyWhenNullSettings>(section)
            .Build();

        Assert.NotNull(section.Numbers);
        Assert.Empty(section.Numbers!);
    }

    [Fact]
    public void Build_WithNoFile_ArrayProperty_EmptyWhenNull_ReturnsEmptyArray()
    {
        var section = new EmptyWhenNullSettingsImpl();
        IniConfigRegistry.ForFile("ewn_array.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<IEmptyWhenNullSettings>(section)
            .Build();

        Assert.NotNull(section.Codes);
        Assert.Empty(section.Codes!);
    }

    [Fact]
    public void Build_WithNoFile_RegularNullableString_ReturnsNull()
    {
        var section = new EmptyWhenNullSettingsImpl();
        IniConfigRegistry.ForFile("ewn_null.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<IEmptyWhenNullSettings>(section)
            .Build();

        // NullableString has no EmptyWhenNull — it should remain null
        Assert.Null(section.NullableString);
    }


    [Fact]
    public void Build_WithFile_EmptyKeyValue_StringProperty_EmptyWhenNull_ReturnsEmptyString()
    {
        WriteIni("ewn_emptykey.ini", "[EmptyWhenNull]\nDescription =\n");

        var section = new EmptyWhenNullSettingsImpl();
        IniConfigRegistry.ForFile("ewn_emptykey.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<IEmptyWhenNullSettings>(section)
            .Build();

        Assert.Equal(string.Empty, section.Description);
    }

    [Fact]
    public void Build_WithFile_EmptyKeyValue_ListProperty_EmptyWhenNull_ReturnsEmptyList()
    {
        WriteIni("ewn_emptylist.ini", "[EmptyWhenNull]\nTags =\n");

        var section = new EmptyWhenNullSettingsImpl();
        IniConfigRegistry.ForFile("ewn_emptylist.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<IEmptyWhenNullSettings>(section)
            .Build();

        Assert.NotNull(section.Tags);
        Assert.Empty(section.Tags!);
    }

    [Fact]
    public void Build_WithFile_AbsentKey_StringProperty_EmptyWhenNull_ReturnsEmptyString()
    {
        // Section present but Description key is absent
        WriteIni("ewn_absent.ini", "[EmptyWhenNull]\nGreeting = world\n");

        var section = new EmptyWhenNullSettingsImpl();
        IniConfigRegistry.ForFile("ewn_absent.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<IEmptyWhenNullSettings>(section)
            .Build();

        Assert.Equal(string.Empty, section.Description);
    }


    [Fact]
    public void Build_WithFile_StringProperty_EmptyWhenNull_LoadsValueNormally()
    {
        WriteIni("ewn_value.ini", "[EmptyWhenNull]\nDescription = Hello World\n");

        var section = new EmptyWhenNullSettingsImpl();
        IniConfigRegistry.ForFile("ewn_value.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<IEmptyWhenNullSettings>(section)
            .Build();

        Assert.Equal("Hello World", section.Description);
    }

    [Fact]
    public void Build_WithFile_ListProperty_EmptyWhenNull_LoadsValuesNormally()
    {
        WriteIni("ewn_listval.ini", "[EmptyWhenNull]\nTags = alpha,beta,gamma\n");

        var section = new EmptyWhenNullSettingsImpl();
        IniConfigRegistry.ForFile("ewn_listval.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<IEmptyWhenNullSettings>(section)
            .Build();

        Assert.Equal(new List<string> { "alpha", "beta", "gamma" }, section.Tags);
    }


    [Fact]
    public void ResetToDefaults_StringProperty_EmptyWhenNull_RestoresEmptyString()
    {
        var section = new EmptyWhenNullSettingsImpl();
        IniConfigRegistry.ForFile("ewn_reset.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<IEmptyWhenNullSettings>(section)
            .Build();

        section.Description = "changed";
        Assert.Equal("changed", section.Description);

        section.ResetToDefaults();
        Assert.Equal(string.Empty, section.Description);
    }

    [Fact]
    public void ResetToDefaults_ListProperty_EmptyWhenNull_RestoresEmptyList()
    {
        var section = new EmptyWhenNullSettingsImpl();
        IniConfigRegistry.ForFile("ewn_resetlist.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<IEmptyWhenNullSettings>(section)
            .Build();

        section.Tags = new List<string> { "x", "y" };
        Assert.Equal(new List<string> { "x", "y" }, section.Tags);

        section.ResetToDefaults();
        Assert.NotNull(section.Tags);
        Assert.Empty(section.Tags!);
    }

    // ── Section-level [IniSection(EmptyWhenNull=true)] ────────────────────────

    [Fact]
    public void SectionLevel_WithNoFile_StringProperty_ReturnsEmptyString()
    {
        var section = new SectionEmptyWhenNullSettingsImpl();
        IniConfigRegistry.ForFile("sewn_nofile.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<ISectionEmptyWhenNullSettings>(section)
            .Build();

        Assert.Equal(string.Empty, section.Label);
    }

    [Fact]
    public void SectionLevel_WithNoFile_ListProperty_ReturnsEmptyList()
    {
        var section = new SectionEmptyWhenNullSettingsImpl();
        IniConfigRegistry.ForFile("sewn_list.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<ISectionEmptyWhenNullSettings>(section)
            .Build();

        Assert.NotNull(section.Items);
        Assert.Empty(section.Items!);
    }

    [Fact]
    public void SectionLevel_WithNoFile_PropertyWithDefault_ReturnsDefault()
    {
        var section = new SectionEmptyWhenNullSettingsImpl();
        IniConfigRegistry.ForFile("sewn_default.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<ISectionEmptyWhenNullSettings>(section)
            .Build();

        // DefaultValue wins over EmptyWhenNull in ResetToDefaults
        Assert.Equal("hello", section.WithDefault);
    }

    [Fact]
    public void SectionLevel_WithNoFile_ValueTypeProperty_ReturnsDefault()
    {
        var section = new SectionEmptyWhenNullSettingsImpl();
        IniConfigRegistry.ForFile("sewn_valuetype.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<ISectionEmptyWhenNullSettings>(section)
            .Build();

        // Value types are not affected by EmptyWhenNull
        Assert.Equal(0, section.Counter);
    }

    [Fact]
    public void SectionLevel_WithFile_EmptyKeyValue_StringProperty_ReturnsEmptyString()
    {
        WriteIni("sewn_emptykey.ini", "[SectionEmptyWhenNull]\nLabel =\n");

        var section = new SectionEmptyWhenNullSettingsImpl();
        IniConfigRegistry.ForFile("sewn_emptykey.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<ISectionEmptyWhenNullSettings>(section)
            .Build();

        Assert.Equal(string.Empty, section.Label);
    }

    [Fact]
    public void SectionLevel_ResetToDefaults_StringProperty_RestoresEmptyString()
    {
        var section = new SectionEmptyWhenNullSettingsImpl();
        IniConfigRegistry.ForFile("sewn_reset.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<ISectionEmptyWhenNullSettings>(section)
            .Build();

        section.Label = "changed";
        section.ResetToDefaults();
        Assert.Equal(string.Empty, section.Label);
    }

    // ── IniConfig-level IniConfigBuilder.EmptyWhenNull() ─────────────────────

    [Fact]
    public void ConfigLevel_WithNoFile_PropertyWithDefault_DefaultWins()
    {
        var section = new GeneralSettingsImpl();
        IniConfigRegistry.ForFile("cewn_nofile.ini")
            .AddSearchPath(_tempDir)
            .EmptyWhenNull()
            .RegisterSection<IGeneralSettings>(section)
            .Build();

        // IGeneralSettings.AppName has [IniValue(DefaultValue = "MyApp")] — default wins
        Assert.Equal("MyApp", section.AppName);
    }

    [Fact]
    public void ConfigLevel_WithNoFile_StringPropertyNoDefault_ReturnsEmptyString()
    {
        // NullableString in IEmptyWhenNullSettings has no default and no property-level EmptyWhenNull
        var section = new EmptyWhenNullSettingsImpl();
        IniConfigRegistry.ForFile("cewn_str.ini")
            .AddSearchPath(_tempDir)
            .EmptyWhenNull()
            .RegisterSection<IEmptyWhenNullSettings>(section)
            .Build();

        // Config-level flag causes NullableString to return empty instead of null
        Assert.Equal(string.Empty, section.NullableString);
    }

    [Fact]
    public void ConfigLevel_WithFile_EmptyKeyValue_StringPropertyNoDefault_ReturnsEmptyString()
    {
        WriteIni("cewn_emptykey.ini", "[EmptyWhenNull]\nNullableString =\n");

        var section = new EmptyWhenNullSettingsImpl();
        IniConfigRegistry.ForFile("cewn_emptykey.ini")
            .AddSearchPath(_tempDir)
            .EmptyWhenNull()
            .RegisterSection<IEmptyWhenNullSettings>(section)
            .Build();

        Assert.Equal(string.Empty, section.NullableString);
    }

    [Fact]
    public void ConfigLevel_WithFile_RealValue_StringProperty_LoadsValueNormally()
    {
        WriteIni("cewn_value.ini", "[EmptyWhenNull]\nNullableString = actual value\n");

        var section = new EmptyWhenNullSettingsImpl();
        IniConfigRegistry.ForFile("cewn_value.ini")
            .AddSearchPath(_tempDir)
            .EmptyWhenNull()
            .RegisterSection<IEmptyWhenNullSettings>(section)
            .Build();

        Assert.Equal("actual value", section.NullableString);
    }

    [Fact]
    public void ConfigLevel_WithoutEmptyWhenNull_StringPropertyNoDefault_ReturnsNull()
    {
        var section = new EmptyWhenNullSettingsImpl();
        IniConfigRegistry.ForFile("cewn_noset.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<IEmptyWhenNullSettings>(section)
            .Build();

        // GlobalEmptyWhenNull is false by default — NullableString stays null
        Assert.Null(section.NullableString);
    }

    [Fact]
    public void ConfigLevel_ResetToDefaults_StringPropertyNoDefault_RestoresEmptyString()
    {
        var section = new EmptyWhenNullSettingsImpl();
        IniConfigRegistry.ForFile("cewn_reset.ini")
            .AddSearchPath(_tempDir)
            .EmptyWhenNull()
            .RegisterSection<IEmptyWhenNullSettings>(section)
            .Build();

        section.NullableString = "something";
        Assert.Equal("something", section.NullableString);

        section.ResetToDefaults();
        Assert.Equal(string.Empty, section.NullableString);
    }
}
