// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.ComponentModel;
using Dapplo.Ini.Internationalization.Configuration;

namespace Dapplo.Ini.Internationalization.Tests;

/// <summary>
/// <see cref="LanguageConfigBuilder.AllowLateSectionRegistration"/>: plugins register their sections after the host
/// has loaded the language configuration and shown UI.
/// </summary>
public sealed class LateSectionRegistrationTests : TempDirectoryTestBase
{
    private readonly string _hostDir;
    private readonly string _pluginDir;

    public LateSectionRegistrationTests()
    {
        _hostDir = Dir("host");
        _pluginDir = Dir("imgur");
        Write(_hostDir, "app.en-US.ini", """
            [MainLanguage]
            WelcomeMessage=Welcome
            ErrorTitle=Error
            SaveButton=Save
            CancelButton=Cancel
            """);
        // CancelButton is missing in German: it stays "Cancel" (fallback) and must not raise PropertyChanged.
        // SaveButton is the same text in both languages.
        Write(_hostDir, "app.de-DE.ini", """
            [MainLanguage]
            WelcomeMessage=Willkommen
            ErrorTitle=Fehler
            SaveButton=Save
            """);
        Write(_pluginDir, "app.imgur.en-US.ini", """
            [Imgur]
            History=History
            Upload=Upload
            """);
        // Upload is missing in German: falls back to English.
        Write(_pluginDir, "app.imgur.de-DE.ini", """
            [Imgur]
            History=Verlauf
            """);
    }

    private LanguageConfigBuilder CreateBuilder(INotifyMainLanguage main, TestLanguageListener listener, bool allowLate = true)
    {
        var builder = LanguageConfigBuilder.ForBasename("app")
            .AddSearchPath(_hostDir)
            .WithBaseLanguage("en-US")
            .RegisterSection<INotifyMainLanguage>(main)
            .AddListener(listener);
        return allowLate ? builder.AllowLateSectionRegistration() : builder;
    }

    [Fact]
    public void PluginScenario_LateSectionIsLoadedRightAway_AndLanguageSwitchUpdatesBothSections()
    {
        var listener = new TestLanguageListener();
        var main = new NotifyMainLanguageImpl();
        using var config = CreateBuilder(main, listener).Build();
        var languageChanged = 0;
        config.LanguageChanged += (_, _) => languageChanged++;

        // The host shows its texts
        Assert.Equal("Welcome", main.WelcomeMessage);

        // A plugin starts later and registers its section: its texts are there without a reload
        var mainChanges = new List<string?>();
        main.PropertyChanged += (_, e) => mainChanges.Add(e.PropertyName);
        var imgur = config.RegisterSection<IImgurLanguage>(new ImgurLanguageImpl(), _pluginDir);

        Assert.Equal("History", imgur.History);
        Assert.Equal("Upload", imgur.Upload);
        Assert.Equal(0, languageChanged);
        Assert.Empty(mainChanges);   // the host section was not reloaded
        Assert.Contains(("Imgur", true), listener.SectionsAdded);
        Assert.Empty(listener.Errors);

        // A language switch updates both sections and raises PropertyChanged only for changed texts
        var imgurChanges = new List<string?>();
        imgur.PropertyChanged += (_, e) => imgurChanges.Add(e.PropertyName);

        config.SetLanguage("de-DE");

        Assert.Equal("Willkommen", main.WelcomeMessage);
        Assert.Equal("Cancel", main.CancelButton);
        Assert.Equal("Verlauf", imgur.History);
        Assert.Equal("Upload", imgur.Upload);
        Assert.Equal(new[] { nameof(INotifyMainLanguage.WelcomeMessage), nameof(INotifyMainLanguage.ErrorTitle), "Item[]" }, mainChanges);
        Assert.Equal(new[] { nameof(IImgurLanguage.History), "Item[]" }, imgurChanges);
        Assert.Equal(1, languageChanged);
    }

    [Fact]
    public void LateSection_UsesCurrentLanguageAndFallbackChain()
    {
        var listener = new TestLanguageListener();
        var main = new NotifyMainLanguageImpl();
        using var config = CreateBuilder(main, listener).WithCurrentLanguage("de-DE").Build();

        var imgur = config.RegisterSection<IImgurLanguage>(new ImgurLanguageImpl(), _pluginDir);

        Assert.Equal("Verlauf", imgur.History);
        Assert.Equal("Upload", imgur.Upload);   // from the en-US fallback
    }

    [Fact]
    public async Task RegisterSectionAsync_LateSectionIsLoadedRightAway()
    {
        var listener = new TestLanguageListener();
        var main = new NotifyMainLanguageImpl();
        using var config = await CreateBuilder(main, listener).WithCurrentLanguage("de-DE").BuildAsync();

        var imgur = await config.RegisterSectionAsync<IImgurLanguage>(new ImgurLanguageImpl(), _pluginDir);

        Assert.Equal("Verlauf", imgur.History);
        Assert.Contains(("Imgur", true), listener.SectionsAdded);
    }

