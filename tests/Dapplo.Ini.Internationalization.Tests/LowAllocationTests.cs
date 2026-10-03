// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using Dapplo.Ini.Internationalization.Configuration;

namespace Dapplo.Ini.Internationalization.Tests;

/// <summary>
/// Loading language files streams them through a pooled buffer, reads each file once for all sections, and reuses
/// strings of the previous load; lookups do not allocate.
/// </summary>
public sealed class LowAllocationTests : TempDirectoryTestBase
{
    private readonly string _dir;

    public LowAllocationTests()
    {
        _dir = Dir("lang");
        Write(_dir, "app.en-US.ini", """
            [__language__]
            Description=English
            [MainLanguage]
            WelcomeMessage=Welcome
            Error_Title=Error
            SaveButton=Save
            [Editor]
            Title=Editor
            Undo=Undo
            """);
        Write(_dir, "app.de-DE.ini", """
            [MainLanguage]
            WelcomeMessage=Willkommen
            WelcomeMessage=Willkommen (zweite Zeile gewinnt)
            [Editor]
            Title=Bearbeiten
            """);
    }

    private static long AllocatedBy(Action action)
    {
        for (var i = 0; i < 5; i++) action();
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 10; i++) action();
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    private LanguageConfig Build(MainLanguageImpl main, EditorLanguageImpl editor) => LanguageConfigBuilder.ForBasename("app")
        .AddSearchPath(_dir)
        .WithBaseLanguage("en-US")
        .RegisterSection<IMainLanguage>(main)
        .RegisterSection<IEditorLanguage>(editor)
        .Build();

    [Fact]
    public void Lookups_DoNotAllocate()
    {
        var main = new MainLanguageImpl();
        using var config = Build(main, new EditorLanguageImpl());

        var allocated = AllocatedBy(() =>
        {
            Assert.True(config.TryGetTranslation("error_title", out _));
            Assert.True(config.TryGetTranslation("Editor.Title", out _));
            Assert.False(config.TryGetTranslation("imgur.history", out _));
            Assert.Equal("Welcome", main["Welcome_Message"]);
            Assert.True(main.ContainsKey(" errortitle "));
            Assert.True(main.TryGetValue("SaveButton", out _));
            _ = main.WelcomeMessage;
        });

        Assert.Equal(0, allocated);
    }

    [Fact]
    public void ReloadOfUnchangedFiles_ReusesTheStrings()
    {
        var main = new MainLanguageImpl();
        var editor = new EditorLanguageImpl();
        using var config = Build(main, editor);
        config.SetLanguage("de-DE");
        var welcome = main.WelcomeMessage;
        var saveButton = main.SaveButton;
        var key = main.Keys.Single(k => k == "welcomemessage");

        config.SetLanguage("de-DE");

        Assert.Same(welcome, main.WelcomeMessage);
        Assert.Same(saveButton, main.SaveButton);
        Assert.Same(key, main.Keys.Single(k => k == "welcomemessage"));
    }

    [Fact]
    public void ReverseOrderLoading_KeepsTheSemantics()
    {
        var main = new MainLanguageImpl();
        var editor = new EditorLanguageImpl();
        using var config = Build(main, editor);

        config.SetLanguage("de-DE");

        Assert.Equal("Willkommen (zweite Zeile gewinnt)", main.WelcomeMessage);   // last line of a file wins
        Assert.Equal("Error", main.ErrorTitle);                                 // fallback for a missing key
        Assert.Equal("Bearbeiten", editor.Title);                               // more specific file wins
        Assert.Equal("Undo", editor.Undo);
        Assert.False(main.ContainsKey("Description"));
    }

    [Fact]
    public void LongKeysAndValues_UseThePooledPath()
    {
        var longKey = new string('k', 300);
        var longValue = string.Concat(Enumerable.Repeat("line\\n", 200));
        Write(_dir, "long.en-US.ini", $"[MainLanguage]\n{longKey}={longValue}\nWelcomeMessage=Hi");
        var main = new MainLanguageImpl();
        using var config = LanguageConfigBuilder.ForBasename("long").AddSearchPath(_dir).WithBaseLanguage("en-US")
            .RegisterSection<IMainLanguage>(main).Build();

        Assert.Equal(string.Concat(Enumerable.Repeat("line\n", 200)), main[longKey]);
        Assert.Equal("Hi", main.WelcomeMessage);
    }

    [Fact]
    public void Utf16AndBomFiles_AreRead()
    {
        File.WriteAllText(Path.Combine(_dir, "enc.en-US.ini"), "[MainLanguage]\r\nWelcomeMessage=Grüß Gott 😀", new UnicodeEncoding(false, true));
        File.WriteAllText(Path.Combine(_dir, "enc.de-DE.ini"), "[__language__]\r\nDescription=Deutsch ✓\r\n[MainLanguage]\r\nErrorTitle=Fehler", new UTF8Encoding(true));
        var main = new MainLanguageImpl();
        using var config = LanguageConfigBuilder.ForBasename("enc").AddSearchPath(_dir).WithBaseLanguage("en-US")
            .WithCurrentLanguage("de-DE").RegisterSection<IMainLanguage>(main).Build();

        Assert.Equal("Grüß Gott 😀", main.WelcomeMessage);
        Assert.Equal("Fehler", main.ErrorTitle);
        Assert.Equal("Deutsch ✓", config.GetLanguages().Single(l => l.Ietf == "de-DE").Description);
    }
}
