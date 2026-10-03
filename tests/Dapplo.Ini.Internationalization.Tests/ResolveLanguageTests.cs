// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Dapplo.Ini.Internationalization.Configuration;

namespace Dapplo.Ini.Internationalization.Tests;

/// <summary>
/// <see cref="LanguageConfig.ResolveLanguage"/> and <see cref="LanguageConfigBuilder.ResolveLanguages"/>.
/// </summary>
public sealed class ResolveLanguageTests : TempDirectoryTestBase
{
    [Theory]
    // 1. Exact match, case-insensitive, returned in the available spelling
    [InlineData("de-DE", "de-DE")]
    [InlineData("DE-de", "de-DE")]
    // 2. Installer legacy values without hyphen, or with underscore
    [InlineData("ptBR", "pt-BR")]
    [InlineData("zhCN", "zh-CN")]
    [InlineData("pt_BR", "pt-BR")]
    // 3. Parent or child with the same language subtag
    [InlineData("de", "de-DE")]               // default specific culture
    [InlineData("de-AT", "de-DE")]            // de-DE is the specific culture of the parent "de"
    [InlineData("zh-Hant-TW", "zh-TW")]       // same region
    [InlineData("zh-TW", "zh-TW")]
    [InlineData("ptPT", "pt-BR")]             // legacy value, only pt-BR exists
    [InlineData("nl", "nl-NL")]
    // 4. Otherwise the base language
    [InlineData("xx", "en-US")]
    [InlineData("ja-JP", "en-US")]
    [InlineData("", "en-US")]
    public void ResolveLanguage_FindsClosestAvailableLanguage(string requested, string expected)
    {
        var available = new[] { "de-DE", "de-x-franconia", "en-US", "nl-NL", "pt-BR", "zh-CN", "zh-TW" };

        Assert.Equal(expected, LanguageConfig.ResolveLanguage(requested, available, "en-US"));
    }

    [Fact]
    public void ResolveLanguage_PrefersAvailableParent()
    {
        var available = new[] { "de", "de-DE", "en-US" };

        Assert.Equal("de", LanguageConfig.ResolveLanguage("de-AT", available, "en-US"));
        Assert.Equal("de", LanguageConfig.ResolveLanguage("de-CH", available, "en-US"));
    }

    [Theory]
    [InlineData("zh-HK")]
    [InlineData("zh-MO")]
    [InlineData("zh-Hant")]
    [InlineData("zh-Hant-HK")]
    public void ResolveLanguage_TraditionalChinese_IsNotResolvedToSimplified(string requested)
    {
        Assert.Equal("zh-TW", LanguageConfig.ResolveLanguage(requested, new[] { "zh-CN", "zh-TW", "en-US" }, "en-US"));
    }

    [Theory]
    [InlineData("zh-SG")]
    [InlineData("zh-Hans")]
    [InlineData("zh")]
    public void ResolveLanguage_SimplifiedChinese(string requested)
    {
        Assert.Equal("zh-CN", LanguageConfig.ResolveLanguage(requested, new[] { "zh-CN", "zh-TW", "en-US" }, "en-US"));
    }

    [Fact]
    public void ResolveLanguage_RegionMatch_RespectsTheScript()
    {
        var available = new[] { "en-US", "sr-Cyrl-RS", "sr-Latn", "zh-CN", "zh-HK" };

        Assert.Equal("sr-Latn", LanguageConfig.ResolveLanguage("sr-Latn-RS", available, "en-US"));
        Assert.Equal("zh-CN", LanguageConfig.ResolveLanguage("zh-Hans-HK", available, "en-US"));
        Assert.Equal("zh-HK", LanguageConfig.ResolveLanguage("zh-Hant-HK", available, "en-US"));
    }

    [Fact]
    public void ResolveLanguage_PrefersTagWithSameRegion()
    {
        Assert.Equal("sr-RS", LanguageConfig.ResolveLanguage("sr-Latn-RS", new[] { "sr-ME", "sr-RS" }, "en-US"));
    }

