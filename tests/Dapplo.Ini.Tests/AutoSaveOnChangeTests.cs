// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Threading;
using Dapplo.Ini;
using Dapplo.Ini.Attributes;
using Dapplo.Ini.Interfaces;

namespace Dapplo.Ini.Tests;

/// <summary>A section whose IAfterLoad hook migrates a value, which makes it dirty right after the load.</summary>
[IniSection("VersionMigration")]
public interface IVersionMigrationSettings : IIniSection, IAfterLoad<IVersionMigrationSettings>
{
    [IniValue(DefaultValue = "1")]
    int Version { get; set; }

    static new void OnAfterLoad(IVersionMigrationSettings self)
    {
        if (self.Version < 2)
            self.Version = 2;
    }
}

/// <summary>
/// The auto-save timer is one-shot and armed by the first change: an idle application never wakes up for it.
/// </summary>
[Collection("IniConfigRegistry")]
public sealed class AutoSaveOnChangeTests : IDisposable
{
    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(50);
    private readonly string _tempDir;

    public AutoSaveOnChangeTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        IniConfigRegistry.Clear();
    }

    public void Dispose()
    {
        IniConfigRegistry.Clear();
        try { Directory.Delete(_tempDir, recursive: true); }
        catch (IOException) { }
    }

    private string PathOf(string fileName) => Path.Combine(_tempDir, fileName);

    /// <summary>Reads the file, the auto-save may be replacing it at that moment</summary>
    private static string ReadText(string path)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return File.ReadAllText(path);
            }
            catch (IOException) when (attempt < 100)
            {
                Thread.Sleep(10);
            }
        }
    }

    private IniConfig Build(string fileName, GeneralSettingsImpl section, IIniConfigListener? listener = null, bool allowLate = false)
    {
        File.WriteAllText(PathOf(fileName), "[General]\nAppName = Original\n");
        var builder = IniConfigRegistry.ForFile(fileName)
            .AddSearchPath(_tempDir)
            .AutoSaveInterval(Interval)
            .RegisterSection<IGeneralSettings>(section);
        if (listener != null)
            builder.AddListener(listener);
        if (allowLate)
            builder.AllowLateSectionRegistration();
        return builder.Build();
    }

    private static async Task WaitUntil(Func<bool> condition, string message)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
                Assert.Fail("Timed out waiting until " + message);
            await Task.Delay(10);
        }
    }

    /// <summary>A negative check needs some time to pass: several intervals</summary>
    private static Task SeveralIntervals() => Task.Delay(TimeSpan.FromTicks(Interval.Ticks * 6));

    [Fact]
    public async Task NoChange_TimerNeverRuns()
    {
        var section = new GeneralSettingsImpl();
        using var config = Build("idle.ini", section);
        var writeTime = File.GetLastWriteTimeUtc(PathOf("idle.ini"));

        await SeveralIntervals();

        Assert.Equal(0, config.AutoSaveTicks);
        Assert.False(config.IsAutoSaveArmed);
        Assert.Equal(writeTime, File.GetLastWriteTimeUtc(PathOf("idle.ini")));

        // The timer works: the first change arms it
        section.AppName = "Changed";
        await WaitUntil(() => config.AutoSaveTicks == 1, "the change is saved");
    }

    [Fact]
    public async Task OneChange_SavedOnce_ThenDisarmed()
    {
        var section = new GeneralSettingsImpl();
        using var config = Build("once.ini", section);

        section.AppName = "Saved";
        section.MaxRetries = 7; // a second change while armed is saved by the same run
        Assert.True(config.IsAutoSaveArmed);

        await WaitUntil(() => !config.HasPendingChanges() && !config.IsAutoSaveArmed, "saved and disarmed");
        Assert.Contains("AppName = Saved", ReadText(PathOf("once.ini")));
        Assert.Contains("MaxRetries = 7", ReadText(PathOf("once.ini")));

        await SeveralIntervals();
        Assert.Equal(1, config.AutoSaveTicks);
        Assert.False(config.IsAutoSaveArmed);
    }

    [Fact]
    public async Task ChangeDuringSave_IsSavedBySecondRun()
    {
        var section = new GeneralSettingsImpl();
        var listener = new SaveListener(onFirstSave: () => section.AppName = "SecondValue");
        using var config = Build("during.ini", section, listener);

        section.AppName = "FirstValue";

        await WaitUntil(() => ReadText(PathOf("during.ini")).Contains("SecondValue") && !config.HasPendingChanges(),
            "the change made during the first save is saved");
        Assert.True(config.AutoSaveTicks >= 2);
        Assert.Equal(2, listener.SaveCount);
        await WaitUntil(() => !config.IsAutoSaveArmed, "disarmed");
    }

    [Fact]
    public async Task Paused_ChangeIsSavedAfterResume()
    {
        var section = new GeneralSettingsImpl();
        using var config = Build("paused.ini", section);

        config.PauseAutoSave();
        section.AppName = "WhilePaused";
        await SeveralIntervals();

        Assert.True(config.HasPendingChanges());
        Assert.DoesNotContain("WhilePaused", ReadText(PathOf("paused.ini")));
        Assert.False(config.IsAutoSaveArmed);
        Assert.Equal(0, config.AutoSaveTicks);

        config.ResumeAutoSave();
        await WaitUntil(() => !config.HasPendingChanges(), "saved after resume");
        Assert.Contains("AppName = WhilePaused", ReadText(PathOf("paused.ini")));
    }

    [Fact]
    public async Task GateBusy_ChangeIsSavedAfterTheGateIsReleased()
    {
        using var release = new ManualResetEventSlim();
        using var blocking = new ManualResetEventSlim();
        var section = new GeneralSettingsImpl();
        var listener = new SaveListener(onFirstSave: () =>
        {
            blocking.Set();
            release.Wait(TimeSpan.FromSeconds(10));
        });
        using var config = Build("busy.ini", section, listener);

        // A long Save holds the gate
        config.PauseAutoSave();
        var longSave = Task.Run(() => config.Save());
        Assert.True(blocking.Wait(TimeSpan.FromSeconds(10)), "the manual save started");
        config.ResumeAutoSave();

        section.AppName = "WhileBusy";
        // The timer can't get the gate, it tries again instead of dropping the change
        await WaitUntil(() => config.AutoSaveTicks >= 2, "the timer retried while the gate was busy");
        Assert.True(config.HasPendingChanges());

        release.Set();
        await longSave;
        await WaitUntil(() => !config.HasPendingChanges(), "saved after the gate was released");
        Assert.Contains("AppName = WhileBusy", ReadText(PathOf("busy.ini")));
    }

    [Fact]
    public async Task LateRegisteredSection_ChangeArmsTheTimer()
    {
        var section = new GeneralSettingsImpl();
        using var config = Build("late.ini", section, allowLate: true);
        var late = config.AddSection<IReloadSettings>(new ReloadSettingsImpl());

        await SeveralIntervals();
        Assert.Equal(0, config.AutoSaveTicks);

        late.Value = "LateChange";
        Assert.True(config.IsAutoSaveArmed);
        await WaitUntil(() => !config.HasPendingChanges(), "the late section's change is saved");
        Assert.Contains("Value = LateChange", ReadText(PathOf("late.ini")));
    }

    [Fact]
    public async Task DisposeWhileArmed_NoExceptionAndNoSave()
    {
        var section = new GeneralSettingsImpl();
        var config = IniConfigRegistry.ForFile("dispose.ini")
            .AddSearchPath(_tempDir)
            .AutoSaveInterval(TimeSpan.FromMilliseconds(200))
            .RegisterSection<IGeneralSettings>(section)
            .Build();

        section.AppName = "NotSaved";
        Assert.True(config.IsAutoSaveArmed);
        config.Dispose();

        // Changes after the dispose don't reach the config anymore
        section.AppName = "AfterDispose";
        await Task.Delay(TimeSpan.FromMilliseconds(500));

        Assert.Equal(0, config.AutoSaveTicks);
        Assert.False(File.Exists(PathOf("dispose.ini")));
    }

    [Fact]
    public async Task ChangeByAfterLoadHook_IsSavedAlthoughItWasMadeBeforeTheTimerExisted()
    {
        // IAfterLoad runs before the post-load setup creates the timer: the timer must be armed when it starts
        File.WriteAllText(PathOf("migration.ini"), "[VersionMigration]\nVersion = 1\n");
        var section = new VersionMigrationSettingsImpl();
        using var config = IniConfigRegistry.ForFile("migration.ini")
            .AddSearchPath(_tempDir)
            .AutoSaveInterval(Interval)
            .RegisterSection<IVersionMigrationSettings>(section)
            .Build();

        Assert.Equal(2, section.Version);
        await WaitUntil(() => !config.HasPendingChanges(), "the migrated value is saved");
        Assert.Contains("Version = 2", ReadText(PathOf("migration.ini")));
    }

    [Fact]
    public async Task ReplacedSection_IsDisconnected()
    {
        var first = new GeneralSettingsImpl();
        var second = new GeneralSettingsImpl();
        File.WriteAllText(PathOf("replaced.ini"), "[General]\nAppName = Original\n");
        var config = IniConfigRegistry.ForFile("replaced.ini")
            .AddSearchPath(_tempDir)
            .AutoSaveInterval(Interval)
            .RegisterSection<IGeneralSettings>(first)
            .Create();
        // Before the load, registering the same type again replaces the section
        config.AddSection<IGeneralSettings>(second);
        config.Load();
        using (config)
        {
            first.AppName = "FromTheReplacedSection";
            Assert.False(config.IsAutoSaveArmed);
            await SeveralIntervals();
            Assert.Equal(0, config.AutoSaveTicks);
        }
    }

    private sealed class SaveListener : IniConfigListenerBase
    {
        private readonly Action _onFirstSave;
        private int _saveCount;

        public SaveListener(Action onFirstSave) => _onFirstSave = onFirstSave;

        public int SaveCount => Volatile.Read(ref _saveCount);

        public override void OnSaved(string filePath)
        {
            if (Interlocked.Increment(ref _saveCount) == 1)
                _onFirstSave();
        }
    }
}
