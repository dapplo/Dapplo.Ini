// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Dapplo.Ini.Configuration;
using Dapplo.Ini.Interfaces;
using Microsoft.Win32;

namespace Dapplo.Ini.WindowsPolicies.Tests;

/// <summary>Mapping per type, the layers of the four sources and the change notification, on a fake registry.</summary>
[Collection("IniConfigRegistry")]
public sealed class PolicySourceTests : IDisposable
{
    private const string Root = @"SOFTWARE\Policies\Dapplo\PolicyTest";
    private const string Mandatory = Root + @"\Core";
    private const string Recommended = Root + @"\Recommended\Core";

    private readonly string _tempDir;
    private readonly FakeRegistry _hklm = new();
    private readonly FakeRegistry _hkcu = new();
    private readonly Dictionary<RegistryHive, FakeWatcher> _watchers = new();
    private readonly RecordingListener _listener = new();

    public PolicySourceTests()
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

    private WindowsPolicyOptions Options(bool watch = false) => new()
    {
        RegistryFactory = hive => hive == RegistryHive.LocalMachine ? _hklm : _hkcu,
        WatchForChanges = watch,
        WatcherFactory = hive => _watchers[hive] = new FakeWatcher(),
        ChangeDebounce = TimeSpan.FromMilliseconds(30)
    };

    private IniConfig Build(CoreSettingsImpl section, string? userFile = null, bool watch = false)
    {
        var fileName = Guid.NewGuid().ToString("N") + ".ini";
        if (userFile != null)
            File.WriteAllText(Path.Combine(_tempDir, fileName), userFile);
        return IniConfigRegistry.ForFile(fileName)
            .AddSearchPath(_tempDir)
            .RegisterSection<ICoreSettings>(section)
            .AddListener(_listener)
            .AddWindowsPolicies("Dapplo", "PolicyTest", Options(watch))
            .ValueSourceChangeDebounce(TimeSpan.FromMilliseconds(30))
            .Build();
    }

    // ── Mapping per type ──────────────────────────────────────────────────────

    [Fact]
    public void Bool_FromDword()
    {
        _hklm.Set(Mandatory, "CheckForUpdates", 0, RegistryValueKind.DWord);
        var section = new CoreSettingsImpl();
        using var config = Build(section);
        Assert.False(section.CheckForUpdates);
        Assert.True(section.IsConstant("CheckForUpdates"));
    }

    [Fact]
    public void Bool_InvalidValue_IsReportedAndSkipped()
    {
        _hklm.Set(Mandatory, "CheckForUpdates", 2, RegistryValueKind.DWord)
             .Set(Mandatory, "AppName", 5, RegistryValueKind.DWord)
             .Set(Mandatory, "MaxRetries", 7, RegistryValueKind.DWord);
        var section = new CoreSettingsImpl();
        using var config = Build(section);

        Assert.True(section.CheckForUpdates);   // the default
        Assert.False(section.IsConstant("CheckForUpdates"));
        Assert.Equal("MyApp", section.AppName);
        Assert.Equal(7, section.MaxRetries);    // the load continued
        Assert.Contains(_listener.Failed, f => f.Key == "CheckForUpdates" && f.Raw == "2");
        Assert.Contains(_listener.Failed, f => f.Key == "AppName");
    }

    [Fact]
    public void Integers_FromDwordAndQword()
    {
        _hklm.Set(Mandatory, "MaxRetries", 3, RegistryValueKind.DWord)
             .Set(Mandatory, "Flags", unchecked((int)0xFFFFFFFF), RegistryValueKind.DWord)
             .Set(Mandatory, "BigNumber", 5_000_000_000L, RegistryValueKind.QWord)
             .Set(Mandatory, "Offset", "-3", RegistryValueKind.String);
        var section = new CoreSettingsImpl();
        using var config = Build(section);

        Assert.Equal(3, section.MaxRetries);
        Assert.Equal(uint.MaxValue, section.Flags);
        Assert.Equal(5_000_000_000L, section.BigNumber);
        Assert.Equal(-3, section.Offset);
    }

    [Fact]
    public void Integer_OutOfRange_IsReported()
    {
        _hklm.Set(Mandatory, "BigNumber", 1, RegistryValueKind.DWord)
             .Set(Mandatory, "MaxRetries", 5_000_000_000L, RegistryValueKind.QWord);
        var section = new CoreSettingsImpl();
        using var config = Build(section);
        Assert.Equal(1, section.BigNumber);
        Assert.Equal(5, section.MaxRetries);
        Assert.Contains(_listener.Failed, f => f.Key == "MaxRetries");
    }

