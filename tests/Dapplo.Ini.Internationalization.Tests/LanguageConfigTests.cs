// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Dapplo.Ini.Internationalization.Configuration;

namespace Dapplo.Ini.Internationalization.Tests;

/// <summary>
/// Tests for the <see cref="LanguageConfigBuilder"/> and <see cref="LanguageConfig"/> functionality.
/// </summary>
public sealed class LanguageConfigTests
{
    // Resolve the language file directory relative to the test assembly output location.
    private static readonly string LangDir =
        Path.Combine(AppContext.BaseDirectory, "Lang");

    // ── Load (base language) ──────────────────────────────────────────────────

    [Fact]
    public void Build_BaseLanguage_LoadsTranslations()
    {
        var section = new MainLanguageImpl();
        using var config = LanguageConfigBuilder.Create("testapp")
            .WithDirectory(LangDir)
            .WithBaseLanguage("en-US")
            .AddSection<IMainLanguage>(section)
            .Build();

        Assert.Equal("Welcome to the application!", section.WelcomeMessage);
        Assert.Equal("Error", section.ErrorTitle);
        Assert.Equal("Save", section.SaveButton);
        Assert.Equal("Cancel", section.CancelButton);
    }

    // ── Key normalization (underscores/dashes in file keys) ──────────────────

    [Fact]
    public void Build_KeyWithUnderscores_MappedToProperty()
    {
        // Save_Button in the file should match property "SaveButton"
        var section = new MainLanguageImpl();
        using var config = LanguageConfigBuilder.Create("testapp")
            .WithDirectory(LangDir)
            .WithBaseLanguage("en-US")
            .AddSection<IMainLanguage>(section)
            .Build();

        Assert.Equal("Save", section.SaveButton);
        Assert.Equal("Cancel", section.CancelButton);
    }

    // ── Escape sequences ──────────────────────────────────────────────────────

    [Fact]
    public void Build_EscapeSequences_AreUnescaped()
    {
        var section = new MainLanguageImpl();
        using var config = LanguageConfigBuilder.Create("testapp")
            .WithDirectory(LangDir)
            .WithBaseLanguage("en-US")
            .AddSection<IMainLanguage>(section)
            .Build();

        Assert.Equal("Line one\nLine two", section.MultiLine);
        Assert.Equal("Column1\tColumn2", section.TabValue);
        Assert.Equal("Path\\file", section.BackslashValue);
    }

    // ── Missing key sentinel ──────────────────────────────────────────────────

