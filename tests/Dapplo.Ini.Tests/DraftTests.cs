// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Threading;
using System.Threading.Tasks;
using Dapplo.Ini.Attributes;
using Dapplo.Ini.Configuration;
using Dapplo.Ini.Interfaces;

namespace Dapplo.Ini.Tests;

/// <summary>Section a settings dialog edits through a draft.</summary>
[IniSection("Draft")]
public interface IDraftSettings : IIniSection, INotifyPropertyChanged, INotifyPropertyChanging
{
    [IniValue(DefaultValue = "initial")]
    string? Name { get; set; }

    /// <summary>Runtime state the application changes while the dialog is open (e.g. a file counter).</summary>
    [IniValue(DefaultValue = "0")]
    int Counter { get; set; }

    [IniValue(DefaultValue = "True")]
    bool Enabled { get; set; }

    [Range(1, 10)]
    [IniValue(DefaultValue = "5")]
    int Level { get; set; }

    [IniValue(DefaultValue = "a,b")]
    List<string>? Tags { get; set; }

    [IniValue(DefaultValue = "1,2")]
    int[]? Numbers { get; set; }

    Dictionary<string, int>? Limits { get; set; }

    [IniValue(DefaultValue = "runtime", RuntimeOnly = true)]
    string? Session { get; set; }
}

/// <summary>Transactional section, to check that applying a draft switches the values at once.</summary>
[IniSection("DraftTx")]
public interface IDraftTxSettings : IIniSection, ITransactional, INotifyPropertyChanged
{
    [IniValue(DefaultValue = "a1", Transactional = true)]
    string? First { get; set; }

    [IniValue(DefaultValue = "b1", Transactional = true)]
    string? Second { get; set; }
}

/// <summary>Other property shapes a draft must copy: interface-typed collections, read-only and ignored properties.</summary>
[IniSection("DraftShapes")]
public interface IDraftShapesSettings : IIniSection
{
    [IniValue(DefaultValue = "1,2")]
    IList<int>? Ints { get; set; }

    [IniValue(DefaultValue = "x,y")]
    IReadOnlyList<string>? ReadOnlyList { get; set; }

    IDictionary<string, string>? Map { get; set; }

    [IniValue(DefaultValue = "fixed")]
    string? ReadOnlyValue { get; }

    [System.Runtime.Serialization.IgnoreDataMember]
    List<string>? Scratch { get; set; }
}

[Collection("IniConfigRegistry")]
public sealed class DraftTests : IDisposable
{
    private readonly string _tempDir;

    public DraftTests()
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

    private string PathOf(string fileName) => Path.Combine(_tempDir, fileName);

    private IniConfigBuilder Builder(string fileName, DraftSettingsImpl section, string content = "[Draft]\nName = from-file\nLimits.max = 3\n")
    {
        File.WriteAllText(PathOf(fileName), content);
        return IniConfigRegistry.ForFile(fileName)
            .AddSearchPath(_tempDir)
            .RegisterSection<IDraftSettings>(section);
    }

    // ── CreateDraft ───────────────────────────────────────────────────────────

    [Fact]
    public void CreateDraft_CopiesEveryValue_AndKnowsItsSource()
    {
        var live = new DraftSettingsImpl();
        using var config = Builder("copy.ini", live).Build();
        live.Session = "s1";

        IDraftSettings draft = ((IDraftSettings)live).CreateDraft();

        Assert.NotSame(live, draft);
        Assert.IsType<DraftSettingsImpl>(draft);
        Assert.True(draft.IsDraft());
        Assert.False(((IIniSection)live).IsDraft());
        Assert.Same(live, draft.GetDraftSource());
        Assert.Equal("from-file", draft.Name);
        Assert.Equal(5, draft.Level);
        Assert.Equal(new[] { "a", "b" }, draft.Tags);
        Assert.Equal(new[] { 1, 2 }, draft.Numbers);
        Assert.Equal(3, draft.Limits!["max"]);
        Assert.Equal("s1", draft.Session);
        Assert.Equal("from-file", draft.GetDraftBaseValue("Name"));
        Assert.False(draft.HasChanges);
        Assert.Empty(draft.GetDraftChanges());
    }

