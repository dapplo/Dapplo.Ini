// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Dapplo.Ini.Interfaces;

namespace Dapplo.Ini.Tests;

/// <summary>
/// The user file keeps only user values: values read from it or set by the application. Inherited values (compiled
/// defaults, defaults files) are written as commented-out lines, constants are never written.
/// </summary>
[Collection("IniConfigRegistry")]
public sealed class UserValuesTests : IDisposable
{
    private readonly string _tempDir;

    public UserValuesTests()
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

    private string Read(string fileName) => File.ReadAllText(Path.Combine(_tempDir, fileName)).Replace("\r\n", "\n");

    private static string[] ValueLines(string content)
        => content.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0 && l[0] != ';' && l[0] != '#' && l[0] != '[').ToArray();

    private IniConfigBuilder General(string fileName, GeneralSettingsImpl section)
        => IniConfigRegistry.ForFile(fileName).AddSearchPath(_tempDir).RegisterSection<IGeneralSettings>(section);

    // ── Only user values are written ─────────────────────────────────────────

    [Fact]
    public void Save_WritesOnlyUserValues_AndInheritedValuesAsComments()
    {
        var section = new GeneralSettingsImpl();
        using var config = General("only-user.ini", section).Build();

        section.MaxRetries = 5;
        config.Save();

        var content = Read("only-user.ini");
        Assert.Equal(new[] { "MaxRetries = 5" }, ValueLines(content));
        Assert.Contains("; AppName = MyApp", content);
        Assert.Contains("; Application name\n; AppName = MyApp", content);
        Assert.Contains("; EnableLogging = True", content);
        Assert.True(section.IsUserValue("MaxRetries"));
        Assert.False(section.IsUserValue("AppName"));
    }

    [Fact]
    public void CommentedInheritedLines_AreNotReadBackAsValues()
    {
        var section = new GeneralSettingsImpl();
        using (var config = General("comments.ini", section).Build())
        {
            section.MaxRetries = 5;
            config.Save();
        }
        IniConfigRegistry.Clear();

        var reloaded = new GeneralSettingsImpl();
        using var again = General("comments.ini", reloaded).Build();
        Assert.Equal(5, reloaded.MaxRetries);
        Assert.True(reloaded.IsUserValue("MaxRetries"));
        Assert.False(reloaded.IsUserValue("AppName"));
        Assert.Null(reloaded.GetRawValue("AppName"));
    }

    [Fact]
    public void WriteInheritedValuesAsComments_Disabled_LeavesInheritedKeysOut()
    {
        var section = new GeneralSettingsImpl();
        using var config = General("no-comments.ini", section).WriteInheritedValuesAsComments(false).Build();

        section.MaxRetries = 5;
        config.Save();

        var content = Read("no-comments.ini");
        Assert.DoesNotContain("AppName", content);
        Assert.Contains("MaxRetries = 5", content);
    }

    [Fact]
    public void ChangedDefaultsFile_ReachesAUserWhoSavedBefore()
    {
        var defaults = WriteIni("app-defaults.ini", "[General]\nAppName = FromDefaults\n");
        var section = new GeneralSettingsImpl();
        using (var config = General("changed-defaults.ini", section).AddDefaultsFile(defaults).Build())
        {
            Assert.Equal("FromDefaults", section.AppName);
            section.MaxRetries = 1;
            config.Save();
        }
        IniConfigRegistry.Clear();

        // The administrator changes the defaults file
        WriteIni("app-defaults.ini", "[General]\nAppName = NewDefault\n");

        var reloaded = new GeneralSettingsImpl();
        using var again = General("changed-defaults.ini", reloaded).AddDefaultsFile(defaults).Build();
        Assert.Equal("NewDefault", reloaded.AppName);
        Assert.Equal(1, reloaded.MaxRetries);
    }

    [Fact]
    public void Reload_AppliesAChangedDefaultsFileToInheritedKeys()
    {
        var defaults = WriteIni("reload-defaults.ini", "[General]\nAppName = First\n");
        var section = new GeneralSettingsImpl();
        using var config = General("reload-user.ini", section).AddDefaultsFile(defaults).Build();
        section.MaxRetries = 3;
        config.Save();

        WriteIni("reload-defaults.ini", "[General]\nAppName = Second\n");
        config.Reload();

        Assert.Equal("Second", section.AppName);
        Assert.Equal(3, section.MaxRetries);
    }

    [Fact]
    public void UserValueEqualToTheDefault_StaysAUserValue()
    {
        var defaults = WriteIni("equal-defaults.ini", "[General]\nAppName = Shared\n");
        WriteIni("equal.ini", "[General]\nAppName = Shared\n");
        var section = new GeneralSettingsImpl();
        using (var config = General("equal.ini", section).AddDefaultsFile(defaults).Build())
        {
            Assert.True(section.IsUserValue("AppName"));
            config.Save();
            Assert.Contains("AppName = Shared", ValueLines(Read("equal.ini")));
        }
        IniConfigRegistry.Clear();

        WriteIni("equal-defaults.ini", "[General]\nAppName = Changed\n");
        var reloaded = new GeneralSettingsImpl();
        using var again = General("equal.ini", reloaded).AddDefaultsFile(defaults).Build();
        Assert.Equal("Shared", reloaded.AppName);
    }

    [Fact]
    public void SetRawValueAndMigrationsInAfterLoad_AreUserValues()
    {
        var section = new GeneralSettingsImpl();
        using var config = General("set-raw.ini", section).Build();

        section.SetRawValue("Threshold", "2.5");
        config.Save();

        Assert.Equal(new[] { "Threshold = 2.5" }, ValueLines(Read("set-raw.ini")));
    }

    [Fact]
    public void ResetKey_TurnsAUserValueBackIntoTheInheritedValue()
    {
        var defaults = WriteIni("reset-defaults.ini", "[General]\nAppName = FromDefaults\n");
        WriteIni("reset.ini", "[General]\nAppName = Mine\nMaxRetries = 9\n");
        var section = new GeneralSettingsImpl();
        using var config = General("reset.ini", section).AddDefaultsFile(defaults).Build();
        var changed = new List<string?>();
        section.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        section.ResetKey("AppName");
        section.ResetKey("MaxRetries");

        Assert.Equal("FromDefaults", section.AppName);
        Assert.Equal(42, section.MaxRetries);
        Assert.False(section.IsUserValue("AppName"));
        Assert.Contains(nameof(IGeneralSettings.AppName), changed);
        Assert.True(section.HasChanges);

        config.Save();
        var content = Read("reset.ini");
        Assert.Empty(ValueLines(content));
        Assert.Contains("; AppName = FromDefaults", content);
    }

    [Fact]
    public void ResetToDefaults_ClearsAllUserValues()
    {
        WriteIni("reset-all.ini", "[General]\nAppName = Mine\nMaxRetries = 9\n");
        var section = new GeneralSettingsImpl();
        using var config = General("reset-all.ini", section).Build();

        section.ResetToDefaults();
        Assert.False(section.IsUserValue("AppName"));
        Assert.True(section.HasChanges);
        config.Save();

        Assert.Empty(ValueLines(Read("reset-all.ini")));
    }

    [Fact]
    public void InPlaceChangeOfACollection_IsWrittenAsAUserValue()
    {
        var section = new CollectionSettingsImpl();
        using var config = IniConfigRegistry.ForFile("in-place.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<ICollectionSettings>(section)
            .Build();

        section.StringList!.Add("D");
        section.StringIntDictionary!["z"] = 30;
        section.MarkAsDirty();
        config.Save();

        var lines = ValueLines(Read("in-place.ini"));
        Assert.Contains("StringList = A,B,C,D", lines);
        Assert.Contains("StringIntDictionary.z = 30", lines);
        Assert.DoesNotContain("IntList = 1,2,3", lines);
        Assert.DoesNotContain("StringIntDictionary.x = 10", lines);
        Assert.True(section.IsUserValue("StringList"));
    }

    [Fact]
    public void DictionaryAssignedAsAWhole_IsWrittenCompletely_AndResetKeyRestoresIt()
    {
        var section = new CollectionSettingsImpl();
        using var config = IniConfigRegistry.ForFile("dict.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<ICollectionSettings>(section)
            .Build();

        section.StringIntDictionary = new Dictionary<string, int> { ["a"] = 1, ["b"] = 2 };
        config.Save();
        var lines = ValueLines(Read("dict.ini"));
        Assert.Contains("StringIntDictionary.a = 1", lines);
        Assert.Contains("StringIntDictionary.b = 2", lines);
        Assert.True(section.IsUserValue("StringIntDictionary"));

        section.ResetKey("StringIntDictionary");
        Assert.Equal(new Dictionary<string, int> { ["x"] = 10, ["y"] = 20 }, section.StringIntDictionary);
        config.Save();
        Assert.Empty(ValueLines(Read("dict.ini")));
    }

    // ── Constants ─────────────────────────────────────────────────────────────

    [Fact]
    public void Constant_IsNeverWritten()
    {
        var constants = WriteIni("c-constants.ini", "[ConstantsTest]\nAdminValue = forced\n");
        var section = new ConstantsSettingsImpl();
        using var config = IniConfigRegistry.ForFile("c-user.ini")
            .AddSearchPath(_tempDir)
            .AddConstantsFile(constants)
            .RegisterSection<IConstantsSettings>(section)
            .Build();

        section.UserValue = "chosen";
        config.Save();

        var content = Read("c-user.ini");
        Assert.DoesNotContain("forced", content);
        Assert.DoesNotContain("AdminValue", content);
        Assert.Contains("UserValue = chosen", ValueLines(content));
    }

    [Fact]
    public void Constant_IsNeverWritten_AlsoWithSaveAllValues()
    {
        var constants = WriteIni("ca-constants.ini", "[ConstantsTest]\nAdminValue = forced\n");
        var section = new ConstantsSettingsImpl();
        using var config = IniConfigRegistry.ForFile("ca-user.ini")
            .AddSearchPath(_tempDir)
            .AddConstantsFile(constants)
            .RegisterSection<IConstantsSettings>(section)
            .SaveAllValues()
            .Build();

        config.Save();

        var content = Read("ca-user.ini");
        Assert.DoesNotContain("forced", content);
        Assert.Contains("UserValue = user-default", ValueLines(content));
    }

    [Fact]
    public void RemovedConstant_BringsBackTheUserValue()
    {
        var constants = WriteIni("rc-constants.ini", "[ConstantsTest]\nAdminValue = forced\n");
        WriteIni("rc-user.ini", "[ConstantsTest]\nAdminValue = mine\n");
        var section = new ConstantsSettingsImpl();
        using var config = IniConfigRegistry.ForFile("rc-user.ini")
            .AddSearchPath(_tempDir)
            .AddConstantsFile(constants)
            .RegisterSection<IConstantsSettings>(section)
            .Build();
        Assert.Equal("forced", section.AdminValue);

        section.UserValue = "x";
        config.Save();
        Assert.Contains("AdminValue = mine", ValueLines(Read("rc-user.ini")));

        // The administrator removes the constant
        File.Delete(constants);
        config.Reload();
        Assert.Equal("mine", section.AdminValue);
        Assert.False(section.IsConstant("AdminValue"));
    }

    // ── Value sources ─────────────────────────────────────────────────────────

    [Fact]
    public void ValueSourceValues_AreNotWritten_AndTheUserValueBelowStays()
    {
        WriteIni("vs-user.ini", "[General]\nAppName = Mine\n");
        var source = new DictionaryValueSource();
        source.Set("General", "AppName", "FromSource");
        source.Set("General", "MaxRetries", "7");
        var section = new GeneralSettingsImpl();
        using var config = General("vs-user.ini", section).AddValueSource(source).Build();
        Assert.Equal("FromSource", section.AppName);
        Assert.Equal(7, section.MaxRetries);

        config.Save();

        var lines = ValueLines(Read("vs-user.ini"));
        Assert.Equal(new[] { "AppName = Mine" }, lines);
    }

    // ── SaveAllValues ─────────────────────────────────────────────────────────

    [Fact]
    public void SaveAllValues_WritesEveryValue()
    {
        var section = new GeneralSettingsImpl();
        using var config = General("all.ini", section).SaveAllValues().Build();

        config.Save();

        var lines = ValueLines(Read("all.ini"));
        Assert.Contains("AppName = MyApp", lines);
        Assert.Contains("MaxRetries = 42", lines);
    }

    // ── PreserveFormatting ────────────────────────────────────────────────────

    [Fact]
    public void PreserveFormatting_CommentedLinesDoNotPileUp_AndUncommentingGivesAValue()
    {
        WriteIni("pf.ini", "; my settings\n[General]\nMaxRetries = 5\n");
        var section = new GeneralSettingsImpl();
        using (var config = General("pf.ini", section).PreserveFormatting().Build())
        {
            config.Save();
            var first = Read("pf.ini");
            config.Reload();
            config.Save();
            config.Reload();
            config.Save();
            Assert.Equal(first, Read("pf.ini"));
            Assert.Equal(1, CountOccurrences(first, "; AppName = MyApp"));
            Assert.Equal(1, CountOccurrences(first, "; Application name"));
            Assert.StartsWith("; my settings\n", first);
        }
        IniConfigRegistry.Clear();

        // The user removes the "; " in front of the commented-out line
        var edited = Read("pf.ini").Replace("; AppName = MyApp", "AppName = Edited");
        WriteIni("pf.ini", edited);
        var reloaded = new GeneralSettingsImpl();
        using var again = General("pf.ini", reloaded).PreserveFormatting().Build();
        Assert.Equal("Edited", reloaded.AppName);
        Assert.True(reloaded.IsUserValue("AppName"));
        again.Save();
        var saved = Read("pf.ini");
        Assert.Contains("AppName = Edited", ValueLines(saved));
        Assert.DoesNotContain("; AppName", saved);
    }

    [Fact]
    public void PreserveUnknownSections_CommentedLinesDoNotPileUp()
    {
        WriteIni("pu.ini", "[General]\nMaxRetries = 5\n[Other]\nKey = value\n");
        var section = new GeneralSettingsImpl();
        using var config = General("pu.ini", section).PreserveUnknownSections().Build();
        config.Save();
        var first = Read("pu.ini");
        config.Reload();
        config.Save();

        Assert.Equal(first, Read("pu.ini"));
        Assert.Equal(1, CountOccurrences(first, "; AppName = MyApp"));
        Assert.Contains("Key = value", first);
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        for (var i = text.IndexOf(value, StringComparison.Ordinal); i >= 0; i = text.IndexOf(value, i + 1, StringComparison.Ordinal))
            count++;
        return count;
    }

    // ── Migration of files written with all values ────────────────────────────

    [Fact]
    public void ConvertUnchangedUserValuesToInherited_TurnsValuesEqualToTheDefault()
    {
        var defaults = WriteIni("m-defaults.ini", "[General]\nThreshold = 1.5\n");
        WriteIni("m.ini", "[General]\nAppName = MyApp\nMaxRetries = 7\nEnableLogging = True\nThreshold = 1.5\n");
        var section = new GeneralSettingsImpl();
        using var config = General("m.ini", section).AddDefaultsFile(defaults).Build();
        Assert.True(section.IsUserValue("AppName"));

        var converted = config.ConvertUnchangedUserValuesToInherited();

        Assert.Equal(3, converted);
        Assert.False(section.IsUserValue("AppName"));
        Assert.False(section.IsUserValue("EnableLogging"));
        Assert.False(section.IsUserValue("Threshold"));
        Assert.True(section.IsUserValue("MaxRetries"));
        Assert.Equal("MyApp", section.AppName);
        Assert.Equal(1.5, section.Threshold);
        Assert.True(section.HasChanges);

        config.Save();
        Assert.Equal(new[] { "MaxRetries = 7" }, ValueLines(Read("m.ini")));
    }

    [Fact]
    public void ConvertUnchangedUserValuesToInherited_HandlesCollectionsAndDictionaries()
    {
        WriteIni("mc.ini", "[Collections]\nStringList = A,B,C\nIntList = 4,5\nStringIntDictionary.x = 10\nStringIntDictionary.y = 21\n");
        var section = new CollectionSettingsImpl();
        using var config = IniConfigRegistry.ForFile("mc.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<ICollectionSettings>(section)
            .Build();

        config.ConvertUnchangedUserValuesToInherited();

        Assert.False(section.IsUserValue("StringList"));
        Assert.True(section.IsUserValue("IntList"));
        Assert.False(section.IsUserValue("StringIntDictionary.x"));
        Assert.True(section.IsUserValue("StringIntDictionary.y"));
        Assert.Equal(new List<int> { 4, 5 }, section.IntList);
        Assert.Equal(new Dictionary<string, int> { ["x"] = 10, ["y"] = 21 }, section.StringIntDictionary);
    }

    [Fact]
    public void ConvertUnchangedValuesToInherited_RunsOnceThroughTheMetadataFlag()
    {
        WriteIni("once.ini", "[General]\nAppName = MyApp\nMaxRetries = 7\n");
        var section = new GeneralSettingsImpl();
        using (var config = General("once.ini", section).EnableMetadata("1.0").ConvertUnchangedValuesToInherited().Build())
        {
            Assert.False(section.IsUserValue("AppName"));
            Assert.True(section.IsUserValue("MaxRetries"));
            config.Save();
            Assert.Contains("UserValuesOnly = True", Read("once.ini"));
        }
        IniConfigRegistry.Clear();

        // The user now chooses the default on purpose: it must stay a user value on the next load
        WriteIni("once.ini", Read("once.ini").Replace("; AppName = MyApp", "AppName = MyApp"));
        var reloaded = new GeneralSettingsImpl();
        using var again = General("once.ini", reloaded).EnableMetadata("1.0").ConvertUnchangedValuesToInherited().Build();
        Assert.True(again.Metadata!.UserValuesOnly);
        Assert.True(reloaded.IsUserValue("AppName"));
    }

    [Fact]
    public void ConvertUnchangedValuesToInherited_WithoutMetadata_Throws()
    {
        Assert.Throws<InvalidOperationException>(() =>
            General("no-meta.ini", new GeneralSettingsImpl()).ConvertUnchangedValuesToInherited().Build());
    }

    [Fact]
    public void ConvertUnchangedValuesToInherited_CustomCondition_IsAskedWithTheFileMetadata()
    {
        WriteIni("cond.ini", "[General]\nAppName = MyApp\n");
        var section = new GeneralSettingsImpl();
        IniMetadata? seen = null;
        using var config = General("cond.ini", section)
            .ConvertUnchangedValuesToInherited(m => { seen = m; return true; })
            .Build();

        Assert.Null(seen);
        Assert.False(section.IsUserValue("AppName"));
    }

    private sealed class DictionaryValueSource : IValueSource
    {
        private readonly Dictionary<string, string?> _values = new(StringComparer.OrdinalIgnoreCase);

        public void Set(string section, string key, string? value) => _values[section + "/" + key] = value;

        public bool TryGetValue(string sectionName, string key, out string? value)
            => _values.TryGetValue(sectionName + "/" + key, out value);

        public event EventHandler<ValueChangedEventArgs>? ValueChanged
        {
            add { }
            remove { }
        }
    }
}