    [Fact]
    public void Build_MissingKey_ReturnsSentinel()
    {
        // Use a temp directory with an empty language file so nothing is loaded.
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            File.WriteAllText(Path.Combine(tempDir, "app.en-US.ini"), "");

            var section = new MainLanguageImpl();
            using var config = LanguageConfigBuilder.Create("app")
                .WithDirectory(tempDir)
                .WithBaseLanguage("en-US")
                .AddSection<IMainLanguage>(section)
                .Build();

            Assert.Equal("###WelcomeMessage###", section.WelcomeMessage);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    // ── Language switching ────────────────────────────────────────────────────

    [Fact]
    public void SetLanguage_SwitchesToNewLanguage()
    {
        var section = new MainLanguageImpl();
        using var config = LanguageConfigBuilder.Create("testapp")
            .WithDirectory(LangDir)
            .WithBaseLanguage("en-US")
            .WithCurrentLanguage("en-US")
            .AddSection<IMainLanguage>(section)
            .Build();

        Assert.Equal("Welcome to the application!", section.WelcomeMessage);

        config.SetLanguage("de-DE");

        Assert.Equal("Willkommen bei der Anwendung!", section.WelcomeMessage);
    }

    // ── Progressive fallback ──────────────────────────────────────────────────

    [Fact]
    public void SetLanguage_ProgressiveFallback_LoadsParentCulture()
    {
        // de.ini has WelcomeMessage=Willkommen!
        // de-DE.ini overrides WelcomeMessage=Willkommen bei der Anwendung!
        // So after loading de-DE we expect the overridden value.
        var section = new MainLanguageImpl();
        using var config = LanguageConfigBuilder.Create("testapp")
            .WithDirectory(LangDir)
            .WithBaseLanguage("en-US")
            .AddSection<IMainLanguage>(section)
            .UseFallback()
            .Build();

        config.SetLanguage("de-DE");
        Assert.Equal("Willkommen bei der Anwendung!", section.WelcomeMessage);
    }

    // ── Fallback for missing keys ─────────────────────────────────────────────

    [Fact]
    public void SetLanguage_WithFallback_MissingKeyUsesBaseLanguage()
    {
        // de-DE.ini does not contain CancelButton — should fall back to en-US value "Cancel".
        var section = new MainLanguageImpl();
        using var config = LanguageConfigBuilder.Create("testapp")
            .WithDirectory(LangDir)
            .WithBaseLanguage("en-US")
            .AddSection<IMainLanguage>(section)
            .UseFallback()
            .Build();

        config.SetLanguage("de-DE");

        // CancelButton missing in de-DE → falls back to base language (en-US) value
        Assert.Equal("Cancel", section.CancelButton);
    }

    // ── Module sections ───────────────────────────────────────────────────────

    [Fact]
    public void Build_ModuleSection_LoadsFromModuleFile()
    {
        var coreSection = new CoreLanguageImpl();
        using var config = LanguageConfigBuilder.Create("testapp")
            .WithDirectory(LangDir)
            .WithBaseLanguage("en-US")
            .AddSection<ICoreLanguage>(coreSection)
            .Build();

        Assert.Equal("Core Module", coreSection.CoreTitle);
        Assert.Equal("Ready", coreSection.CoreStatus);
    }

    [Fact]
    public void SetLanguage_ModuleSection_SwitchesCorrectly()
    {
        var coreSection = new CoreLanguageImpl();
        using var config = LanguageConfigBuilder.Create("testapp")
            .WithDirectory(LangDir)
            .WithBaseLanguage("en-US")
            .AddSection<ICoreLanguage>(coreSection)
            .Build();

        config.SetLanguage("de-DE");

        Assert.Equal("Kernmodul", coreSection.CoreTitle);
        Assert.Equal("Bereit", coreSection.CoreStatus);
    }

    // ── Multiple sections in one config ──────────────────────────────────────

    [Fact]
    public void Build_MultipleSections_AllLoaded()
    {
        var main = new MainLanguageImpl();
        var core = new CoreLanguageImpl();

        using var config = LanguageConfigBuilder.Create("testapp")
            .WithDirectory(LangDir)
            .WithBaseLanguage("en-US")
            .AddSection<IMainLanguage>(main)
            .AddSection<ICoreLanguage>(core)
            .Build();

        Assert.Equal("Welcome to the application!", main.WelcomeMessage);
        Assert.Equal("Core Module", core.CoreTitle);
    }

    // ── GetSection ────────────────────────────────────────────────────────────

    [Fact]
    public void GetSection_ReturnsRegisteredSection()
    {
        var section = new MainLanguageImpl();
        using var config = LanguageConfigBuilder.Create("testapp")
            .WithDirectory(LangDir)
            .WithBaseLanguage("en-US")
            .AddSection<IMainLanguage>(section)
            .Build();

        var retrieved = config.GetSection<IMainLanguage>();
        Assert.Same(section, retrieved);
    }

    [Fact]
    public void GetSection_UnregisteredType_Throws()
    {
        using var config = LanguageConfigBuilder.Create("testapp")
            .WithDirectory(LangDir)
            .WithBaseLanguage("en-US")
            .Build();

        Assert.Throws<InvalidOperationException>(() => config.GetSection<IMainLanguage>());
    }

    // ── GetAvailableLanguages ─────────────────────────────────────────────────

    [Fact]
    public void GetAvailableLanguages_ReturnsDiscoveredLanguages()
    {
        var section = new MainLanguageImpl();
        using var config = LanguageConfigBuilder.Create("testapp")
            .WithDirectory(LangDir)
            .WithBaseLanguage("en-US")
            .AddSection<IMainLanguage>(section)
            .Build();

        var langs = config.GetAvailableLanguages();

        // We expect at least en-US and de-DE (and de)
        var ietfTags = langs.Select(l => l.Ietf).ToList();
        Assert.Contains("en-US", ietfTags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("de-DE", ietfTags, StringComparer.OrdinalIgnoreCase);

        // Each entry should have a non-empty native name
        foreach (var (ietf, nativeName) in langs)
        {
            Assert.False(string.IsNullOrEmpty(nativeName), $"NativeName should not be empty for '{ietf}'");
        }
    }

    // ── LanguageChanged event ─────────────────────────────────────────────────

    [Fact]
    public void SetLanguage_RaisesLanguageChangedEvent()
    {
        var section = new MainLanguageImpl();
        using var config = LanguageConfigBuilder.Create("testapp")
            .WithDirectory(LangDir)
            .WithBaseLanguage("en-US")
            .AddSection<IMainLanguage>(section)
            .Build();

        bool eventFired = false;
        config.LanguageChanged += (_, _) => eventFired = true;

        config.SetLanguage("de-DE");

        Assert.True(eventFired);
    }

    // ── Async build ───────────────────────────────────────────────────────────

    [Fact]
    public async Task BuildAsync_LoadsTranslations()
    {
        var section = new MainLanguageImpl();
        using var config = await LanguageConfigBuilder.Create("testapp")
            .WithDirectory(LangDir)
            .WithBaseLanguage("en-US")
            .AddSection<IMainLanguage>(section)
            .BuildAsync();

        Assert.Equal("Welcome to the application!", section.WelcomeMessage);
    }

    // ── IReadOnlyDictionary<string,string> support ────────────────────────────

    [Fact]
    public void DictionaryInterface_IndexerReturnsTranslation()
    {
        var section = new DictionaryLanguageImpl();
        using var config = LanguageConfigBuilder.Create("testapp")
            .WithDirectory(LangDir)
            .WithBaseLanguage("en-US")
            .AddSection<IDictionaryLanguage>(section)
            .Build();

        // Access via IReadOnlyDictionary indexer (uses normalized key lookup)
        IDictionaryLanguage asDict = section;
        Assert.Equal("Welcome to the application!", asDict["WelcomeMessage"]);
        Assert.Equal("Welcome to the application!", asDict["welcome_message"]);
        Assert.Equal("Welcome to the application!", asDict["welcome-message"]);
    }

    [Fact]
    public void DictionaryInterface_ContainsKey_Works()
    {
        var section = new DictionaryLanguageImpl();
        using var config = LanguageConfigBuilder.Create("testapp")
            .WithDirectory(LangDir)
            .WithBaseLanguage("en-US")
            .AddSection<IDictionaryLanguage>(section)
            .Build();

        IDictionaryLanguage asDict = section;
        Assert.True(asDict.ContainsKey("WelcomeMessage"));
        Assert.True(asDict.ContainsKey("welcomemessage"));
        Assert.False(asDict.ContainsKey("NonExistentKey"));
    }

    // ── Builder validation ────────────────────────────────────────────────────

    [Fact]
    public void Build_WithoutBaseLanguage_Throws()
    {
        Assert.Throws<InvalidOperationException>(() =>
            LanguageConfigBuilder.Create("testapp")
                .WithDirectory(LangDir)
                .Build());
    }

    [Fact]
    public void Build_SectionWithoutDirectory_Throws()
    {
        var section = new MainLanguageImpl();
        Assert.Throws<InvalidOperationException>(() =>
            LanguageConfigBuilder.Create("testapp")
                .WithBaseLanguage("en-US")
                // No WithDirectory, no per-section directory
                .AddSection<IMainLanguage>(section)
                .Build());
    }

    // ── SectionName ────────────────────────────────────────────────────────────

    [Fact]
    public void SectionName_ReflectsAttributeValue()
    {
        var main = new MainLanguageImpl();
        var core = new CoreLanguageImpl();

        Assert.Null(main.SectionName);
        Assert.Equal("core", core.SectionName);
    }

    // ── ILanguageSection is optional ──────────────────────────────────────────

    [Fact]
    public void Interface_WithoutILanguageSection_WorksCorrectly()
    {
        // IMainLanguage does NOT extend ILanguageSection — verify it still works end-to-end.
        var section = new MainLanguageImpl();
        using var config = LanguageConfigBuilder.Create("testapp")
            .WithDirectory(LangDir)
            .WithBaseLanguage("en-US")
            .AddSection<IMainLanguage>(section)
            .Build();

        Assert.Equal("Welcome to the application!", section.WelcomeMessage);
    }

    // ── ForBasename() factory aligned with IniConfigBuilder.ForFile() ─────────

    [Fact]
    public void ForBasename_CreatesBuilder()
    {
        var section = new MainLanguageImpl();
        using var config = LanguageConfigBuilder.ForBasename("testapp")
            .WithDirectory(LangDir)
            .WithBaseLanguage("en-US")
            .AddSection<IMainLanguage>(section)
            .Build();

        Assert.Equal("Welcome to the application!", section.WelcomeMessage);
    }

    // ── Section in main file (module keys inside [sectionName] block) ─────────

    [Fact]
    public void CoreSectionInMainFile_LoadsKeysFromSection()
    {
        // mergedapp.en-US.ini has WelcomeMessage + [core] section.
        // No dedicated mergedapp.core.en-US.ini exists.
        // ICoreLanguage should fall back to reading the [core] block in the main file.
        var main = new MainLanguageImpl();
        var core = new CoreLanguageImpl();

        using var config = LanguageConfigBuilder.ForBasename("mergedapp")
            .WithDirectory(LangDir)
            .WithBaseLanguage("en-US")
            .AddSection<IMainLanguage>(main)
            .AddSection<ICoreLanguage>(core)
            .Build();

        Assert.Equal("Welcome (merged)", main.WelcomeMessage);
        Assert.Equal("Core Module (merged)", core.CoreTitle);
        Assert.Equal("Ready (merged)", core.CoreStatus);
    }

    [Fact]
    public void MainFileWithSection_MainSectionReadsAllKeys()
    {
        // When SectionName is null (IMainLanguage), all keys are read from the file
        // including those outside [section] blocks.
        var main = new MainLanguageImpl();

        using var config = LanguageConfigBuilder.ForBasename("mergedapp")
            .WithDirectory(LangDir)
            .WithBaseLanguage("en-US")
            .AddSection<IMainLanguage>(main)
            .Build();

        Assert.Equal("Welcome (merged)", main.WelcomeMessage);
        Assert.Equal("Error (merged)", main.ErrorTitle);
    }

    [Fact]
    public void DedicatedFileHasPrecedenceOverSectionInMainFile()
    {
        // testapp has a dedicated testapp.core.en-US.ini file.
        // That file should be used in preference to any [core] section in testapp.en-US.ini.
        var core = new CoreLanguageImpl();

        using var config = LanguageConfigBuilder.Create("testapp")
            .WithDirectory(LangDir)
            .WithBaseLanguage("en-US")
            .AddSection<ICoreLanguage>(core)
            .Build();

        // Values from testapp.core.en-US.ini (not from a hypothetical [core] in testapp.en-US.ini)
        Assert.Equal("Core Module", core.CoreTitle);
        Assert.Equal("Ready", core.CoreStatus);
    }

    // ── Deferred / plugin loading (Create + AddSection + Load) ───────────────

    [Fact]
    public void Create_ThenPluginAddSection_ThenLoad_LoadsAllSections()
    {
        var main = new MainLanguageImpl();

        // Phase 1: host creates config without loading (aligned with IniConfigBuilder.Create)
        var config = LanguageConfigBuilder.ForBasename("testapp")
            .WithDirectory(LangDir)
            .WithBaseLanguage("en-US")
            .AddSection<IMainLanguage>(main)
            .Create();

        // Phase 2: plugin registers its own section
        var plugin = new PluginLanguageImpl();
        config.AddSection<IPluginLanguage>(plugin, LangDir);

        // Phase 3: host triggers loading
        config.Load();
        config.Dispose();

        Assert.Equal("Welcome to the application!", main.WelcomeMessage);
        Assert.Equal("Core Module", plugin.CoreTitle);
        Assert.Equal("Ready", plugin.CoreStatus);
    }

    [Fact]
    public void Create_WithoutLoad_SectionsHaveNoTranslations()
    {
        var main = new MainLanguageImpl();
        var config = LanguageConfigBuilder.Create("testapp")
            .WithDirectory(LangDir)
            .WithBaseLanguage("en-US")
            .AddSection<IMainLanguage>(main)
            .Create();

        // No Load() called — translations must not be present yet
        Assert.Equal("###WelcomeMessage###", main.WelcomeMessage);

        config.Dispose();
    }

    [Fact]
    public void AddSection_OnConfig_MakesItAvailableViaGetSection()
    {
        var config = LanguageConfigBuilder.Create("testapp")
            .WithDirectory(LangDir)
            .WithBaseLanguage("en-US")
            .Create();

        var plugin = new PluginLanguageImpl();
        config.AddSection<IPluginLanguage>(plugin, LangDir);
        config.Load();

        var retrieved = config.GetSection<IPluginLanguage>();
        Assert.Same(plugin, retrieved);

        config.Dispose();
    }

    [Fact]
    public void Load_SectionWithoutDirectory_Throws()
    {
        // AddSection with no directory and no default directory → Load() should throw
        var config = LanguageConfigBuilder.Create("testapp")
            .WithBaseLanguage("en-US")
            .Create();

        var plugin = new PluginLanguageImpl();
        config.AddSection<IPluginLanguage>(plugin);

        Assert.Throws<InvalidOperationException>(() => config.Load());
        config.Dispose();
    }

    // ── CurrentLanguage / BaseLanguage properties ─────────────────────────────

    [Fact]
    public void CurrentLanguage_ReflectsActiveLanguage()
    {
        var section = new MainLanguageImpl();
        using var config = LanguageConfigBuilder.Create("testapp")
            .WithDirectory(LangDir)
            .WithBaseLanguage("en-US")
            .WithCurrentLanguage("de-DE")
            .AddSection<IMainLanguage>(section)
            .Build();

        Assert.Equal("de-DE", config.CurrentLanguage);
        Assert.Equal("en-US", config.BaseLanguage);
    }
}