    [Fact]
    public void CreateDraft_KeepsMetadataAndValueOrigins()
    {
        var live = new DraftSettingsImpl();
        using var config = Builder("meta.ini", live).Build();

        var draft = live.CreateDraft();

        Assert.Equal(live.SectionName, draft.SectionName);
        Assert.Equal(live.GetKeys(), draft.GetKeys());
        Assert.Equal(ValueOriginKind.UserFile, draft.GetValueOrigin("Name").Kind);
        Assert.Equal(ValueOriginKind.CompiledDefault, draft.GetValueOrigin("Level").Kind);
        Assert.True(draft.IsUserValue("Name"));
        Assert.False(draft.IsUserValue("Level"));
        Assert.Equal("from-file", draft.GetRawValue("Name"));
    }

    [Fact]
    public void CreateDraft_ThroughIIniSection_ReturnsTheSameGeneratedType()
    {
        var live = new DraftSettingsImpl();
        using var config = Builder("plugin.ini", live).Build();

        // The host only knows IIniSection (e.g. sections registered by plugins)
        IIniSection section = config.GetSections().Single();
        IIniSection draft = section.CreateDraft();

        Assert.IsType<DraftSettingsImpl>(draft);
        Assert.Same(section, draft.GetDraftSource());
    }

    [Fact]
    public void Draft_DeepCopiesCollections()
    {
        var live = new DraftSettingsImpl();
        using var config = Builder("deep.ini", live).Build();

        var draft = live.CreateDraft();
        draft.Tags!.Add("c");
        draft.Numbers![0] = 42;
        draft.Limits!["max"] = 7;
        draft.Limits["min"] = 1;

        Assert.Equal(new[] { "a", "b" }, live.Tags);
        Assert.Equal(new[] { 1, 2 }, live.Numbers);
        Assert.Equal(3, live.Limits!["max"]);
        Assert.False(live.Limits.ContainsKey("min"));
        Assert.NotSame(live.Tags, draft.Tags);
        Assert.NotSame(live.Numbers, draft.Numbers);
        Assert.NotSame(live.Limits, draft.Limits);

        // Collections changed in place are changes too, and are applied as copies
        Assert.Equal(new[] { "Tags", "Numbers", "Limits" }, draft.GetDraftChanges());
        draft.ApplyDraft();
        Assert.Equal(new[] { "a", "b", "c" }, live.Tags);
        Assert.Equal(new[] { 42, 2 }, live.Numbers);
        Assert.Equal(7, live.Limits["max"]);
        Assert.Equal(1, live.Limits["min"]);
        Assert.NotSame(live.Tags, draft.Tags);
        draft.Tags.Add("d");
        Assert.Equal(3, live.Tags.Count);
    }

