// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Dapplo.Ini.Internationalization.Configuration;

namespace Dapplo.Ini.Internationalization.Tests;

/// <summary>
/// Looking up keys built at runtime (<see cref="LanguageConfig.TryGetTranslation"/>), safe <c>Format</c>, and the
/// parsing of comments and escapes in values.
/// </summary>
public sealed class TranslationLookupTests : TempDirectoryTestBase
{
    private readonly string _dir;

    public TranslationLookupTests()
    {
        _dir = Dir("lang");
        Write(_dir, "app.en-US.ini", """
            [MainLanguage]
            WelcomeMessage=Welcome
            WindowCaptureMode.Auto=Automatic
            Title=Main title
            Greeting=Hello {0}, you have {1} new screenshots
            Broken=Hello {0
            Shared=from main

            [Editor]
            Title=Editor title
            Undo=Undo {0}
            Shared=from editor
            """);
        Write(_dir, "app.imgur.en-US.ini", """
            [Imgur]
            History=Imgur history
            Shared=from imgur
            """);
    }

    private LanguageConfig Build(TestLanguageListener? listener = null)
    {
        var builder = LanguageConfigRegistry.ForFile("app")
            .AddSearchPath(_dir)
            .WithBaseLanguage("en-US")
            // The module section is registered first: host sections are still searched first
            .RegisterSection<IImgurLanguage>(new ImgurLanguageImpl())
            .RegisterSection<IMainLanguage>(new MainLanguageImpl())
            .RegisterSection<IEditorLanguage>(new EditorLanguageImpl());
        if (listener != null) builder.AddListener(listener);
        return builder.Build();
    }

    [Theory]
    [InlineData("WelcomeMessage", "Welcome")]
    [InlineData("welcome_message", "Welcome")]                  // normal key normalisation
    [InlineData("Undo", "Undo {0}")]                            // only in the second host section
    [InlineData("WindowCaptureMode.Auto", "Automatic")]         // dots are kept; prefix is no module/section
    [InlineData("windowcapturemode.auto", "Automatic")]
    [InlineData("imgur.history", "Imgur history")]              // module prefix
    [InlineData("Imgur.History", "Imgur history")]
    [InlineData("History", "Imgur history")]                    // only in the module section
    [InlineData("Editor.Title", "Editor title")]                // section prefix
    [InlineData("MainLanguage.Title", "Main title")]
    [InlineData("Title", "Main title")]                         // first registered host section wins
    [InlineData("Shared", "from main")]                         // host sections before module sections
    [InlineData("imgur.Shared", "from imgur")]
    public void TryGetTranslation_FindsKeyWithoutKnowingTheSection(string key, string expected)
    {
        using var config = Build();

        Assert.True(config.TryGetTranslation(key, out var value));
        Assert.Equal(expected, value);
        Assert.Equal(expected, config.GetTranslation(key));
    }

    [Theory]
    [InlineData("Missing")]
    [InlineData("imgur.WelcomeMessage")]   // a module prefix only searches that module
    [InlineData("Editor.Greeting")]
    [InlineData("")]
    [InlineData("  ")]
    public void TryGetTranslation_MissingKey_ReturnsFalse(string key)
    {
        using var config = Build();

        Assert.False(config.TryGetTranslation(key, out var value));
        Assert.Null(value);
    }

    [Fact]
    public void GetTranslation_MissingKey_ReturnsSentinelAndReports()
    {
        var listener = new TestLanguageListener();
        using var config = Build(listener);

        Assert.Equal("###imgur.unknown###", config.GetTranslation("imgur.unknown"));
        Assert.Equal((null, "imgur.unknown"), Assert.Single(listener.NotFound));
    }

    [Fact]
    public void Registry_TryGetTranslation_SingleConfig()
    {
        using var config = Build();

        Assert.True(LanguageConfigRegistry.TryGetTranslation("imgur.history", out var value));
        Assert.Equal("Imgur history", value);
        Assert.True(LanguageConfigRegistry.TryGetTranslation("app.ini", "WelcomeMessage", out value));
        Assert.Equal("Welcome", value);
        Assert.Equal("###nope###", LanguageConfigRegistry.GetTranslation("nope"));
    }

