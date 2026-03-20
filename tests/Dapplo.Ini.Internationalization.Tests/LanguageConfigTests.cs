// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Dapplo.Ini.Internationalization;
using Dapplo.Ini.Internationalization.Configuration;

namespace Dapplo.Ini.Internationalization.Tests;

/// <summary>
/// Tests for the <see cref="LanguageConfigBuilder"/>, <see cref="LanguageConfig"/>,
/// and <see cref="LanguageConfigRegistry"/> functionality.
/// </summary>
public sealed class LanguageConfigTests : IDisposable
{
    // Resolve the language file directory relative to the test assembly output location.
    private static readonly string LangDir =
        Path.Combine(AppContext.BaseDirectory, "Lang");

    public LanguageConfigTests()
    {
        LanguageConfigRegistry.Clear();
    }

    public void Dispose()
    {
        LanguageConfigRegistry.Clear();
    }

    // ── Load (base language) ──────────────────────────────────────────────────

    [Fact]
    public void Build_BaseLanguage_LoadsTranslations()
    {
        var section = new MainLanguageImpl();
        using var config = LanguageConfigBuilder.ForBasename("testapp")
            .AddSearchPath(LangDir)
            .WithBaseLanguage("en-US")
            .RegisterSection<IMainLanguage>(section)
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
        using var config = LanguageConfigBuilder.ForBasename("testapp")
            .AddSearchPath(LangDir)
            .WithBaseLanguage("en-US")
            .RegisterSection<IMainLanguage>(section)
            .Build();

        Assert.Equal("Save", section.SaveButton);
        Assert.Equal("Cancel", section.CancelButton);
    }

    // ── Escape sequences ──────────────────────────────────────────────────────

    [Fact]
    public void Build_EscapeSequences_AreUnescaped()
    {
        var section = new MainLanguageImpl();
        using var config = LanguageConfigBuilder.ForBasename("testapp")
            .AddSearchPath(LangDir)
            .WithBaseLanguage("en-US")
            .RegisterSection<IMainLanguage>(section)
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
            File.WriteAllText(Path.Combine(tempDir, "app.en-US.ini"), "[MainLanguage]\n");

            var section = new MainLanguageImpl();
            using var config = LanguageConfigBuilder.ForBasename("app")
                .AddSearchPath(tempDir)
                .WithBaseLanguage("en-US")
                .RegisterSection<IMainLanguage>(section)
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
        using var config = LanguageConfigBuilder.ForBasename("testapp")
            .AddSearchPath(LangDir)
            .WithBaseLanguage("en-US")
            .WithCurrentLanguage("en-US")
            .RegisterSection<IMainLanguage>(section)
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
        using var config = LanguageConfigBuilder.ForBasename("testapp")
            .AddSearchPath(LangDir)
            .WithBaseLanguage("en-US")
            .RegisterSection<IMainLanguage>(section)
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
        using var config = LanguageConfigBuilder.ForBasename("testapp")
            .AddSearchPath(LangDir)
            .WithBaseLanguage("en-US")
            .RegisterSection<IMainLanguage>(section)
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
        // ICoreLanguage (SectionName="CoreLanguage", no ModuleName) reads [CoreLanguage] from testapp.en-US.ini.
        var coreSection = new CoreLanguageImpl();
        using var config = LanguageConfigBuilder.ForBasename("testapp")
            .AddSearchPath(LangDir)
            .WithBaseLanguage("en-US")
            .RegisterSection<ICoreLanguage>(coreSection)
            .Build();

        Assert.Equal("Core Module", coreSection.CoreTitle);
        Assert.Equal("Ready", coreSection.CoreStatus);
    }

    [Fact]
    public void SetLanguage_ModuleSection_SwitchesCorrectly()
    {
        var coreSection = new CoreLanguageImpl();
        using var config = LanguageConfigBuilder.ForBasename("testapp")
            .AddSearchPath(LangDir)
            .WithBaseLanguage("en-US")
            .RegisterSection<ICoreLanguage>(coreSection)
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

        using var config = LanguageConfigBuilder.ForBasename("testapp")
            .AddSearchPath(LangDir)
            .WithBaseLanguage("en-US")
            .RegisterSection<IMainLanguage>(main)
            .RegisterSection<ICoreLanguage>(core)
            .Build();

        Assert.Equal("Welcome to the application!", main.WelcomeMessage);
        Assert.Equal("Core Module", core.CoreTitle);
    }