    [Fact]
    public void Draft_CopiesOtherPropertyShapes()
    {
        var live = new DraftShapesSettingsImpl { Scratch = new List<string> { "s" } };
        using var config = IniConfigRegistry.ForFile("shapes.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<IDraftShapesSettings>(live)
            .Build();
        live.Map = new Dictionary<string, string> { ["k"] = "v" };

        var draft = (DraftShapesSettingsImpl)((IDraftShapesSettings)live).CreateDraft();
        draft.Ints!.Add(3);
        draft.Map!["k"] = "w";
        draft.Scratch!.Add("t");
        draft.ReadOnlyValue = "changed";

        Assert.Equal(new[] { 1, 2 }, live.Ints);
        Assert.Equal("v", live.Map["k"]);
        Assert.Equal(new[] { "s" }, live.Scratch);
        Assert.Equal(new[] { "x", "y" }, draft.ReadOnlyList);
        Assert.Empty(new[] { "ReadOnlyList" }.Intersect(draft.GetDraftChanges()));

        draft.ApplyDraft();
        Assert.Equal(new[] { 1, 2, 3 }, live.Ints);
        Assert.Equal("w", live.Map["k"]);
        Assert.Equal(new[] { "s", "t" }, live.Scratch);
        Assert.Equal("changed", live.ReadOnlyValue);
    }

    // ── Locks ─────────────────────────────────────────────────────────────────

    [Fact]
    public void ConstantKey_StaysLockedInTheDraft()
    {
        var constants = PathOf("const-constants.ini");
        File.WriteAllText(constants, "[Draft]\nName = admin\n");
        var live = new DraftSettingsImpl();
        using var config = Builder("const.ini", live).AddConstantsFile(constants).Build();

        var draft = live.CreateDraft();

        Assert.True(draft.IsConstant("Name"));
        Assert.False(draft.IsConstant("Level"));
        Assert.Equal("admin", draft.Name);
        Assert.Throws<AccessViolationException>(() => draft.Name = "x");
        Assert.Throws<AccessViolationException>(() => draft.SetRawValue("Name", "x"));
        Assert.Equal("admin", draft.Name);
        Assert.Equal(ValueOriginKind.ConstantsFile, draft.GetValueOrigin("Name").Kind);
    }

    [Fact]
    public void PolicyLock_StaysLockedInTheDraft()
    {
        var policy = new PolicySource().With("Draft", "Level", "8");
        var live = new DraftSettingsImpl();
        using var config = Builder("policy.ini", live).AddValueSource(policy, ValueSourceLayer.Constant).Build();

        var draft = live.CreateDraft();

        Assert.True(draft.IsConstant("Level"));
        Assert.Equal(8, draft.Level);
        Assert.Throws<AccessViolationException>(() => draft.Level = 2);
        Assert.Equal(ValueOriginKind.ValueSource, draft.GetValueOrigin("Level").Kind);
    }

    [Fact]
    public void Apply_KeyLockedInTheMeantime_ThrowsBeforeWritingAnything()
    {
        var live = new DraftSettingsImpl();
        using var config = Builder("locked-later.ini", live).Build();
        var draft = live.CreateDraft();
        draft.Enabled = false;
        draft.Name = "edited";

        // An administrator locks Name while the dialog is open
        var constants = PathOf("locked-later-constants.ini");
        File.WriteAllText(constants, "[Draft]\nName = admin\n");
        config.ConstantFilePaths.Add(constants);
        config.Reload();
        Assert.True(live.IsConstant("Name"));

        Assert.Throws<AccessViolationException>(() => draft.ApplyDraft());
        Assert.True(live.Enabled);
        Assert.Equal("admin", live.Name);
    }

    // ── Apply ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Apply_WritesOnlyAssignedAndChangedKeys()
    {
        var live = new DraftSettingsImpl();
        using var config = Builder("only.ini", live).Build();
        var draft = live.CreateDraft();

        draft.Name = "edited";
        draft.Level = 7;
        draft.Level = 5;          // assigned, but back at its base value
        draft.Enabled = true;     // same value: not even an assignment

        Assert.Equal(new[] { "Name" }, draft.GetDraftChanges());
        Assert.Contains("Level", draft.GetDraftAssignedKeys());

        var changed = new List<string?>();
        live.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        var applied = draft.ApplyTo(live);

        Assert.Equal(new[] { "Name" }, applied);
        Assert.Equal(new[] { "Name" }, changed);
        Assert.Equal("edited", live.Name);
        Assert.True(live.HasChanges);
        Assert.True(live.IsUserValue("Name"));
        Assert.False(live.IsUserValue("Level"));

        // The applied values are the new base: applying again writes nothing
        Assert.Empty(draft.ApplyDraft());
    }

    [Fact]
    public void Apply_AssignedKeyChangedLiveMeanwhile_DraftWins()
    {
        var live = new DraftSettingsImpl();
        using var config = Builder("conflict.ini", live).Build();
        var draft = live.CreateDraft();

        draft.Counter = 10;   // the user edits the counter in the dialog
        live.Counter = 3;     // and the application changes it meanwhile

        draft.ApplyDraft();

        Assert.Equal(10, live.Counter);
    }

    [Fact]
    public void Apply_UntouchedKeyChangedLiveMeanwhile_LiveValueStays()
    {
        var live = new DraftSettingsImpl();
        using var config = Builder("untouched.ini", live).Build();
        var draft = live.CreateDraft();

        draft.Name = "edited";
        live.Counter = 3;     // e.g. a capture increments a file counter while the dialog is open
        live.Tags!.Add("live");

        var applied = draft.ApplyDraft();

        Assert.Equal(new[] { "Name" }, applied);
        Assert.Equal(3, live.Counter);
        Assert.Equal(new[] { "a", "b", "live" }, live.Tags);
        Assert.Equal(0, draft.Counter);
    }

    [Fact]
    public void Apply_RuntimeOnlyProperty_IsCopiedAndApplied()
    {
        var live = new DraftSettingsImpl();
        using var config = Builder("runtime.ini", live).Build();
        var draft = live.CreateDraft();

        draft.Session = "edited";
        Assert.Equal("runtime", live.Session);

        Assert.Equal(new[] { "Session" }, draft.ApplyDraft());
        Assert.Equal("edited", live.Session);
    }

    [Fact]
    public void Apply_TransactionalSection_ReadersSeeAllKeysSwitchAtOnce()
    {
        var live = new DraftTxSettingsImpl();
        using var config = IniConfigRegistry.ForFile("tx.ini")
            .AddSearchPath(_tempDir)
            .RegisterSection<IDraftTxSettings>(live)
            .Build();
        var draft = live.CreateDraft();
        draft.First = "a2";
        draft.Second = "b2";

        // A reader that looks at the live section on every change only ever sees a consistent pair
        var seen = new List<(string?, string?)>();
        live.PropertyChanged += (_, _) => seen.Add((live.First, live.Second));
        draft.ApplyDraft();

        Assert.False(live.IsInTransaction);
        Assert.Equal("a2", live.First);
        Assert.Equal("b2", live.Second);
        Assert.NotEmpty(seen);
        Assert.All(seen, pair => Assert.Equal(("a2", "b2"), pair));
    }

    [Fact]
    public void IniConfigApply_AppliesSeveralDraftsAndSavesOnce()
    {
        var live = new DraftSettingsImpl();
        var tx = new DraftTxSettingsImpl();
        var listener = new SaveCounter();
        using var config = Builder("many.ini", live)
            .RegisterSection<IDraftTxSettings>(tx)
            .AddListener(listener)
            .Build();

        var draft = live.CreateDraft();
        var txDraft = tx.CreateDraft();
        var untouched = live.CreateDraft();
        draft.Name = "edited";
        txDraft.Second = "b2";

        var applied = config.Apply(new IIniSection[] { draft, txDraft, untouched });

        Assert.Equal(1, listener.Saves);
        Assert.Equal(new[] { "Name" }, applied[live]);
        Assert.Equal(new[] { "Second" }, applied[tx]);
        Assert.False(config.HasPendingChanges());
        var content = File.ReadAllText(PathOf("many.ini"));
        Assert.Contains("Name = edited", content);
        Assert.Contains("Second = b2", content);
    }

    [Fact]
    public void IniConfigApply_WithoutChanges_DoesNotSave()
    {
        var live = new DraftSettingsImpl();
        var listener = new SaveCounter();
        using var config = Builder("nochange.ini", live).AddListener(listener).Build();

        var applied = config.Apply(live.CreateDraft());

        Assert.Empty(applied);
        Assert.Equal(0, listener.Saves);
    }

    [Fact]
    public async Task IniConfigApplyAsync_AppliesAndSaves()
    {
        var live = new DraftSettingsImpl();
        var listener = new SaveCounter();
        using var config = Builder("async.ini", live).AddListener(listener).Build();
        var draft = live.CreateDraft();
        draft.Level = 9;

        var applied = await config.ApplyAsync(new IIniSection[] { draft });

        Assert.Equal(new[] { "Level" }, applied[live]);
        Assert.Equal(1, listener.Saves);
        Assert.Contains("Level = 9", File.ReadAllText(PathOf("async.ini")));
    }

    [Fact]
    public void IniConfigApply_RejectsSectionsThatAreNoDraftsOrBelongElsewhere()
    {
        var live = new DraftSettingsImpl();
        using var config = Builder("reject.ini", live).Build();
        var other = new DraftSettingsImpl();
        using var otherConfig = Builder("reject-other.ini", other).Build();

        Assert.Throws<ArgumentException>(() => config.Apply(live));
        Assert.Throws<ArgumentException>(() => config.Apply(other.CreateDraft()));
    }

    [Fact]
    public void Draft_CannotBeRegistered()
    {
        var live = new DraftSettingsImpl();
        using var config = Builder("register.ini", live).Build();
        var draft = live.CreateDraft();

        Assert.Throws<ArgumentException>(() => IniConfigRegistry.ForFile("register-draft.ini").RegisterSection<IDraftSettings>(draft));
    }

    // ── Invisible to the configuration ────────────────────────────────────────

    [Fact]
    public async Task AutoSave_NeverWritesADraft()
    {
        var live = new DraftSettingsImpl();
        var listener = new SaveCounter();
        using var config = Builder("autosave.ini", live)
            .AutoSaveInterval(TimeSpan.FromMilliseconds(50))
            .AddListener(listener)
            .Build();
        var before = File.ReadAllText(PathOf("autosave.ini"));

        var draft = live.CreateDraft();
        draft.Name = "edited";
        draft.Tags!.Add("c");
        draft.MarkAsDirty();
        await Task.Delay(400);

        Assert.True(draft.HasChanges);
        Assert.False(live.HasChanges);
        Assert.False(config.HasPendingChanges());
        Assert.Equal(0, listener.Saves);
        Assert.Equal(before, File.ReadAllText(PathOf("autosave.ini")));
        Assert.DoesNotContain(draft, config.GetSections());
    }

    [Fact]
    public void Reload_DoesNotTouchTheDraft()
    {
        var live = new DraftSettingsImpl();
        using var config = Builder("reload.ini", live).Build();
        var draft = live.CreateDraft();

        File.WriteAllText(PathOf("reload.ini"), "[Draft]\nName = reloaded\nLevel = 2\n");
        config.Reload();

        Assert.Equal("reloaded", live.Name);
        Assert.Equal("from-file", draft.Name);
        Assert.Equal(5, draft.Level);
    }

    // ── Events and validation ─────────────────────────────────────────────────

    [Fact]
    public void Events_AreRaisedOnTheDraft_AndOnTheLiveSectionOnlyOnApply()
    {
        var live = new DraftSettingsImpl();
        using var config = Builder("events.ini", live).Build();
        var draft = live.CreateDraft();
        var liveEvents = new List<string?>();
        var draftChanging = new List<string?>();
        var draftChanged = new List<string?>();
        live.PropertyChanged += (_, e) => liveEvents.Add(e.PropertyName);
        live.PropertyChanging += (_, e) => liveEvents.Add("changing " + e.PropertyName);
        draft.PropertyChanging += (_, e) => draftChanging.Add(e.PropertyName);
        draft.PropertyChanged += (_, e) => draftChanged.Add(e.PropertyName);

        draft.Name = "edited";
        draft.Level = 6;

        Assert.Equal(new[] { "Name", "Level" }, draftChanging);
        Assert.Equal(new[] { "Name", "Level" }, draftChanged);
        Assert.Empty(liveEvents);

        draft.ApplyDraft();
        Assert.Equal(new[] { "changing Name", "Name", "changing Level", "Level" }, liveEvents);
    }

    [Fact]
    public void Validation_RunsOnTheDraft()
    {
        var live = new DraftSettingsImpl();
        using var config = Builder("validation.ini", live).Build();
        var draft = live.CreateDraft();
        var errorsChanged = new List<string?>();
        draft.ErrorsChanged += (_, e) => errorsChanged.Add(e.PropertyName);

        Assert.False(draft.HasErrors);
        draft.Level = 42;

        Assert.True(draft.HasErrors);
        Assert.Contains("Level", errorsChanged);
        Assert.Single(draft.GetErrors(nameof(IDraftSettings.Level)).Cast<string>());
        Assert.False(live.HasErrors);
    }

    [Fact]
    public void Validation_ErrorsOfTheLiveValuesAreReportedOnTheDraft()
    {
        var live = new DraftSettingsImpl();
        using var config = Builder("invalid.ini", live, "[Draft]\nLevel = 99\n").Build();
        Assert.True(live.HasErrors);

        var draft = live.CreateDraft();

        Assert.True(draft.HasErrors);
        draft.Level = 3;
        Assert.False(draft.HasErrors);
        Assert.True(live.HasErrors);
    }

    // ── Refresh ───────────────────────────────────────────────────────────────

    [Fact]
    public void Refresh_TakesOverLiveValuesOfKeysNotAssigned()
    {
        var live = new DraftSettingsImpl();
        using var config = Builder("refresh.ini", live).Build();
        var draft = live.CreateDraft();
        draft.Name = "edited";
        draft.Tags!.Add("draft");

        live.Name = "live-name";
        live.Counter = 4;
        live.Tags!.Add("live");
        var changed = new List<string?>();
        draft.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        var refreshed = draft.RefreshDraft();

        Assert.Equal(new[] { "Counter" }, refreshed);
        Assert.Equal(new[] { "Counter" }, changed);
        Assert.Equal(4, draft.Counter);
        Assert.Equal("edited", draft.Name);
        Assert.Equal(new[] { "a", "b", "draft" }, draft.Tags);
        Assert.Equal(4, draft.GetDraftBaseValue("Counter"));

        // The refreshed key is not a change of the draft
        Assert.Equal(new[] { "Name", "Tags" }, draft.GetDraftChanges());
        draft.ApplyDraft();
        Assert.Equal(4, live.Counter);
        Assert.Equal(new[] { "a", "b", "draft" }, live.Tags);
    }

    [Fact]
    public void Refresh_TakesOverNewLocks()
    {
        var live = new DraftSettingsImpl();
        using var config = Builder("refresh-lock.ini", live).Build();
        var draft = live.CreateDraft();

        var constants = PathOf("refresh-lock-constants.ini");
        File.WriteAllText(constants, "[Draft]\nLevel = 9\n");
        config.ConstantFilePaths.Add(constants);
        config.Reload();

        Assert.False(draft.IsConstant("Level"));
        draft.RefreshDraft();
        Assert.True(draft.IsConstant("Level"));
        Assert.Equal(9, draft.Level);
        Assert.Throws<AccessViolationException>(() => draft.Level = 1);
    }

    // ── Errors ────────────────────────────────────────────────────────────────

    [Fact]
    public void DraftOperations_OnALiveSection_Throw()
    {
        var live = new DraftSettingsImpl();
        using var config = Builder("notadraft.ini", live).Build();

        Assert.Throws<InvalidOperationException>(() => live.ApplyDraft());
        Assert.Throws<InvalidOperationException>(() => live.RefreshDraft());
        Assert.Throws<InvalidOperationException>(() => live.GetDraftChanges());
        Assert.Throws<ArgumentException>(() => live.CreateDraft().ApplyTo(new DraftTxSettingsImpl()));
    }

    private sealed class SaveCounter : IniConfigListenerBase
    {
        public int Saves;
        public override void OnSaved(string filePath) => Interlocked.Increment(ref Saves);
    }

    private sealed class PolicySource : IValueSource
    {
        private readonly Dictionary<string, string?> _values = new(StringComparer.OrdinalIgnoreCase);

        public PolicySource With(string section, string key, string? value)
        {
            _values[section + "/" + key] = value;
            return this;
        }

        public bool TryGetValue(string sectionName, string key, out string? value)
            => _values.TryGetValue(sectionName + "/" + key, out value);

        public event EventHandler<ValueChangedEventArgs>? ValueChanged { add { } remove { } }
    }
}