    [Fact]
    public void Strings_AreNeverExpanded()
    {
        _hklm.Set(Mandatory, "AppName", "Policy app", RegistryValueKind.String)
             .Set(Mandatory, "OutputPath", @"%USERPROFILE%\shots", RegistryValueKind.ExpandString);
        var section = new CoreSettingsImpl();
        using var config = Build(section);
        Assert.Equal("Policy app", section.AppName);
        Assert.Equal(@"%USERPROFILE%\shots", section.OutputPath);
    }

    [Fact]
    public void Enum_ByName_AndAnUnknownNameIsReported()
    {
        _hklm.Set(Mandatory, "OutputFileFormat", "jpg", RegistryValueKind.String);
        var section = new CoreSettingsImpl();
        using (Build(section))
            Assert.Equal(OutputFormat.Jpg, section.OutputFileFormat);

        IniConfigRegistry.Clear();
        _hklm.Set(Mandatory, "OutputFileFormat", "Gif", RegistryValueKind.String);
        var second = new CoreSettingsImpl();
        using (Build(second))
            Assert.Equal(OutputFormat.Png, second.OutputFileFormat);
        Assert.Contains(_listener.Failed, f => f.Key == "OutputFileFormat" && f.Raw == "Gif");
    }

    [Fact]
    public void OtherTypes_FromTheIniText()
    {
        _hklm.Set(Mandatory, "Threshold", "2.75", RegistryValueKind.String);
        var section = new CoreSettingsImpl();
        using var config = Build(section);
        Assert.Equal(2.75, section.Threshold);
    }

    [Fact]
    public void List_FromSubKeyWithNumberedValues_InNumericOrder()
    {
        _hklm.Set(Mandatory + @"\Plugins", "2", "second", RegistryValueKind.String)
             .Set(Mandatory + @"\Plugins", "10", "tenth, with comma", RegistryValueKind.String)
             .Set(Mandatory + @"\Plugins", "1", "first", RegistryValueKind.String)
             .Set(Mandatory + @"\Plugins", "note", "ignored", RegistryValueKind.String)
             .Set(Mandatory + @"\Ports", "1", "80", RegistryValueKind.String)
             .Set(Mandatory + @"\Ports", "2", 443, RegistryValueKind.DWord);
        var section = new CoreSettingsImpl();
        using var config = Build(section);
        Assert.Equal(new[] { "first", "second", "tenth, with comma" }, section.Plugins);
        Assert.Equal(new[] { 80, 443 }, section.Ports);
        Assert.True(section.IsConstant("Plugins"));
    }

    [Fact]
    public void List_FromMultiString()
    {
        _hklm.Set(Mandatory, "Plugins", new[] { "a", "b c" }, RegistryValueKind.MultiString);
        var section = new CoreSettingsImpl();
        using var config = Build(section);
        Assert.Equal(new[] { "a", "b c" }, section.Plugins);
    }

    [Fact]
    public void Dictionary_FromSubKey()
    {
        _hklm.Set(Mandatory + @"\Headers", "Accept", "text/html", RegistryValueKind.String)
             .Set(Mandatory + @"\Headers", "X-Mode", "a=b,c", RegistryValueKind.String);
        var section = new CoreSettingsImpl();
        using var config = Build(section);
        Assert.NotNull(section.Headers);
        Assert.Equal(2, section.Headers!.Count);
        Assert.Equal("text/html", section.Headers["Accept"]);
        Assert.Equal("a=b,c", section.Headers["X-Mode"]);
        Assert.True(section.IsConstant("Headers"));
    }

    [Fact]
    public void NotAdministrableAndRuntimeOnlyKeys_AreNotRead()
    {
        _hklm.Set(Mandatory, "LastUsedFolder", "forced", RegistryValueKind.String)
             .Set(Mandatory, "SessionCount", 9, RegistryValueKind.DWord);
        var section = new CoreSettingsImpl();
        using var config = Build(section);
        Assert.Equal("", section.LastUsedFolder ?? "");
        Assert.Equal(0, section.SessionCount);
    }

    // ── Layers ────────────────────────────────────────────────────────────────

