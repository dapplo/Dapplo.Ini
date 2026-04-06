// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Threading.Tasks;
using Dapplo.Ini.Interfaces;

namespace Dapplo.Ini.Tests;

/// <summary>
/// Tests for the generic meta-model access feature (issue: "Support applications which need
/// generic access").  Covers:
/// 1. <see cref="IniConfig.GetSection(string)"/> — look up a section by INI name
/// 2. <see cref="IniConfig.GetSections()"/> — enumerate all registered sections
/// 3. <see cref="IIniSection.GetKeys()"/> — enumerate declared property key names
/// 4. <see cref="IIniSection.GetPropertyType(string)"/> — retrieve the .NET type for a key
/// </summary>
[Collection("IniConfigRegistry")]
public sealed class GenericAccessTests : IDisposable
{
    private readonly string _tempDir;

    public GenericAccessTests()
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

    private IniConfig BuildConfig(string iniFile = "generic.ini")
    {
        WriteIni(iniFile, "[General]\nAppName = Test\nMaxRetries = 3\nEnableLogging = true\nThreshold = 1.5");
        var section = new GeneralSettingsImpl();
        return IniConfigRegistry.ForFile(iniFile)
            .AddSearchPath(_tempDir)
            .RegisterSection<IGeneralSettings>(section)
            .Build();
    }

    // ── IniConfig.GetSection(string) ───────────────────────────────────────────

    [Fact]
    public void GetSection_ByName_ReturnsCorrectSection()
    {
        var config = BuildConfig("get-by-name.ini");

        var section = config.GetSection("General");

        Assert.NotNull(section);
        Assert.Equal("General", section.SectionName);
    }

    [Fact]
    public void GetSection_ByName_IsCaseInsensitive()
    {
        var config = BuildConfig("get-case.ini");

        Assert.NotNull(config.GetSection("general"));
        Assert.NotNull(config.GetSection("GENERAL"));
        Assert.NotNull(config.GetSection("General"));
    }

    [Fact]
    public void GetSection_ByName_ReturnsNull_WhenNotFound()
    {
        var config = BuildConfig("get-null.ini");

        var section = config.GetSection("DoesNotExist");

        Assert.Null(section);
    }

    // ── IniConfig.GetSections() ────────────────────────────────────────────────