    // ── GetSection ────────────────────────────────────────────────────────────

    [Fact]
    public void GetSection_ReturnsRegisteredSection()
    {
        var section = new MainLanguageImpl();
        using var config = LanguageConfigBuilder.ForBasename("testapp")
            .AddSearchPath(LangDir)
            .WithBaseLanguage("en-US")
            .RegisterSection<IMainLanguage>(section)
            .Build();

        var retrieved = config.GetSection<IMainLanguage>();
        Assert.Same(section, retrieved);
    }

    [Fact]
    public void GetSection_UnregisteredType_Throws()
    {
        using var config = LanguageConfigBuilder.ForBasename("testapp")
            .AddSearchPath(LangDir)
            .WithBaseLanguage("en-US")
            .Build();

        Assert.Throws<InvalidOperationException>(() => config.GetSection<IMainLanguage>());
    }

    // ── GetAvailableLanguages ─────────────────────────────────────────────────

    [Fact]
    public void GetAvailableLanguages_ReturnsDiscoveredLanguages()
    {
        var section = new MainLanguageImpl();
        using var config = LanguageConfigBuilder.ForBasename("testapp")
            .AddSearchPath(LangDir)
            .WithBaseLanguage("en-US")
            .RegisterSection<IMainLanguage>(section)
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
        using var config = LanguageConfigBuilder.ForBasename("testapp")
            .AddSearchPath(LangDir)
            .WithBaseLanguage("en-US")
            .RegisterSection<IMainLanguage>(section)
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
        using var config = await LanguageConfigBuilder.ForBasename("testapp")
            .AddSearchPath(LangDir)
            .WithBaseLanguage("en-US")
            .RegisterSection<IMainLanguage>(section)
            .BuildAsync();

        Assert.Equal("Welcome to the application!", section.WelcomeMessage);
    }

    [Fact]
    public async Task SetLanguageAsync_SwitchesLanguage()
    {
        var section = new MainLanguageImpl();
        using var config = await LanguageConfigBuilder.ForBasename("testapp")
            .AddSearchPath(LangDir)
            .WithBaseLanguage("en-US")
            .RegisterSection<IMainLanguage>(section)
            .BuildAsync();

        Assert.Equal("Welcome to the application!", section.WelcomeMessage);

        await config.SetLanguageAsync("de-DE");

        Assert.Equal("Willkommen bei der Anwendung!", section.WelcomeMessage);
    }

    // ── IReadOnlyDictionary<string,string> support ────────────────────────────