    [Fact]
    public void Recommended_IsADefaultTheUserCanChange_AndMandatoryIsConstant()
    {
        _hklm.Set(Recommended, "AppName", "recommended", RegistryValueKind.String)
             .Set(Recommended, "MaxRetries", 8, RegistryValueKind.DWord)
             .Set(Mandatory, "OutputFileFormat", "Bmp", RegistryValueKind.String);
        var section = new CoreSettingsImpl();
        using var config = Build(section, "[Core]\nAppName = mine\nOutputFileFormat = Jpg\n");

        Assert.Equal("mine", section.AppName);              // the user's value wins over a recommendation
        Assert.Equal(8, section.MaxRetries);                // the recommendation wins over the compiled default
        Assert.False(section.IsConstant("MaxRetries"));
        Assert.False(section.IsUserValue("MaxRetries"));
        Assert.Equal(OutputFormat.Bmp, section.OutputFileFormat);  // mandatory wins over the user
        Assert.True(section.IsConstant("OutputFileFormat"));
        Assert.Throws<AccessViolationException>(() => section.OutputFileFormat = OutputFormat.Png);

        var origin = section.GetValueOrigin("OutputFileFormat");
        Assert.Equal(ValueOriginKind.ValueSource, origin.Kind);
        Assert.Equal(ValueSourceLayer.Constant, origin.Layer);
        Assert.Equal("Windows policy (HKLM, mandatory)", origin.SourceName);
        Assert.Equal("Windows policy (HKLM, recommended)", section.GetValueOrigin("MaxRetries").SourceName);
    }

    [Fact]
    public void Machine_WinsOverUser_InBothLayers()
    {
        _hkcu.Set(Recommended, "MaxRetries", 2, RegistryValueKind.DWord)
             .Set(Recommended, "AppName", "user-recommended", RegistryValueKind.String)
             .Set(Mandatory, "OutputFileFormat", "Jpg", RegistryValueKind.String)
             .Set(Mandatory, "Threshold", "4", RegistryValueKind.String);
        _hklm.Set(Recommended, "MaxRetries", 3, RegistryValueKind.DWord)
             .Set(Mandatory, "OutputFileFormat", "Bmp", RegistryValueKind.String);
        var section = new CoreSettingsImpl();
        using var config = Build(section);

        Assert.Equal(3, section.MaxRetries);
        Assert.Equal("user-recommended", section.AppName);
        Assert.Equal(OutputFormat.Bmp, section.OutputFileFormat);
        Assert.Equal(4, section.Threshold);
        Assert.Equal("Windows policy (HKCU, mandatory)", section.GetValueOrigin("Threshold").SourceName);
    }

    [Fact]
    public void IgnoreConstants_KeyIsNotForced()
    {
        _hklm.Set(Mandatory, "Nickname", "forced", RegistryValueKind.String);
        var section = new CoreSettingsImpl();
        using var config = Build(section, "[Core]\nNickname = me\n");
        Assert.Equal("me", section.Nickname);
        Assert.False(section.IsConstant("Nickname"));
    }

