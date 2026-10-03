// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Globalization;
using Dapplo.Ini.Internationalization.Configuration;

namespace Dapplo.Ini.Internationalization.Tests;

/// <summary>
/// <see cref="LanguageConfig.GetLanguages"/> / <see cref="LanguageConfig.GetAvailableLanguages"/>: the
/// <c>[__language__]</c> description and which files define a language.
/// </summary>
public sealed class AvailableLanguagesTests : TempDirectoryTestBase
{
    private readonly string _dir;

    public AvailableLanguagesTests()
    {
        _dir = Dir("lang");
        Write(_dir, "app.en-US.ini", "[MainLanguage]\nWelcomeMessage=Welcome");
        Write(_dir, "app.de-DE.ini", "[MainLanguage]\nWelcomeMessage=Willkommen");
        Write(_dir, "app.de-x-franconia.ini", """
            ; Description of the language itself, never a translation
            [__language__]
            Description=Fränkisch

            [MainLanguage]
            WelcomeMessage=Grüß Gott
            """);
        // Module files: never add a language of their own
        Write(_dir, "app.imgur.en-US.ini", "[Imgur]\nHistory=History");
        Write(_dir, "app.imgur.fr-FR.ini", "[Imgur]\nHistory=Historique");
        // A module file without tag, whose module name looks like a language tag
        Write(_dir, "app.box.ini", "[Box]\nUpload=Upload");
        // Files of other applications or without a tag
        Write(_dir, "other.it-IT.ini", "[MainLanguage]\nWelcomeMessage=Benvenuto");
        Write(_dir, "app.imgur.ini", "[Imgur]\nHistory=History");
    }

    private LanguageConfig Build()
    {
        var builder = LanguageConfigBuilder.ForBasename("app")
            .AddSearchPath(_dir)
            .WithBaseLanguage("en-US")
            .RegisterSection<IMainLanguage>(new MainLanguageImpl())
            .RegisterSection<IImgurLanguage>(new ImgurLanguageImpl())
            .RegisterSection<IBoxLanguage>(new BoxLanguageImpl());
        return builder.Build();
    }

    [Fact]
    public void GetLanguages_OnlyBaseFilesDefineLanguages_SortedByTag()
    {
        using var config = Build();

        var languages = config.GetLanguages();

        Assert.Equal(new[] { "de-DE", "de-x-franconia", "en-US" }, languages.Select(l => l.Ietf));
        Assert.All(languages, l => Assert.True(l.HasBaseFile));
    }

    [Fact]
    public void GetLanguages_DescriptionFromFile_NativeNameFromCulture()
    {
        using var config = Build();

        var languages = config.GetLanguages().ToDictionary(l => l.Ietf);

        var franconian = languages["de-x-franconia"];
        Assert.Equal("Fränkisch", franconian.Description);
        Assert.Null(franconian.NativeName);
        Assert.Equal("Fränkisch", franconian.DisplayName);

        var german = languages["de-DE"];
        Assert.Null(german.Description);
        Assert.Equal(CultureInfo.GetCultureInfo("de-DE").NativeName, german.NativeName);
        Assert.Equal(german.NativeName, german.DisplayName);
    }

    [Fact]
    public void GetAvailableLanguages_UsesDescriptionThenNativeNameThenTag()
    {
        Write(_dir, "app.de-x-franconia.ini", "[MainLanguage]\nWelcomeMessage=Grüß Gott");   // no description
        Write(_dir, "app.fr-x-ch.ini", "[__language__]\nDescription=Romand");
        using var config = Build();

        var languages = config.GetAvailableLanguages().ToDictionary(l => l.Ietf, l => l.NativeName);

        Assert.Equal("de-x-franconia", languages["de-x-franconia"]);
        Assert.Equal("Romand", languages["fr-x-ch"]);
        Assert.Equal(CultureInfo.GetCultureInfo("en-US").NativeName, languages["en-US"]);
    }

    [Fact]
    public void LanguageSection_IsNeverRoutedToARegisteredSection()
    {
        var main = new MainLanguageImpl();
        using var config = LanguageConfigBuilder.ForBasename("app")
            .AddSearchPath(_dir)
            .WithBaseLanguage("en-US")
            .WithCurrentLanguage("de-x-franconia")
            .RegisterSection<IMainLanguage>(main)
            .Build();

        Assert.Equal("Grüß Gott", main.WelcomeMessage);
        Assert.False(main.ContainsKey("Description"));
        Assert.False(config.TryGetTranslation("Description", out _));
        Assert.False(config.TryGetTranslation("__language__.Description", out _));
    }

    [Fact]
    public void GetLanguages_OnlyModuleSections_ModuleFilesOfRegisteredModulesDefineLanguages()
    {
        using var config = LanguageConfigBuilder.ForBasename("app")
            .AddSearchPath(_dir)
            .WithBaseLanguage("en-US")
            .RegisterSection<IImgurLanguage>(new ImgurLanguageImpl())
            .Build();

        var languages = config.GetLanguages();

        Assert.Equal(new[] { "en-US", "fr-FR" }, languages.Select(l => l.Ietf));
        Assert.False(languages.Single(l => l.Ietf == "fr-FR").HasBaseFile);
        Assert.True(languages.Single(l => l.Ietf == "en-US").HasBaseFile);
    }

    [Theory]
    [InlineData("app.de-DE.ini", null, "de-DE")]
    [InlineData("app.de-x-franconia.ini", null, "de-x-franconia")]
    [InlineData("app.zh-Hant-TW.ini", null, "zh-Hant-TW")]
    [InlineData("app.es-419.ini", null, "es-419")]
    [InlineData("app.imgur.de-DE.ini", "imgur", "de-DE")]
    [InlineData("APP.Imgur.fr-QC.ini", "Imgur", "fr-QC")]
    public void TryParseLanguageFileName_RecognisesLanguageFiles(string fileName, string? module, string ietf)
    {
        using var config = Build();
        var modules = new HashSet<string>(new[] { "imgur", "box" }, StringComparer.OrdinalIgnoreCase);

        Assert.True(config.TryParseLanguageFileName(Path.Combine(_dir, fileName), modules, out var actualModule, out var actualIetf));
        Assert.Equal(module, actualModule);
        Assert.Equal(ietf, actualIetf);
    }

    [Theory]
    [InlineData("app.imgur.ini")]     // module without tag: "imgur" is not a tag
    [InlineData("app.box.ini")]       // three letters, but a registered module name
    [InlineData("other.de-DE.ini")]   // another basename
    [InlineData("app.ini")]
    [InlineData("app..ini")]
    [InlineData("app.imgur..ini")]
    [InlineData("application.de-DE.ini")]
    public void TryParseLanguageFileName_RejectsOtherFiles(string fileName)
    {
        using var config = Build();
        var modules = new HashSet<string>(new[] { "imgur", "box" }, StringComparer.OrdinalIgnoreCase);

        Assert.False(config.TryParseLanguageFileName(Path.Combine(_dir, fileName), modules, out _, out _));
    }
}
