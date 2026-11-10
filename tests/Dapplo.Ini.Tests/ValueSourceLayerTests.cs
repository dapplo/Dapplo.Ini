// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Threading;
using System.Threading.Tasks;
using Dapplo.Ini.Configuration;
using Dapplo.Ini.Interfaces;

namespace Dapplo.Ini.Tests;

/// <summary>Value source layers (Default, Override, Constant), value origins and the reload on <c>ValueChanged</c>.</summary>
[Collection("IniConfigRegistry")]
public sealed class ValueSourceLayerTests : IDisposable
{
    private readonly string _tempDir;

    public ValueSourceLayerTests()
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
        => content.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0 && l[0] != ';' && l[0] != '[').ToArray();

    private IniConfigBuilder General(string fileName, GeneralSettingsImpl section)
        => IniConfigRegistry.ForFile(fileName).AddSearchPath(_tempDir).RegisterSection<IGeneralSettings>(section);

    // ── Default layer ─────────────────────────────────────────────────────────

    [Fact]
    public void DefaultLayer_IsBelowTheUserFile_AndNotSaved()
    {
        WriteIni("default.ini", "[General]\nAppName = Mine\n");
        var source = new TestSource("defaults").With("General", "AppName", "FromSource").With("General", "MaxRetries", "3");
        var section = new GeneralSettingsImpl();
        using var config = General("default.ini", section).AddValueSource(source, ValueSourceLayer.Default).Build();

        Assert.Equal("Mine", section.AppName);
        Assert.Equal(3, section.MaxRetries);
        Assert.False(section.IsUserValue("MaxRetries"));
        Assert.False(section.IsConstant("MaxRetries"));

        config.Save();
        var content = Read("default.ini");
        Assert.Equal(new[] { "AppName = Mine" }, ValueLines(content));
        Assert.Contains("; MaxRetries = 3", content);
    }

    [Fact]
    public void DefaultLayer_IsAboveTheDefaultsFiles_AndAChangeArrivesForKeysTheUserDidNotSet()
    {
        var defaults = WriteIni("dl-defaults.ini", "[General]\nMaxRetries = 1\nThreshold = 9.5\n");
        var source = new TestSource().With("General", "MaxRetries", "2");
        var section = new GeneralSettingsImpl();
        using var config = General("dl.ini", section).AddDefaultsFile(defaults).AddValueSource(source, ValueSourceLayer.Default).Build();
        Assert.Equal(2, section.MaxRetries);
        Assert.Equal(9.5, section.Threshold);
        config.Save();

        source.With("General", "MaxRetries", "5");
        config.Reload();
        Assert.Equal(5, section.MaxRetries);
    }

    [Fact]
    public void DefaultLayer_RespectsIgnoreDefaults()
    {
        var source = new TestSource().With("IgnoreDefaultsSection", "Value", "from-source");
        var section = new IgnoreDefaultsSectionSettingsImpl();
        using var config = IniConfigRegistry.ForFile("dl-ignore.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<IIgnoreDefaultsSectionSettings>(section)
            .AddValueSource(source, ValueSourceLayer.Default)
            .Build();

        Assert.NotEqual("from-source", section.Value);
    }

    [Fact]
    public void Layer_LaterSourceWinsInsideALayer()
    {
        var first = new TestSource("first").With("General", "AppName", "first");
        var second = new TestSource("second").With("General", "AppName", "second");
        var section = new GeneralSettingsImpl();
        using var config = General("order.ini", section)
            .AddValueSource(first, ValueSourceLayer.Default)
            .AddValueSource(second, ValueSourceLayer.Default)
            .Build();

        Assert.Equal("second", section.AppName);
        Assert.Same(second, section.GetValueOrigin("AppName").Source);
    }

    // ── Override layer ────────────────────────────────────────────────────────

    [Fact]
    public void OverrideLayer_IsTheDefault_AndWinsOverTheUserFileButNotConstants()
    {
        WriteIni("ov.ini", "[ConstantsTest]\nUserValue = mine\n");
        var constants = WriteIni("ov-constants.ini", "[ConstantsTest]\nAdminValue = forced\n");
        var source = new TestSource().With("ConstantsTest", "UserValue", "override").With("ConstantsTest", "AdminValue", "override");
        var listener = new RecordingListener();
        var section = new ConstantsSettingsImpl();
        using var config = IniConfigRegistry.ForFile("ov.ini")
            .AddSearchPath(_tempDir)
            .AddConstantsFile(constants)
            .RegisterSection<IConstantsSettings>(section)
            .AddValueSource(source)
            .AddListener(listener)
            .Build();

        Assert.Equal("override", section.UserValue);
        Assert.Equal("forced", section.AdminValue);
        Assert.Contains(("ConstantsTest", "AdminValue", "override"), listener.Ignored);
        Assert.Equal(ValueSourceLayer.Override, section.GetValueOrigin("UserValue").Layer);

        config.Save();
        Assert.Equal(new[] { "UserValue = mine" }, ValueLines(Read("ov.ini")));
    }

    // ── Constant layer ────────────────────────────────────────────────────────

    [Fact]
    public void ConstantLayer_LocksTheKey_AndIsNeverSaved()
    {
        WriteIni("cl.ini", "[General]\nAppName = Mine\n");
        var source = new TestSource("policy").With("General", "AppName", "Locked").With("General", "MaxRetries", "1");
        var section = new GeneralSettingsImpl();
        using var config = General("cl.ini", section).AddValueSource(source, ValueSourceLayer.Constant).Build();

        Assert.Equal("Locked", section.AppName);
        Assert.True(section.IsConstant("AppName"));
        Assert.Throws<AccessViolationException>(() => section.AppName = "x");
        Assert.Throws<AccessViolationException>(() => section.SetRawValue("MaxRetries", "4"));

        section.Threshold = 1.25;
        config.Save();
        var content = Read("cl.ini");
        Assert.Equal(new[] { "AppName = Mine", "Threshold = 1.25" }, ValueLines(content));
        Assert.DoesNotContain("Locked", content);
        Assert.DoesNotContain("MaxRetries = 1", content);
    }

    [Fact]
    public void ConstantLayer_WinsOverTheConstantsFile_AndReportsTheReplacedValue()
    {
        var constants = WriteIni("cc-constants.ini", "[ConstantsTest]\nAdminValue = from-file\n");
        var source = new TestSource().With("ConstantsTest", "AdminValue", "from-source");
        var listener = new RecordingListener();
        var section = new ConstantsSettingsImpl();
        using var config = IniConfigRegistry.ForFile("cc.ini")
            .AddSearchPath(_tempDir)
            .AddConstantsFile(constants)
            .RegisterSection<IConstantsSettings>(section)
            .AddValueSource(source, ValueSourceLayer.Constant)
            .AddListener(listener)
            .Build();

        Assert.Equal("from-source", section.AdminValue);
        Assert.True(section.IsConstant("AdminValue"));
        Assert.Contains(("ConstantsTest", "AdminValue", "from-file"), listener.Ignored);
        Assert.Equal(ValueOriginKind.ValueSource, section.GetValueOrigin("AdminValue").Kind);
    }

    [Fact]
    public void ConstantLayer_RespectsIgnoreConstants()
    {
        var source = new TestSource().With("IgnoreConstantsSection", "Value", "locked");
        var section = new IgnoreConstantsSectionSettingsImpl();
        using var config = IniConfigRegistry.ForFile("cl-ignore.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<IIgnoreConstantsSectionSettings>(section)
            .AddValueSource(source, ValueSourceLayer.Constant)
            .Build();

        Assert.Equal("compiled-default", section.Value);
        Assert.False(section.IsConstant("Value"));
    }

    [Fact]
    public void ConstantLayer_BlocksOverrideSources()
    {
        var constant = new TestSource().With("General", "AppName", "constant");
        var overriding = new TestSource().With("General", "AppName", "override");
        var section = new GeneralSettingsImpl();
        using var config = General("cl-ov.ini", section)
            .AddValueSource(overriding)
            .AddValueSource(constant, ValueSourceLayer.Constant)
            .Build();

        Assert.Equal("constant", section.AppName);
    }

    [Fact]
    public async Task AsyncSources_AreAppliedInTheirLayer()
    {
        WriteIni("async.ini", "[General]\nAppName = Mine\n");
        var defaults = new TestAsyncSource().With("General", "AppName", "async-default").With("General", "MaxRetries", "11");
        var constants = new TestAsyncSource().With("General", "Threshold", "0.5");
        var section = new GeneralSettingsImpl();
        using var config = await General("async.ini", section)
            .AddValueSource(defaults, ValueSourceLayer.Default)
            .AddValueSource(constants, ValueSourceLayer.Constant)
            .BuildAsync();

        Assert.Equal("Mine", section.AppName);
        Assert.Equal(11, section.MaxRetries);
        Assert.Equal(0.5, section.Threshold);
        Assert.True(section.IsConstant("Threshold"));
    }

    // ── Value origin ──────────────────────────────────────────────────────────

    [Fact]
    public void GetValueOrigin_ReportsEveryLayer()
    {
        var defaults = WriteIni("o-defaults.ini", "[General]\nMaxRetries = 1\n");
        var user = WriteIni("o.ini", "[General]\nThreshold = 2.5\n");
        var constants = WriteIni("o-constants.ini", "[General]\nEnableLogging = False\n");
        var source = new TestSource("my source").With("General", "AppName", "x");
        var section = new GeneralSettingsImpl();
        using var config = General("o.ini", section)
            .AddDefaultsFile(defaults)
            .AddConstantsFile(constants)
            .AddValueSource(source)
            .Build();

        var compiled = new GeneralSettingsImpl();
        Assert.Equal(ValueOriginKind.CompiledDefault, compiled.GetValueOrigin("AppName").Kind);
        Assert.Equal(ValueOriginKind.DefaultsFile, section.GetValueOrigin("MaxRetries").Kind);
        Assert.Equal(defaults, section.GetValueOrigin("MaxRetries").FilePath);
        Assert.Equal(ValueOriginKind.UserFile, section.GetValueOrigin("Threshold").Kind);
        Assert.Equal(user, section.GetValueOrigin("Threshold").FilePath);
        Assert.Equal(ValueOriginKind.ConstantsFile, section.GetValueOrigin("EnableLogging").Kind);
        var origin = section.GetValueOrigin("AppName");
        Assert.Equal(ValueOriginKind.ValueSource, origin.Kind);
        Assert.Equal("my source", origin.SourceName);
        Assert.Equal("ValueSource \"my source\" (Override)", origin.ToString());

        section.Threshold = 3;
        Assert.Equal(ValueOriginKind.UserFile, section.GetValueOrigin("Threshold").Kind);
        Assert.Null(section.GetValueOrigin("Threshold").FilePath);
    }

    // ── Reload on ValueChanged ────────────────────────────────────────────────

    [Fact]
    public async Task ValueChanged_ReloadsTheSection_OncePerBurst_AndKeepsUnsavedValues()
    {
        var source = new TestSource().With("General", "AppName", "first");
        var section = new GeneralSettingsImpl();
        using var config = General("changed.ini", section)
            .AddValueSource(source, ValueSourceLayer.Constant)
            .ValueSourceChangeDebounce(TimeSpan.FromMilliseconds(100))
            .Build();
        var reloaded = 0;
        config.Reloaded += (_, _) => Interlocked.Increment(ref reloaded);
        var changed = new List<string?>();
        section.PropertyChanged += (_, e) => { lock (changed) changed.Add(e.PropertyName); };
        section.MaxRetries = 77;   // not saved yet

        source.With("General", "AppName", "second");
        for (var i = 0; i < 5; i++)
            source.RaiseChanged("General", "AppName");

        await WaitUntil(() => Volatile.Read(ref reloaded) > 0);
        await Task.Delay(300);

        Assert.Equal("second", section.AppName);
        Assert.Equal(1, Volatile.Read(ref reloaded));
        Assert.Equal(1, config.SourceChangeReloads);
        lock (changed) Assert.Contains(nameof(IGeneralSettings.AppName), changed);
        Assert.Equal(77, section.MaxRetries);
        Assert.True(section.HasChanges);
    }

    [Fact]
    public async Task ValueChanged_ForAnotherSection_DoesNotReloadThisOne_WildcardReloadsAll()
    {
        var source = new TestSource().With("General", "AppName", "first");
        var section = new GeneralSettingsImpl();
        using var config = General("wild.ini", section)
            .AddValueSource(source, ValueSourceLayer.Default)
            .ValueSourceChangeDebounce(TimeSpan.FromMilliseconds(50))
            .Build();

        source.With("General", "AppName", "second");
        source.RaiseChanged("Other", null);
        await Task.Delay(300);
        Assert.Equal("first", section.AppName);
        Assert.Equal(0, config.SourceChangeReloads);

        source.RaiseChanged(null, null);
        await WaitUntil(() => config.SourceChangeReloads > 0);
        await WaitUntil(() => section.AppName == "second");
    }

    [Fact]
    public async Task Dispose_UnsubscribesFromTheSources()
    {
        var source = new TestSource().With("General", "AppName", "first");
        var config = General("dispose.ini", new GeneralSettingsImpl()).AddValueSource(source).Build();
        Assert.Equal(1, source.Subscribers);
        config.Dispose();
        Assert.Equal(0, source.Subscribers);
        source.RaiseChanged(null, null);
        await Task.Delay(50);
    }

    // ── Late sections ─────────────────────────────────────────────────────────

    [Fact]
    public void LateSection_GetsAllLayers()
    {
        var defaults = new TestSource().With("ConstantsTest", "UserValue", "from-default-source");
        var constants = new TestSource().With("ConstantsTest", "AdminValue", "locked");
        using var config = General("late.ini", new GeneralSettingsImpl())
            .AllowLateSectionRegistration()
            .AddValueSource(defaults, ValueSourceLayer.Default)
            .AddValueSource(constants, ValueSourceLayer.Constant)
            .Build();

        var late = config.AddSection<IConstantsSettings>(new ConstantsSettingsImpl());

        Assert.Equal("from-default-source", late.UserValue);
        Assert.False(late.IsUserValue("UserValue"));
        Assert.Equal("locked", late.AdminValue);
        Assert.True(late.IsConstant("AdminValue"));
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static async Task WaitUntil(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++)
            await Task.Delay(25);
        Assert.True(condition());
    }

    private sealed class TestSource : IValueSource, INamedValueSource
    {
        private readonly Dictionary<string, string?> _values = new(StringComparer.OrdinalIgnoreCase);
        private EventHandler<ValueChangedEventArgs>? _changed;

        public TestSource(string name = "test") => Name = name;

        public string Name { get; }

        public int Subscribers => _changed?.GetInvocationList().Length ?? 0;

        public TestSource With(string section, string key, string? value)
        {
            lock (_values) _values[section + "/" + key] = value;
            return this;
        }

        public void RaiseChanged(string? section, string? key) => _changed?.Invoke(this, new ValueChangedEventArgs(section, key));

        public bool TryGetValue(string sectionName, string key, out string? value)
        {
            lock (_values) return _values.TryGetValue(sectionName + "/" + key, out value);
        }

        public event EventHandler<ValueChangedEventArgs>? ValueChanged
        {
            add => _changed += value;
            remove => _changed -= value;
        }
    }

    private sealed class TestAsyncSource : IValueSourceAsync
    {
        private readonly Dictionary<string, string?> _values = new(StringComparer.OrdinalIgnoreCase);

        public TestAsyncSource With(string section, string key, string? value)
        {
            _values[section + "/" + key] = value;
            return this;
        }

        public async Task<(bool Found, string? Value)> TryGetValueAsync(string sectionName, string key, CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            return _values.TryGetValue(sectionName + "/" + key, out var value) ? (true, value) : (false, null);
        }

        public event EventHandler<ValueChangedEventArgs>? ValueChanged
        {
            add { }
            remove { }
        }
    }

    private sealed class RecordingListener : IniConfigListenerBase
    {
        public List<(string, string, string?)> Ignored { get; } = new();

        public override void OnValueSourceIgnored(string sectionName, string key, string? ignoredValue)
            => Ignored.Add((sectionName, key, ignoredValue));
    }
}