    [Fact]
    public void MandatoryPolicies_AreNotWrittenToTheUserFile()
    {
        _hklm.Set(Mandatory, "AppName", "forced", RegistryValueKind.String)
             .Set(Recommended, "MaxRetries", 9, RegistryValueKind.DWord);
        var section = new CoreSettingsImpl();
        var fileName = Guid.NewGuid().ToString("N") + ".ini";
        File.WriteAllText(Path.Combine(_tempDir, fileName), "[Core]\nAppName = mine\n");
        using (var config = IniConfigRegistry.ForFile(fileName).AddSearchPath(_tempDir).RegisterSection<ICoreSettings>(section)
                   .AddWindowsPolicies("Dapplo", "PolicyTest", Options()).Build())
        {
            section.Threshold = 7;
            config.Save();
        }
        var lines = File.ReadAllLines(Path.Combine(_tempDir, fileName)).Select(l => l.Trim())
            .Where(l => l.Length > 0 && l[0] != ';' && l[0] != '[').ToArray();
        Assert.Contains("AppName = mine", lines);   // the user's own value below the policy stays
        Assert.Contains("Threshold = 7", lines);
        Assert.DoesNotContain(lines, l => l.StartsWith("MaxRetries", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, l => l.Contains("forced"));
    }

    [Fact]
    public void Options_SelectHivesAndKinds_AndTheRootPath()
    {
        var sources = WindowsPolicyBuilderExtensions.CreateSources("Dapplo", "PolicyTest",
            new WindowsPolicyOptions { IncludeCurrentUser = false, IncludeRecommended = false, RootPath = @"\SOFTWARE\Policies\Other\", WatchForChanges = false });
        var single = Assert.Single(sources);
        Assert.Equal(RegistryHive.LocalMachine, single.Source.Hive);
        Assert.Equal(PolicyKind.Mandatory, single.Source.Kind);
        Assert.Equal(ValueSourceLayer.Constant, single.Layer);
        Assert.Equal(@"SOFTWARE\Policies\Other\General", single.Source.GetSectionPath("General"));

        var all = WindowsPolicyBuilderExtensions.CreateSources("Dapplo", "PolicyTest", new WindowsPolicyOptions { WatchForChanges = false });
        Assert.Equal(new[]
        {
            "Windows policy (HKCU, recommended)", "Windows policy (HKLM, recommended)",
            "Windows policy (HKCU, mandatory)", "Windows policy (HKLM, mandatory)"
        }, all.Select(s => s.Source.Name));
        Assert.Equal(new[] { ValueSourceLayer.Default, ValueSourceLayer.Default, ValueSourceLayer.Constant, ValueSourceLayer.Constant },
            all.Select(s => s.Layer));
        Assert.Equal(@"SOFTWARE\Policies\Dapplo\PolicyTest\Recommended\Core", all[0].Source.GetSectionPath("Core"));
    }

    // ── Change notification ───────────────────────────────────────────────────

    [Fact]
    public async Task Watcher_ABurstOfChangesRaisesValueChangedOnce()
    {
        var watcher = new FakeWatcher();
        using var source = new WindowsPolicySource(RegistryHive.LocalMachine, PolicyKind.Mandatory, Root, _hklm, () => watcher,
            TimeSpan.FromMilliseconds(50));
        var raised = 0;
        Assert.False(source.IsWatching);
        EventHandler<ValueChangedEventArgs> handler = (_, e) =>
        {
            Assert.Null(e.SectionName);
            Interlocked.Increment(ref raised);
        };
        source.ValueChanged += handler;
        Assert.True(source.IsWatching);

        for (var i = 0; i < 20; i++)
            watcher.Fire();
        await WaitFor(() => Volatile.Read(ref raised) > 0);
        await Task.Delay(200);
        Assert.Equal(1, Volatile.Read(ref raised));

        watcher.Fire();
        await WaitFor(() => Volatile.Read(ref raised) > 1);
        Assert.Equal(2, Volatile.Read(ref raised));

        source.ValueChanged -= handler;
        Assert.False(source.IsWatching);
        Assert.True(watcher.IsDisposed);
    }

    [Fact]
    public async Task Watcher_AChangedPolicyReachesTheSection_AndDisposeStopsWatching()
    {
        var section = new CoreSettingsImpl();
        var config = Build(section, watch: true);
        Assert.Equal(5, section.MaxRetries);
        Assert.Equal(2, _watchers.Count);

        _hklm.Set(Mandatory, "MaxRetries", 9, RegistryValueKind.DWord);
        _watchers[RegistryHive.LocalMachine].Fire();
        await WaitFor(() => section.MaxRetries == 9);
        Assert.True(section.IsConstant("MaxRetries"));

        _hklm.DeleteKey(Mandatory);
        _watchers[RegistryHive.LocalMachine].Fire();
        await WaitFor(() => section.MaxRetries == 5);
        Assert.False(section.IsConstant("MaxRetries"));

        config.Dispose();
        Assert.All(_watchers.Values, w => Assert.True(w.IsDisposed));
    }

    private static async Task WaitFor(Func<bool> condition)
    {
        var until = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            if (DateTime.UtcNow > until)
                throw new TimeoutException("The condition was not met in time.");
            await Task.Delay(10);
        }
    }

    private sealed class RecordingListener : IniConfigListenerBase
    {
        public List<(string Section, string Key, string? Raw)> Failed { get; } = new();

        public override void OnValueConversionFailed(string sectionName, string key, string? rawValue, Exception exception)
        {
            lock (Failed)
                Failed.Add((sectionName, key, rawValue));
        }
    }
}