    [Fact]
    public void DictionaryInterface_IndexerReturnsTranslation()
    {
        var section = new DictionaryLanguageImpl();
        using var config = LanguageConfigBuilder.ForBasename("testapp")
            .AddSearchPath(LangDir)
            .WithBaseLanguage("en-US")
            .RegisterSection<IDictionaryLanguage>(section)
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
        using var config = LanguageConfigBuilder.ForBasename("testapp")
            .AddSearchPath(LangDir)
            .WithBaseLanguage("en-US")
            .RegisterSection<IDictionaryLanguage>(section)
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
            LanguageConfigBuilder.ForBasename("testapp")
                .AddSearchPath(LangDir)
                .Build());
    }

    [Fact]
    public void Build_SectionWithoutSearchPath_Throws()
    {
        var section = new MainLanguageImpl();
        Assert.Throws<InvalidOperationException>(() =>
            LanguageConfigBuilder.ForBasename("testapp")
                .WithBaseLanguage("en-US")
                // No AddSearchPath, no per-section path
                .RegisterSection<IMainLanguage>(section)
                .Build());
    }

    // ── SectionName ────────────────────────────────────────────────────────────

    [Fact]
    public void SectionName_ReflectsAttributeValue()
    {
        var main = new MainLanguageImpl();
        var core = new CoreLanguageImpl();

        Assert.Equal("MainLanguage", main.SectionName);  // derived from IMainLanguage
        Assert.Null(main.ModuleName);
        Assert.Equal("CoreLanguage", core.SectionName);    // derived from ICoreLanguage (no explicit now)
        Assert.Null(core.ModuleName);
    }

    // ── ILanguageSection is optional ──────────────────────────────────────────

    [Fact]
    public void Interface_WithoutILanguageSection_WorksCorrectly()
    {
        // IMainLanguage does NOT extend ILanguageSection — verify it still works end-to-end.
        var section = new MainLanguageImpl();
        using var config = LanguageConfigBuilder.ForBasename("testapp")
            .AddSearchPath(LangDir)
            .WithBaseLanguage("en-US")
            .RegisterSection<IMainLanguage>(section)
            .Build();

        Assert.Equal("Welcome to the application!", section.WelcomeMessage);
    }

    // ── ForBasename() factory aligned with IniConfigBuilder.ForFile() ─────────

    [Fact]
    public void ForBasename_CreatesBuilder()
    {
        var section = new MainLanguageImpl();
        using var config = LanguageConfigBuilder.ForBasename("testapp")
            .AddSearchPath(LangDir)
            .WithBaseLanguage("en-US")
            .RegisterSection<IMainLanguage>(section)
            .Build();

        Assert.Equal("Welcome to the application!", section.WelcomeMessage);
    }

    // ── Section in main file (module keys inside [sectionName] block) ─────────

    [Fact]
    public void MergedFile_BothSectionsLoadFromSameFile()
    {
        // mergedapp.en-US.ini has [MainLanguage] and [CoreLanguage] sections in a single file.
        // Both ICoreLanguage and IMainLanguage load their respective sections.
        var main = new MainLanguageImpl();
        var core = new CoreLanguageImpl();

        using var config = LanguageConfigBuilder.ForBasename("mergedapp")
            .AddSearchPath(LangDir)
            .WithBaseLanguage("en-US")
            .RegisterSection<IMainLanguage>(main)
            .RegisterSection<ICoreLanguage>(core)
            .Build();

        Assert.Equal("Welcome (merged)", main.WelcomeMessage);
        Assert.Equal("Error (merged)", main.ErrorTitle);
        Assert.Equal("Core Module (merged)", core.CoreTitle);
        Assert.Equal("Ready (merged)", core.CoreStatus);
    }

    [Fact]
    public void ModuleName_SelectsModuleFile()
    {
        // IPluginLanguage has ModuleName = "core" → reads from testapp.core.en-US.ini under [PluginLanguage]
        // ICoreLanguage has no ModuleName → reads from testapp.en-US.ini under [CoreLanguage]
        var core = new CoreLanguageImpl();
        var plugin = new PluginLanguageImpl();

        using var config = LanguageConfigBuilder.ForBasename("testapp")
            .AddSearchPath(LangDir)
            .WithBaseLanguage("en-US")
            .RegisterSection<ICoreLanguage>(core)
            .RegisterSection<IPluginLanguage>(plugin)
            .Build();

        Assert.Equal("Core Module", core.CoreTitle);
        Assert.Equal("Ready", core.CoreStatus);
        Assert.Equal("Plugin Module", plugin.PluginTitle);
        Assert.Equal("Active", plugin.PluginStatus);
        Assert.Equal("CoreLanguage", core.SectionName);
        Assert.Null(core.ModuleName);
        Assert.Equal("PluginLanguage", plugin.SectionName);
        Assert.Equal("core", plugin.ModuleName);
    }

    // ── Deferred / plugin loading (Create + AddSection + Load) ───────────────

    [Fact]
    public void Create_ThenPluginAddSection_ThenLoad_LoadsAllSections()
    {
        var main = new MainLanguageImpl();

        // Phase 1: host creates config without loading
        var config = LanguageConfigBuilder.ForBasename("testapp")
            .AddSearchPath(LangDir)
            .WithBaseLanguage("en-US")
            .RegisterSection<IMainLanguage>(main)
            .Create();

        // Phase 2: plugin registers its own section
        var plugin = new PluginLanguageImpl();
        config.RegisterSection<IPluginLanguage>(plugin, LangDir);

        // Phase 3: host triggers loading
        config.Load();
        config.Dispose();

        Assert.Equal("Welcome to the application!", main.WelcomeMessage);
        Assert.Equal("Plugin Module", plugin.PluginTitle);
        Assert.Equal("Active", plugin.PluginStatus);
    }

    [Fact]
    public void Create_WithoutLoad_SectionsHaveNoTranslations()
    {
        var main = new MainLanguageImpl();
        var config = LanguageConfigBuilder.ForBasename("testapp")
            .AddSearchPath(LangDir)
            .WithBaseLanguage("en-US")
            .RegisterSection<IMainLanguage>(main)
            .Create();

        // No Load() called — translations must not be present yet
        Assert.Equal("###WelcomeMessage###", main.WelcomeMessage);

        config.Dispose();
    }

    [Fact]
    public void RegisterSection_OnConfig_MakesItAvailableViaGetSection()
    {
        var config = LanguageConfigBuilder.ForBasename("testapp")
            .AddSearchPath(LangDir)
            .WithBaseLanguage("en-US")
            .Create();

        var plugin = new PluginLanguageImpl();
        config.RegisterSection<IPluginLanguage>(plugin, LangDir);
        config.Load();

        var retrieved = config.GetSection<IPluginLanguage>();
        Assert.Same(plugin, retrieved);

        config.Dispose();
    }

    [Fact]
    public void Load_SectionWithoutSearchPath_Throws()
    {
        // RegisterSection with no path and no default search path → Load() should throw
        var config = LanguageConfigBuilder.ForBasename("testapp")
            .WithBaseLanguage("en-US")
            .Create();

        var plugin = new PluginLanguageImpl();
        config.RegisterSection<IPluginLanguage>(plugin);

        Assert.Throws<InvalidOperationException>(() => config.Load());
        config.Dispose();
    }

    // ── CurrentLanguage / BaseLanguage properties ─────────────────────────────

    [Fact]
    public void CurrentLanguage_ReflectsActiveLanguage()
    {
        var section = new MainLanguageImpl();
        using var config = LanguageConfigBuilder.ForBasename("testapp")
            .AddSearchPath(LangDir)
            .WithBaseLanguage("en-US")
            .WithCurrentLanguage("de-DE")
            .RegisterSection<IMainLanguage>(section)
            .Build();

        Assert.Equal("de-DE", config.CurrentLanguage);
        Assert.Equal("en-US", config.BaseLanguage);
    }

    // ── Keys outside section blocks ───────────────────────────────────────────

    [Fact]
    public void KeysOutsideSection_AreIgnored()
    {
        // Keys outside any [section] block must be silently ignored.
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            // File has a key outside sections (ignored) and one inside [MainLanguage]
            File.WriteAllText(Path.Combine(tempDir, "app.en-US.ini"),
                "WelcomeMessage=ShouldBeIgnored\n[MainLanguage]\nWelcomeMessage=InSection\n");

            var section = new MainLanguageImpl();
            using var config = LanguageConfigBuilder.ForBasename("app")
                .AddSearchPath(tempDir)
                .WithBaseLanguage("en-US")
                .RegisterSection<IMainLanguage>(section)
                .Build();

            Assert.Equal("InSection", section.WelcomeMessage);
            // Verify the key that was outside the section was NOT loaded
            Assert.Equal("###ErrorTitle###", section.ErrorTitle);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    // ── LanguageConfigRegistry ────────────────────────────────────────────────

    [Fact]
    public void LanguageConfigRegistry_ForFile_ReturnsBuilderAndRegisters()
    {
        var section = new MainLanguageImpl();
        using var config = LanguageConfigRegistry.ForFile("testapp")
            .AddSearchPath(LangDir)
            .WithBaseLanguage("en-US")
            .RegisterSection<IMainLanguage>(section)
            .Build();

        Assert.Equal("Welcome to the application!", section.WelcomeMessage);

        // The config should be retrievable via the registry
        var retrieved = LanguageConfigRegistry.Get("testapp");
        Assert.Same(config, retrieved);
    }

    [Fact]
    public void LanguageConfigRegistry_ForFile_WithExtension_NormalizesToBasename()
    {
        var section = new MainLanguageImpl();
        // Pass "testapp2.ini" — the .ini extension should be stripped automatically
        using var config = LanguageConfigRegistry.ForFile("testapp2.ini")
            .AddSearchPath(LangDir)
            .WithBaseLanguage("en-US")
            .RegisterSection<IMainLanguage>(section)
            .Build();

        // Retrieve with and without extension — both should work
        var byBasename = LanguageConfigRegistry.Get("testapp2");
        var byFileName = LanguageConfigRegistry.Get("testapp2.ini");
        Assert.Same(config, byBasename);
        Assert.Same(config, byFileName);
    }

    [Fact]
    public void LanguageConfigRegistry_GetSection_ReturnsSection()
    {
        var section = new MainLanguageImpl();
        using var config = LanguageConfigRegistry.ForFile("regapp")
            .AddSearchPath(LangDir)
            .WithBaseLanguage("en-US")
            .RegisterSection<IMainLanguage>(section)
            .Build();

        var retrieved = LanguageConfigRegistry.GetSection<IMainLanguage>("regapp");
        Assert.Same(section, retrieved);
    }

    [Fact]
    public void LanguageConfigRegistry_Get_ThrowsWhenNotRegistered()
    {
        Assert.Throws<KeyNotFoundException>(() => LanguageConfigRegistry.Get("nonexistent"));
    }

    [Fact]
    public void LanguageConfigRegistry_TryGet_ReturnsFalseWhenMissing()
    {
        var found = LanguageConfigRegistry.TryGet("nope", out var config);
        Assert.False(found);
        Assert.Null(config);
    }

    [Fact]
    public void LanguageConfigRegistry_Unregister_RemovesEntry()
    {
        var section = new MainLanguageImpl();
        using var config = LanguageConfigRegistry.ForFile("unreg")
            .AddSearchPath(LangDir)
            .WithBaseLanguage("en-US")
            .RegisterSection<IMainLanguage>(section)
            .Build();

        Assert.True(LanguageConfigRegistry.TryGet("unreg", out _));
        LanguageConfigRegistry.Unregister("unreg");
        Assert.False(LanguageConfigRegistry.TryGet("unreg", out _));
    }

    [Fact]
    public void LanguageConfigBuilder_Build_RegistersInRegistry()
    {
        // Using ForBasename directly (not ForFile) should still register in the registry
        var section = new MainLanguageImpl();
        using var config = LanguageConfigBuilder.ForBasename("directbuild")
            .AddSearchPath(LangDir)
            .WithBaseLanguage("en-US")
            .RegisterSection<IMainLanguage>(section)
            .Build();

        var retrieved = LanguageConfigRegistry.Get("directbuild");
        Assert.Same(config, retrieved);
    }

    [Fact]
    public void LanguageConfigRegistry_Get_NoArg_SingleRegistration_ReturnsConfig()
    {
        var section = new MainLanguageImpl();
        using var config = LanguageConfigRegistry.ForFile("singleapp")
            .AddSearchPath(LangDir)
            .WithBaseLanguage("en-US")
            .RegisterSection<IMainLanguage>(section)
            .Build();

        var retrieved = LanguageConfigRegistry.Get();
        Assert.Same(config, retrieved);
    }

    [Fact]
    public void LanguageConfigRegistry_Get_NoArg_NoRegistration_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => LanguageConfigRegistry.Get());
    }

    [Fact]
    public void LanguageConfigRegistry_Get_NoArg_MultipleRegistrations_Throws()
    {
        using var config1 = LanguageConfigBuilder.ForBasename("multi1lang")
            .AddSearchPath(LangDir)
            .WithBaseLanguage("en-US")
            .Build();
        using var config2 = LanguageConfigBuilder.ForBasename("multi2lang")
            .AddSearchPath(LangDir)
            .WithBaseLanguage("en-US")
            .Build();

        Assert.Throws<InvalidOperationException>(() => LanguageConfigRegistry.Get());
    }

    [Fact]
    public void LanguageConfigRegistry_GetSection_NoArg_SingleRegistration_ReturnsSection()
    {
        var section = new MainLanguageImpl();
        using var config = LanguageConfigRegistry.ForFile("singlesectapp")
            .AddSearchPath(LangDir)
            .WithBaseLanguage("en-US")
            .RegisterSection<IMainLanguage>(section)
            .Build();

        var retrieved = LanguageConfigRegistry.GetSection<IMainLanguage>();
        Assert.Same(section, retrieved);
    }
}