    [Fact]
    public async Task RegisterSectionAsync_Cancelled_DoesNotRegister()
    {
        var listener = new TestLanguageListener();
        using var config = CreateBuilder(new NotifyMainLanguageImpl(), listener).Build();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => config.RegisterSectionAsync<IImgurLanguage>(new ImgurLanguageImpl(), _pluginDir, cts.Token));
        Assert.Throws<InvalidOperationException>(() => config.GetSection<IImgurLanguage>());
    }

    [Fact]
    public void WithoutOption_LateSectionStaysEmptyUntilNextSwitch_AndIsReported()
    {
        var listener = new TestLanguageListener();
        using var config = CreateBuilder(new NotifyMainLanguageImpl(), listener, allowLate: false).Build();

        var imgur = config.RegisterSection<IImgurLanguage>(new ImgurLanguageImpl(), _pluginDir);

        // Today's behaviour is kept ...
        Assert.Equal("###History###", imgur.History);
        // ... but the mistake is visible
        Assert.Contains(("Imgur", false), listener.SectionsAdded);
        var error = Assert.Single(listener.Errors);
        Assert.Equal("RegisterSection", error.Operation);
        Assert.Contains(nameof(LanguageConfigBuilder.AllowLateSectionRegistration), error.Exception.Message);

        config.SetLanguage("de-DE");
        Assert.Equal("Verlauf", imgur.History);
    }

    [Fact]
    public void RegistrationBeforeLoad_IsReportedAsNotLoaded_WithoutError()
    {
        var listener = new TestLanguageListener();
        var config = CreateBuilder(new NotifyMainLanguageImpl(), listener).Create();
        using (config)
        {
            var imgur = config.RegisterSection<IImgurLanguage>(new ImgurLanguageImpl(), _pluginDir);
            Assert.False(config.IsLoaded);
            Assert.Equal(("Imgur", false), Assert.Single(listener.SectionsAdded));
            Assert.Empty(listener.Errors);

            config.Load();
            Assert.True(config.IsLoaded);
            Assert.Equal("History", imgur.History);
        }
    }

    [Fact]
    public void ReservedSectionName_IsRejected()
    {
        using var config = CreateBuilder(new NotifyMainLanguageImpl(), new TestLanguageListener()).Build();
        Assert.Throws<ArgumentException>(() => config.RegisterSection<IReservedLanguage>(new ReservedLanguageImpl()));
    }

    [Fact]
    public async Task LateRegistration_WhileOtherThreadsSwitchLanguageAndReadTexts_IsThreadSafe()
    {
        var listener = new TestLanguageListener();
        var main = new NotifyMainLanguageImpl();
        using var config = CreateBuilder(main, listener).Build();
        var editorDir = Dir("editor");
        Write(editorDir, "app.en-US.ini", "[Editor]\nTitle=Editor\nUndo=Undo");
        Write(editorDir, "app.de-DE.ini", "[Editor]\nTitle=Bearbeiten\nUndo=Rückgängig");

        using var stop = new CancellationTokenSource();
        var reader = Task.Run(() =>
        {
            while (!stop.IsCancellationRequested)
            {
                _ = main.WelcomeMessage;
                _ = config.GetTranslation("WelcomeMessage");
                _ = config.TryGetTranslation("imgur.history", out _);
            }
        });
        var switcher = Task.Run(() =>
        {
            for (var i = 0; i < 50; i++)
                config.SetLanguage(i % 2 == 0 ? "de-DE" : "en-US");
        });

        var imgurTask = Task.Run(() => config.RegisterSection<IImgurLanguage>(new ImgurLanguageImpl(), _pluginDir));
        var editorTask = Task.Run(() => config.RegisterSectionAsync<IEditorLanguage>(new EditorLanguageImpl(), editorDir));
        var imgur = await imgurTask;
        var editor = await editorTask;
        await switcher;
        stop.Cancel();
        await reader;

        Assert.Empty(listener.Errors);
        // Whatever the interleaving, every section ends up with the texts of the current language.
        var german = config.CurrentLanguage == "de-DE";
        Assert.Equal(german ? "Willkommen" : "Welcome", main.WelcomeMessage);
        Assert.Equal(german ? "Verlauf" : "History", imgur.History);
        Assert.Equal(german ? "Bearbeiten" : "Editor", editor.Title);
    }

    [Fact]
    public async Task HandlerMarshallingToBusyUiThread_DoesNotDeadlockWithRegistrationOnThatThread()
    {
        // Greenshot: a background language switch raises PropertyChanged, the handler calls Dispatcher.Invoke,
        // while the UI thread is busy registering a plugin section.
        var main = new NotifyMainLanguageImpl();
        using var config = CreateBuilder(main, new TestLanguageListener()).Build();
        using var ui = new SimulatedUiThread();
        var inHandler = new ManualResetEventSlim();
        main.PropertyChanged += (_, _) =>
        {
            inHandler.Set();
            ui.Invoke(() => { });   // blocks until the UI thread processes its queue
        };

        ui.Post(() =>
        {
            inHandler.Wait();
            config.RegisterSection<IImgurLanguage>(new ImgurLanguageImpl(), _pluginDir);
        });
        var switching = Task.Run(() => config.SetLanguage("de-DE"));

        var completed = await Task.WhenAny(switching, Task.Delay(TimeSpan.FromSeconds(10)));
        Assert.Same(switching, completed);
        await switching;
        Assert.Equal("Willkommen", main.WelcomeMessage);
        ui.Invoke(() => Assert.Equal("Verlauf", config.GetSection<IImgurLanguage>().History));
    }

    [Fact]
    public void ReentrantSetLanguage_FromPropertyChangedHandler_LeavesAllSectionsInTheLastLanguage()
    {
        var editorDir = Dir("editor");
        Write(editorDir, "app.en-US.ini", "[Editor]\nTitle=Editor");
        Write(editorDir, "app.de-DE.ini", "[Editor]\nTitle=Bearbeiten");
        Write(editorDir, "app.fr-FR.ini", "[Editor]\nTitle=Éditeur");
        Write(_hostDir, "app.fr-FR.ini", "[MainLanguage]\nWelcomeMessage=Bienvenue");
        var main = new NotifyMainLanguageImpl();
        using var config = CreateBuilder(main, new TestLanguageListener()).Build();
        var editor = config.RegisterSection<IEditorLanguage>(new EditorLanguageImpl(), editorDir);
        var switched = false;
        main.PropertyChanged += (_, _) =>
        {
            if (switched) return;
            switched = true;
            config.SetLanguage("fr-FR");
        };

        config.SetLanguage("de-DE");

        Assert.Equal("fr-FR", config.CurrentLanguage);
        Assert.Equal("Bienvenue", main.WelcomeMessage);
        Assert.Equal("Éditeur", editor.Title);
    }

    [Fact]
    public void RegistrationFromHandlerDuringFirstLoad_IsLoaded()
    {
        var listener = new TestLanguageListener();
        var main = new NotifyMainLanguageImpl();
        var config = CreateBuilder(main, listener).Create();
        using (config)
        {
            IImgurLanguage? imgur = null;
            main.PropertyChanged += (_, _) => imgur ??= config.RegisterSection<IImgurLanguage>(new ImgurLanguageImpl(), _pluginDir);

            config.Load();

            Assert.NotNull(imgur);
            Assert.Equal("History", imgur!.History);
            Assert.Contains(("Imgur", true), listener.SectionsAdded);
            Assert.Empty(listener.Errors);
        }
    }

    [Fact]
    public void RegistrationOnAnotherThread_AwaitedByAHandler_DoesNotDeadlock()
    {
        var main = new NotifyMainLanguageImpl();
        using var config = CreateBuilder(main, new TestLanguageListener()).Build();
        IImgurLanguage? imgur = null;
        main.PropertyChanged += (_, _) =>
        {
            if (imgur != null) return;
            var registration = Task.Run(() => config.RegisterSection<IImgurLanguage>(new ImgurLanguageImpl(), _pluginDir));
            Assert.True(registration.Wait(TimeSpan.FromSeconds(10)), "registration blocked");
            imgur = registration.Result;
        };

        config.SetLanguage("de-DE");

        Assert.Equal("Verlauf", imgur!.History);
    }

    private sealed class ThrowingListener : Dapplo.Ini.Internationalization.Interfaces.LanguageConfigListenerBase
    {
        public override void OnFileLoaded(string filePath) => throw new InvalidOperationException("listener bug");
    }

    [Fact]
    public void ThrowingListenerOrHandler_DoesNotLeaveTheSwitchHalfApplied()
    {
        var editorDir = Dir("editor");
        Write(editorDir, "app.en-US.ini", "[Editor]\nTitle=Editor");
        Write(editorDir, "app.de-DE.ini", "[Editor]\nTitle=Bearbeiten");
        var listener = new TestLanguageListener();
        var main = new NotifyMainLanguageImpl();
        var editor = new EditorLanguageImpl();
        // Every load reports through a faulty listener; later also a faulty PropertyChanged handler.
        using var faulty = LanguageConfigBuilder.ForBasename("app")
            .AddSearchPath(_hostDir)
            .WithBaseLanguage("en-US")
            .RegisterSection<INotifyMainLanguage>(main)
            .RegisterSection<IEditorLanguage>(editor, editorDir)
            .AddListener(listener)
            .AddListener(new ThrowingListener())
            .Create();
        Assert.Throws<InvalidOperationException>(() => faulty.Load());
        Assert.True(faulty.IsLoaded);
        Assert.Equal("Welcome", main.WelcomeMessage);

        main.PropertyChanged += (_, _) => throw new InvalidOperationException("handler bug");
        var languageChanged = 0;
        faulty.LanguageChanged += (_, _) => languageChanged++;

        var exception = Assert.Throws<InvalidOperationException>(() => faulty.SetLanguage("de-DE"));

        // The first failure is re-thrown after everything ran; the switch is complete
        Assert.Equal("listener bug", exception.Message);
        Assert.Equal("de-DE", faulty.CurrentLanguage);
        Assert.Equal("Willkommen", main.WelcomeMessage);
        Assert.Equal("Bearbeiten", editor.Title);
        Assert.Equal(1, languageChanged);
        Assert.Contains(listener.Errors, e => e.Operation == "SetLanguage" && e.Exception.Message == "handler bug");
    }

    [Fact]
    public void FailedSwitch_KeepsThePreviousLanguage_AndLateSectionsUseIt()
    {
        var listener = new TestLanguageListener();
        var main = new NotifyMainLanguageImpl();
        using var config = CreateBuilder(main, listener).Build();

        // A language file that exists but cannot be read
        var path = Path.Combine(_hostDir, "app.fr-FR.ini");
        FileStream? lockStream = null;
        if (OperatingSystem.IsWindows())
        {
            Write(_hostDir, "app.fr-FR.ini", "[MainLanguage]\nWelcomeMessage=Bienvenue");
            lockStream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        }
        else if (OperatingSystem.IsLinux())
        {
            File.CreateSymbolicLink(path, "/proc/self/mem");   // exists, reading throws IOException
        }
        else
        {
            return;
        }

        using (lockStream)
        {
            Assert.ThrowsAny<IOException>(() => config.SetLanguage("fr-FR"));
        }

        Assert.Equal("en-US", config.CurrentLanguage);
        Assert.Equal("en-US", config.RequestedLanguage);
        Assert.Equal("Welcome", main.WelcomeMessage);
        Assert.Contains(listener.Errors, e => e.Operation == "SetLanguage");

        var imgur = config.RegisterSection<IImgurLanguage>(new ImgurLanguageImpl(), _pluginDir);
        Assert.Equal("History", imgur.History);
    }

    [Fact]
    public async Task FileChangeReload_IncludesLateSection()
    {
        var listener = new TestLanguageListener();
        using var config = CreateBuilder(new NotifyMainLanguageImpl(), listener).MonitorFiles().Build();
        var imgur = config.RegisterSection<IImgurLanguage>(new ImgurLanguageImpl(), _pluginDir);
        var reloaded = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        config.LanguageChanged += (_, _) =>
        {
            if (imgur.History == "History (edited)") reloaded.TrySetResult(true);
        };

        // The plugin directory is only watched because of the late registration
        Write(_pluginDir, "app.imgur.en-US.ini", "[Imgur]\nHistory=History (edited)\nUpload=Upload");

        var completed = await Task.WhenAny(reloaded.Task, Task.Delay(TimeSpan.FromSeconds(10)));
        Assert.Same(reloaded.Task, completed);
        Assert.Equal("History (edited)", imgur.History);
    }
}

