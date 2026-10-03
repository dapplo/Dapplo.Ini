// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Dapplo.Ini.Internationalization.Configuration;

namespace Dapplo.Ini.Internationalization.Tests;

/// <summary>
/// <see cref="LanguageConfigBuilder.MergeSearchPaths"/>: Greenshot's search paths are the portable folder,
/// <c>%APPDATA%\Greenshot\Languages</c> (user corrections) and the installation's <c>Languages</c> folder.
/// </summary>
public sealed class MergeSearchPathsTests : TempDirectoryTestBase
{
    private readonly string _portable;
    private readonly string _appData;
    private readonly string _install;

    public MergeSearchPathsTests()
    {
        _portable = Dir("portable");
        _appData = Dir("appdata");
        _install = Dir("install");
        Write(_install, "app.en-US.ini", """
            [MainLanguage]
            WelcomeMessage=Welcome
            ErrorTitle=Error
            SaveButton=Save
            CancelButton=Cancel
            """);
        Write(_install, "app.de-DE.ini", """
            [MainLanguage]
            WelcomeMessage=Willkommen
            ErrorTitle=Fehler
            SaveButton=Speichern
            CancelButton=Abbrechen
            """);
        // The user only corrects one German text
        Write(_appData, "app.de-DE.ini", """
            [MainLanguage]
            ErrorTitle=Fehler (korrigiert)
            """);
    }

    private LanguageConfigBuilder CreateBuilder(bool merge)
    {
        var builder = LanguageConfigBuilder.ForBasename("app")
            .AddSearchPath(_portable)
            .AddSearchPath(_appData)
            .AddSearchPath(_install)
            .WithBaseLanguage("en-US")
            .WithCurrentLanguage("de-DE");
        return merge ? builder.MergeSearchPaths() : builder;
    }

    [Fact]
    public void Merge_HigherPriorityPathOverridesSingleKeys()
    {
        var main = new MainLanguageImpl();
        using var config = CreateBuilder(merge: true).RegisterSection<IMainLanguage>(main).Build();

        Assert.Equal("Fehler (korrigiert)", main.ErrorTitle);
        Assert.Equal("Willkommen", main.WelcomeMessage);
        Assert.Equal("Abbrechen", main.CancelButton);
    }

    [Fact]
    public void WithoutMerge_FirstSearchPathWithTheFileWins()
    {
        var main = new MainLanguageImpl();
        using var config = CreateBuilder(merge: false).RegisterSection<IMainLanguage>(main).Build();

        Assert.Equal("Fehler (korrigiert)", main.ErrorTitle);
        // The install's de-DE file is ignored: the other keys come from the English fallback
        Assert.Equal("Welcome", main.WelcomeMessage);
    }

    [Fact]
    public void Merge_FilesAreAppliedLowestPriorityFirst()
    {
        var listener = new TestLanguageListener();
        using var config = CreateBuilder(merge: true)
            .RegisterSection<IMainLanguage>(new MainLanguageImpl())
            .AddListener(listener)
            .Build();

        var german = listener.FilesLoaded.Where(f => f.EndsWith("app.de-DE.ini", StringComparison.Ordinal)).ToList();
        Assert.Equal(new[] { Path.Combine(_install, "app.de-DE.ini"), Path.Combine(_appData, "app.de-DE.ini") }, german);
    }

    [Fact]
    public void Merge_SectionWithOwnPath_KeepsThatPath()
    {
        var pluginDir = Dir("plugin");
        Write(pluginDir, "app.imgur.de-DE.ini", "[Imgur]\nHistory=Verlauf");
        Write(_appData, "app.imgur.de-DE.ini", "[Imgur]\nHistory=Verlauf (appdata)\nUpload=Hochladen (appdata)");

        var imgur = new ImgurLanguageImpl();
        using var config = CreateBuilder(merge: true)
            .RegisterSection<IImgurLanguage>(imgur, pluginDir)
            .Build();

        Assert.Equal("Verlauf", imgur.History);
        Assert.Equal("###Upload###", imgur.Upload);
    }

    [Fact]
    public void Merge_SectionWithOwnPathThatIsASearchPath_IsMergedAcrossAllSearchPaths()
    {
        Write(_install, "app.imgur.de-DE.ini", "[Imgur]\nHistory=Verlauf\nUpload=Hochladen");
        Write(_appData, "app.imgur.de-DE.ini", "[Imgur]\nHistory=Verlauf (appdata)");

        var imgur = new ImgurLanguageImpl();
        using var config = CreateBuilder(merge: true)
            .RegisterSection<IImgurLanguage>(imgur, _install + Path.DirectorySeparatorChar)
            .Build();

        Assert.Equal("Verlauf (appdata)", imgur.History);
        Assert.Equal("Hochladen", imgur.Upload);
    }

    [Fact]
    public async Task Merge_MonitorFiles_ReactsToANewFileInAHigherPriorityPath()
    {
        var main = new MainLanguageImpl();
        using var config = CreateBuilder(merge: true).RegisterSection<IMainLanguage>(main).MonitorFiles().Build();
        var reloaded = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        config.LanguageChanged += (_, _) =>
        {
            if (main.SaveButton == "Sichern") reloaded.TrySetResult(true);
        };

        // A user drops a new file into the portable folder
        Write(_portable, "app.de-DE.ini", "[MainLanguage]\nSaveButton=Sichern");

        var completed = await Task.WhenAny(reloaded.Task, Task.Delay(TimeSpan.FromSeconds(10)));
        Assert.Same(reloaded.Task, completed);
        Assert.Equal("Sichern", main.SaveButton);
        Assert.Equal("Fehler (korrigiert)", main.ErrorTitle);
        Assert.Equal("Willkommen", main.WelcomeMessage);
    }
}