    [Fact]
    public void GetSections_ReturnsAllRegisteredSections()
    {
        WriteIni("multi.ini",
            "[General]\nAppName = A\nMaxRetries = 1\nEnableLogging = true\nThreshold = 0\n" +
            "[UserSettings]\nUsername = bob\nPassword = secret\nLoginCount = 0");

        var gen = new GeneralSettingsImpl();
        var usr = new UserSettingsImpl();
        var config = IniConfigRegistry.ForFile("multi.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<IGeneralSettings>(gen)
            .RegisterSection<IUserSettings>(usr)
            .Build();

        var sections = config.GetSections().ToList();

        Assert.Equal(2, sections.Count);
        Assert.Contains(sections, s => s.SectionName == "General");
        Assert.Contains(sections, s => s.SectionName == "UserSettings");
    }

    [Fact]
    public void GetSections_AllowsGenericRawValueAccess()
    {
        var config = BuildConfig("generic-raw.ini");

        // Iterate sections without knowing the concrete types at compile time.
        var rawValues = config.GetSections()
            .SelectMany(s => s.GetKeys().Select(k => (s.SectionName, Key: k, Value: s.GetRawValue(k))))
            .ToList();

        Assert.Contains(rawValues, t => t.Key == "AppName" && t.Value == "Test");
        Assert.Contains(rawValues, t => t.Key == "MaxRetries" && t.Value == "3");
    }

    // ── IIniSection.GetKeys() ──────────────────────────────────────────────────

    [Fact]
    public void GetKeys_ReturnsAllDeclaredPropertyKeys()
    {
        var config = BuildConfig("keys.ini");
        var section = config.GetSection<IGeneralSettings>();

        var keys = section.GetKeys().ToList();

        // IGeneralSettings declares: AppName, MaxRetries, EnableLogging, Threshold
        Assert.Contains("AppName", keys, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("MaxRetries", keys, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("EnableLogging", keys, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("Threshold", keys, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetKeys_Count_MatchesDeclaredProperties()
    {
        var config = BuildConfig("keys-count.ini");
        var section = config.GetSection<IGeneralSettings>();

        // IGeneralSettings has exactly 4 non-ignored properties.
        Assert.Equal(4, section.GetKeys().Count());
    }

    [Fact]
    public void GetKeys_Via_IIniSection_Interface_Works()
    {
        var config = BuildConfig("keys-iface.ini");

        // Use the non-generic interface only
        IIniSection section = config.GetSection("General")!;
        var keys = section.GetKeys().ToList();

        Assert.NotEmpty(keys);
        Assert.Contains("AppName", keys, StringComparer.OrdinalIgnoreCase);
    }

    // ── IIniSection.GetPropertyType(string) ────────────────────────────────────

    [Fact]
    public void GetPropertyType_ReturnsCorrectType_ForStringProperty()
    {
        var config = BuildConfig("type-string.ini");
        var section = config.GetSection<IGeneralSettings>();

        var type = section.GetPropertyType("AppName");

        Assert.Equal(typeof(string), type);
    }

    [Fact]
    public void GetPropertyType_ReturnsCorrectType_ForIntProperty()
    {
        var config = BuildConfig("type-int.ini");
        var section = config.GetSection<IGeneralSettings>();

        var type = section.GetPropertyType("MaxRetries");

        Assert.Equal(typeof(int), type);
    }

    [Fact]
    public void GetPropertyType_ReturnsCorrectType_ForBoolProperty()
    {
        var config = BuildConfig("type-bool.ini");
        var section = config.GetSection<IGeneralSettings>();

        var type = section.GetPropertyType("EnableLogging");

        Assert.Equal(typeof(bool), type);
    }

    [Fact]
    public void GetPropertyType_ReturnsCorrectType_ForDoubleProperty()
    {
        var config = BuildConfig("type-double.ini");
        var section = config.GetSection<IGeneralSettings>();

        var type = section.GetPropertyType("Threshold");

        Assert.Equal(typeof(double), type);
    }

    [Fact]
    public void GetPropertyType_IsCaseInsensitive()
    {
        var config = BuildConfig("type-case.ini");
        var section = config.GetSection<IGeneralSettings>();

        Assert.Equal(typeof(string), section.GetPropertyType("appname"));
        Assert.Equal(typeof(string), section.GetPropertyType("APPNAME"));
        Assert.Equal(typeof(string), section.GetPropertyType("AppName"));
    }

    [Fact]
    public void GetPropertyType_ReturnsNull_ForUnknownKey()
    {
        var config = BuildConfig("type-unknown.ini");
        var section = config.GetSection<IGeneralSettings>();

        var type = section.GetPropertyType("NonExistentKey");

        Assert.Null(type);
    }

    [Fact]
    public void GetPropertyType_Via_IIniSection_Interface_Works()
    {
        var config = BuildConfig("type-iface.ini");

        IIniSection section = config.GetSection("General")!;
        var type = section.GetPropertyType("MaxRetries");

        Assert.Equal(typeof(int), type);
    }

    // ── End-to-end generic iteration scenario ──────────────────────────────────

    [Fact]
    public void CanInspectFullMetaModel_WithoutKnowingConcreteTypes()
    {
        WriteIni("meta.ini",
            "[General]\nAppName = MetaApp\nMaxRetries = 5\nEnableLogging = false\nThreshold = 2.7");

        var section = new GeneralSettingsImpl();
        var config = IniConfigRegistry.ForFile("meta.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<IGeneralSettings>(section)
            .Build();

        // Simulate a generic inspection tool that doesn't reference the section type.
        var results = new List<(string Section, string Key, Type? Type, string? RawValue)>();
        foreach (var s in config.GetSections())
        {
            foreach (var key in s.GetKeys())
            {
                results.Add((s.SectionName, key, s.GetPropertyType(key), s.GetRawValue(key)));
            }
        }

        Assert.Contains(results, r => r.Section == "General" && r.Key == "AppName" && r.Type == typeof(string) && r.RawValue == "MetaApp");
        Assert.Contains(results, r => r.Section == "General" && r.Key == "MaxRetries" && r.Type == typeof(int) && r.RawValue == "5");
        Assert.Contains(results, r => r.Section == "General" && r.Key == "EnableLogging" && r.Type == typeof(bool) && r.RawValue == "false");
        Assert.Contains(results, r => r.Section == "General" && r.Key == "Threshold" && r.Type == typeof(double) && r.RawValue == "2.7");
    }
}