/// <summary>A thread with a work queue, like a UI thread with its dispatcher.</summary>
internal sealed class SimulatedUiThread : IDisposable
{
    private readonly System.Collections.Concurrent.BlockingCollection<Action> _queue = new();
    private readonly Thread _thread;

    public SimulatedUiThread()
    {
        _thread = new Thread(() =>
        {
            foreach (var action in _queue.GetConsumingEnumerable())
                action();
        }) { IsBackground = true, Name = "Simulated UI" };
        _thread.Start();
    }

    public void Post(Action action) => _queue.Add(action);

    /// <summary>Runs <paramref name="action"/> on the UI thread and waits for it (like Dispatcher.Invoke).</summary>
    public void Invoke(Action action)
    {
        if (Thread.CurrentThread == _thread)
        {
            action();
            return;
        }
        Exception? error = null;
        using var done = new ManualResetEventSlim();
        _queue.Add(() =>
        {
            try { action(); }
            catch (Exception ex) { error = ex; }
            finally { done.Set(); }
        });
        if (!done.Wait(TimeSpan.FromSeconds(20))) throw new TimeoutException("UI thread did not respond");
        if (error != null) throw new InvalidOperationException("Invoke failed", error);
    }

    public void Dispose()
    {
        _queue.CompleteAdding();
        _thread.Join(TimeSpan.FromSeconds(5));
    }
}
