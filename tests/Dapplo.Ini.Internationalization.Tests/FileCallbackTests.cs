// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Dapplo.Ini.Internationalization.Configuration;

namespace Dapplo.Ini.Internationalization.Tests;

/// <summary>
/// <c>IIniConfigListener.OnFileLoaded</c> and <c>OnFileNotFound</c> are raised once per file
/// of a load, not once per section using it (Greenshot has six core sections sharing <c>greenshot.{ietf}.ini</c>).
/// </summary>
public sealed class FileCallbackTests : TempDirectoryTestBase
{
    private readonly string _languages;
    private readonly string _enUs;
    private readonly string _deDe;

    public FileCallbackTests()
    {
        _languages = Dir("languages");
        _enUs = Write(_languages, "app.en-US.ini", """
            [MainLanguage]
            WelcomeMessage=Welcome
            [Editor]
            Title=Editor
            """);
        _deDe = Write(_languages, "app.de-DE.ini", """
            [MainLanguage]
            WelcomeMessage=Willkommen
            [Editor]
            Title=Bearbeiten
            """);
        // No app.en.ini and no app.de.ini: the parent languages of the chain are missing
    }

    private static int Count(IEnumerable<string> values, string expected) =>
        values.Count(v => string.Equals(v, expected, StringComparison.OrdinalIgnoreCase));

    [Fact]
    public void TwoSectionsSharingAFile_EachFileIsReportedOncePerLoad()
    {
        var listener = new TestLanguageListener();
        var main = new MainLanguageImpl();
        var editor = new EditorLanguageImpl();
        using var config = LanguageConfigBuilder.ForBasename("app")
            .AddSearchPath(_languages)
            .WithBaseLanguage("en-US")
            .WithCurrentLanguage("en-US")
            .AddListener(listener)
            .RegisterSection<IMainLanguage>(main)
            .RegisterSection<IEditorLanguage>(editor)
            .Build();

        Assert.Equal("Welcome", main.WelcomeMessage);
        Assert.Equal("Editor", editor.Title);
        Assert.Equal(1, Count(listener.FilesLoaded, _enUs));
        Assert.Single(listener.FilesLoaded);
        // The missing parent language is reported once, not once per section
        Assert.Equal(1, Count(listener.FilesNotFound, "app.en.ini"));
        Assert.Single(listener.FilesNotFound);

        // Another language: the English fallback and German are each read and reported once
        listener.FilesLoaded.Clear();
        listener.FilesNotFound.Clear();
        config.SetLanguage("de-DE");
        Assert.Equal("Willkommen", main.WelcomeMessage);
        Assert.Equal("Bearbeiten", editor.Title);
        Assert.Equal(1, Count(listener.FilesLoaded, _enUs));
        Assert.Equal(1, Count(listener.FilesLoaded, _deDe));
        Assert.Equal(2, listener.FilesLoaded.Count);
        Assert.Equal(1, Count(listener.FilesNotFound, "app.en.ini"));
        Assert.Equal(1, Count(listener.FilesNotFound, "app.de.ini"));
        Assert.Equal(2, listener.FilesNotFound.Count);

        // And back
        listener.FilesLoaded.Clear();
        listener.FilesNotFound.Clear();
        config.SetLanguage("en-US");
        Assert.Equal("Welcome", main.WelcomeMessage);
        Assert.Equal(1, Count(listener.FilesLoaded, _enUs));
        Assert.Single(listener.FilesLoaded);
        Assert.Equal(1, Count(listener.FilesNotFound, "app.en.ini"));
        Assert.Single(listener.FilesNotFound);
    }

    [Fact]
    public async Task TwoSectionsSharingAFile_Async_EachFileIsReportedOncePerLoad()
    {
        var listener = new TestLanguageListener();
        using var config = await LanguageConfigBuilder.ForBasename("app")
            .AddSearchPath(_languages)
            .WithBaseLanguage("en-US")
            .WithCurrentLanguage("de-DE")
            .AddListener(listener)
            .RegisterSection<IMainLanguage>(new MainLanguageImpl())
            .RegisterSection<IEditorLanguage>(new EditorLanguageImpl())
            .BuildAsync();

        // In the order they apply: fallback English first, then German
        Assert.Equal(new[] { _enUs, _deDe }, listener.FilesLoaded.ToArray());
        Assert.Equal(new[] { "app.en.ini", "app.de.ini" }, listener.FilesNotFound.ToArray());
    }
}