    [Fact]
    public void ResolveLanguage_PrivateUseVariant_IsOnlyUsedWhenRequestedExactly()
    {
        var available = new[] { "de-x-franconia", "en-US" };

        Assert.Equal("en-US", LanguageConfig.ResolveLanguage("de-DE", available, "en-US"));
        Assert.Equal("en-US", LanguageConfig.ResolveLanguage("de", available, "en-US"));
        Assert.Equal("de-x-franconia", LanguageConfig.ResolveLanguage("DE-X-FRANCONIA", available, "en-US"));
        Assert.Equal("de-DE", LanguageConfig.ResolveLanguage("de-AT", new[] { "de-AT-x-vienna", "de-DE" }, "en-US"));
    }

    [Fact]
    public void ResolveLanguage_PrefersDefaultSpecificCultureOverOtherChildren()
    {
        Assert.Equal("de-DE", LanguageConfig.ResolveLanguage("de", new[] { "de-AT", "de-CH", "de-DE" }, "en-US"));
    }

    [Fact]
    public void ResolveLanguage_FallsBackToASibling()
    {
        Assert.Equal("de-CH", LanguageConfig.ResolveLanguage("de-AT", new[] { "de-CH", "en-US" }, "en-US"));
    }

    [Fact]
    public void ResolveLanguage_NoLanguagesAvailable_ReturnsBaseLanguage()
    {
        Assert.Equal("en-US", LanguageConfig.ResolveLanguage("de-DE", Array.Empty<string>(), "en-US"));
    }

    private LanguageConfigBuilder CreateBuilder(TestLanguageListener listener, bool resolve = true)
    {
        var dir = Dir("lang");
        Write(dir, "app.en-US.ini", "[MainLanguage]\nWelcomeMessage=Welcome");
        Write(dir, "app.de-DE.ini", "[MainLanguage]\nWelcomeMessage=Willkommen");
        Write(dir, "app.pt-BR.ini", "[MainLanguage]\nWelcomeMessage=Bem-vindo");
        var builder = LanguageConfigBuilder.ForBasename("app")
            .AddSearchPath(dir)
            .WithBaseLanguage("en-US")
            .AddListener(listener);
        return resolve ? builder.ResolveLanguages() : builder;
    }

    [Fact]
    public void ResolveLanguages_WithCurrentLanguage_UsesResolvedTag()
    {
        var listener = new TestLanguageListener();
        var main = new MainLanguageImpl();
        using var config = CreateBuilder(listener).WithCurrentLanguage("de").RegisterSection<IMainLanguage>(main).Build();

        Assert.Equal("de-DE", config.CurrentLanguage);
        Assert.Equal("de", config.RequestedLanguage);
        Assert.Equal("Willkommen", main.WelcomeMessage);
        Assert.Equal(("de", "de-DE"), Assert.Single(listener.Resolved));
    }

    [Fact]
    public async Task ResolveLanguages_SetLanguage_UsesResolvedTag()
    {
        var listener = new TestLanguageListener();
        var main = new MainLanguageImpl();
        using var config = CreateBuilder(listener).RegisterSection<IMainLanguage>(main).Build();

        config.SetLanguage("ptBR");
        Assert.Equal("pt-BR", config.CurrentLanguage);
        Assert.Equal("Bem-vindo", main.WelcomeMessage);

        await config.SetLanguageAsync("xx-YY");
        Assert.Equal("en-US", config.CurrentLanguage);
        Assert.Equal("xx-YY", config.RequestedLanguage);
        Assert.Equal("Welcome", main.WelcomeMessage);

        Assert.Equal(new[] { ("en-US", "en-US"), ("ptBR", "pt-BR"), ("xx-YY", "en-US") }, listener.Resolved);
    }

    [Fact]
    public void WithoutResolveLanguages_RequestedTagIsUsedAsIs()
    {
        var listener = new TestLanguageListener();
        var main = new MainLanguageImpl();
        using var config = CreateBuilder(listener, resolve: false).WithCurrentLanguage("de").RegisterSection<IMainLanguage>(main).Build();

        Assert.Equal("de", config.CurrentLanguage);
        Assert.Equal("Welcome", main.WelcomeMessage);   // only the fallback is loaded, as before
        Assert.Empty(listener.Resolved);
        // ResolveLanguage itself is always available
        Assert.Equal("de-DE", config.ResolveLanguage("de"));
    }
}
