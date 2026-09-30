// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Dapplo.Ini.Internationalization;
using Dapplo.Ini.Internationalization.Configuration;

namespace Dapplo.Ini.Internationalization.Tests;

/// <summary>Tests for the language fallback chain and value trimming.</summary>
public sealed class LanguageLoadingFixesTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

    public LanguageLoadingFixesTests()
    {
        Directory.CreateDirectory(_dir);
        LanguageConfigRegistry.Clear();
    }

    public void Dispose()
    {
        LanguageConfigRegistry.Clear();
        Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void GetParentLanguages_ReturnsEveryParentLeastSpecificFirst()
    {
        Assert.Equal(new[] { "zh", "zh-Hant" }, LanguageConfig.GetParentLanguages("zh-Hant-TW"));
        Assert.Equal(new[] { "de" }, LanguageConfig.GetParentLanguages("de-DE"));
        Assert.Empty(LanguageConfig.GetParentLanguages("de"));
    }

    [Fact]
    public void LoadChain_IncludesParentsOfFallbackAndRequestedLanguage_Once()
    {
        using var defaultChain = LanguageConfigBuilder.ForBasename("chain-a").AddSearchPath(_dir)
            .WithBaseLanguage("de-DE").Create();
        Assert.Equal(new[] { "de", "de-DE" }, defaultChain.GetLoadChain("de-DE"));
        Assert.Equal(new[] { "de", "de-DE", "de-AT" }, defaultChain.GetLoadChain("de-AT"));

        LanguageConfigRegistry.Clear();
        using var explicitFallback = LanguageConfigBuilder.ForBasename("chain-b").AddSearchPath(_dir)
            .WithBaseLanguage("de-DE").UseFallbackLanguage("en-US").Create();
        Assert.Equal(new[] { "en", "en-US", "zh", "zh-Hant", "zh-Hant-TW" }, explicitFallback.GetLoadChain("zh-Hant-TW"));
    }

    [Fact]
    public void UseFallbackLanguage_FillsMissingKeysFromThatLanguage()
    {
        File.WriteAllText(Path.Combine(_dir, "fb.de-DE.ini"), "[MainLanguage]\nWelcomeMessage = Willkommen");
        File.WriteAllText(Path.Combine(_dir, "fb.en-US.ini"), "[MainLanguage]\nWelcomeMessage = Welcome\nErrorTitle = Error");

        var section = new MainLanguageImpl();
        using var config = LanguageConfigBuilder.ForBasename("fb")
            .AddSearchPath(_dir)
            .WithBaseLanguage("de-DE")
            .UseFallbackLanguage("en-US")
            .RegisterSection<IMainLanguage>(section)
            .Build();

        Assert.Equal("Willkommen", section.WelcomeMessage);
        Assert.Equal("Error", section.ErrorTitle);
    }

    [Fact]
    public void Values_WithSpacesAroundTheEqualsSign_AreTrimmed_AndMiddleParentIsUsed()
    {
        File.WriteAllText(Path.Combine(_dir, "chain.en-US.ini"), "[MainLanguage]\nWelcomeMessage = Welcome\nErrorTitle = Error");
        File.WriteAllText(Path.Combine(_dir, "chain.zh-Hant.ini"), "[MainLanguage]\nErrorTitle = from zh-Hant");
        File.WriteAllText(Path.Combine(_dir, "chain.zh-Hant-TW.ini"), "[MainLanguage]\nWelcomeMessage =   from zh-Hant-TW");

        var section = new MainLanguageImpl();
        using var config = LanguageConfigBuilder.ForBasename("chain")
            .AddSearchPath(_dir)
            .WithBaseLanguage("en-US")
            .RegisterSection<IMainLanguage>(section)
            .Build();
        config.SetLanguage("zh-Hant-TW");

        Assert.Equal("from zh-Hant-TW", section.WelcomeMessage);
        Assert.Equal("from zh-Hant", section.ErrorTitle);
    }
}