    [Fact]
    public void Registry_TryGetTranslation_WithoutConfig_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => LanguageConfigRegistry.TryGetTranslation("key", out _));
    }

    [Fact]
    public void LateSection_IsFoundByTryGetTranslation()
    {
        Write(_dir, "app.box.en-US.ini", "[Box]\nUpload=Upload to Box");
        using var config = LanguageConfigBuilder.ForBasename("app")
            .AddSearchPath(_dir)
            .WithBaseLanguage("en-US")
            .AllowLateSectionRegistration()
            .RegisterSection<IMainLanguage>(new MainLanguageImpl())
            .Build();
        Assert.False(config.TryGetTranslation("box.upload", out _));

        config.RegisterSection<IBoxLanguage>(new BoxLanguageImpl());

        Assert.Equal("Upload to Box", config.GetTranslation("box.upload"));
    }

    // ── Format never throws ───────────────────────────────────────────────────

    [Fact]
    public void Format_FormatsTranslation()
    {
        using var config = Build();
        var main = config.GetSection<IMainLanguage>();

        Assert.Equal("Hello Robin, you have 3 new screenshots", ((LanguageSectionBase)main).Format("Greeting", "Robin", 3));
    }

    [Fact]
    public void Format_MissingKey_ReturnsSentinelAndReports()
    {
        var listener = new TestLanguageListener();
        using var config = Build(listener);
        var main = (LanguageSectionBase)config.GetSection<IMainLanguage>();

        Assert.Equal("###NotThere###", main.Format("NotThere", 1));
        Assert.Equal(("MainLanguage", "NotThere"), Assert.Single(listener.NotFound));
    }

    [Fact]
    public void Format_BadFormatString_ReturnsUnformattedTextAndReports()
    {
        var listener = new TestLanguageListener();
        using var config = Build(listener);
        var main = (LanguageSectionBase)config.GetSection<IMainLanguage>();

        Assert.Equal("Hello {0", main.Format("Broken", "Robin"));
        var failure = Assert.Single(listener.FormatFailures);
        Assert.Equal("MainLanguage", failure.Section);
        Assert.Equal("Broken", failure.Key);
        Assert.IsType<FormatException>(failure.Exception);
    }

    [Fact]
    public void Format_TooFewOrNullArguments_ReturnsUnformattedText()
    {
        var listener = new TestLanguageListener();
        using var config = Build(listener);
        var main = (LanguageSectionBase)config.GetSection<IMainLanguage>();

        Assert.Equal("Hello {0}, you have {1} new screenshots", main.Format("Greeting", "Robin"));
        Assert.Equal("Hello {0}, you have {1} new screenshots", main.Format("Greeting", null!));
        Assert.Equal(2, listener.FormatFailures.Count);
    }

    [Fact]
    public void Format_SectionNotRegistered_NeverThrows()
    {
        var section = new MainLanguageImpl();
        section.SetTranslation("broken", "{0");

        Assert.Equal("{0", section.Format("Broken", 1));
        Assert.Equal("###Missing###", section.Format("Missing"));
    }

    // ── Comments and escapes ──────────────────────────────────────────────────

    [Fact]
    public void Values_WithSemicolonAndHash_AreKept_OnlyWholeLinesAreComments()
    {
        Write(_dir, "values.en-US.ini", """
            ; a comment
            # another comment
            [MainLanguage]
              ; an indented comment
            WelcomeMessage=Hello; world # not a comment
            ErrorTitle = #1 error; really
            ;SaveButton=commented out
            #CancelButton=commented out
            MultiLine=Line one\nLine two\tTabbed
            BackslashValue=C:\\temp\\new
            TabValue=keep \x unknown escapes and a trailing backslash \
            """);
        var main = new MainLanguageImpl();
        using var config = LanguageConfigBuilder.ForBasename("values")
            .AddSearchPath(_dir)
            .WithBaseLanguage("en-US")
            .RegisterSection<IMainLanguage>(main)
            .Build();

        Assert.Equal("Hello; world # not a comment", main.WelcomeMessage);
        Assert.Equal("#1 error; really", main.ErrorTitle);
        Assert.Equal("###SaveButton###", main.SaveButton);
        Assert.Equal("###CancelButton###", main.CancelButton);
        Assert.Equal("Line one\nLine two\tTabbed", main.MultiLine);
        Assert.Equal(@"C:\temp\new", main.BackslashValue);
        Assert.Equal(@"keep \x unknown escapes and a trailing backslash \", main.TabValue);
    }

    [Theory]
    [InlineData(@"a\nb", "a\nb")]
    [InlineData(@"a\tb", "a\tb")]
    [InlineData(@"a\\b", @"a\b")]
    [InlineData(@"a\\nb", @"a\nb")]       // an escaped backslash followed by n is not a newline
    [InlineData(@"a\rb", @"a\rb")]        // unsupported escapes are kept
    [InlineData(@"end\", @"end\")]
    [InlineData("plain", "plain")]
    public void UnescapeValue_HandlesSupportedEscapes(string raw, string expected)
    {
        Assert.Equal(expected, LanguageConfig.UnescapeValue(raw));
    }
}
