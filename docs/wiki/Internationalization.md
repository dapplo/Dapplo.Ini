# Internationalization

`Dapplo.Ini` includes built-in support for `.ini`-based language packs via the
`Dapplo.Ini.Internationalization` namespace. Translation strings are defined as
interface properties; the source generator creates a concrete implementation
automatically.

---

## Quick start

```csharp
using Dapplo.Ini.Internationalization;
using Dapplo.Ini.Internationalization.Attributes;

// 1. Define a language section interface
[IniLanguageSection]
public interface IMainLanguage
{
    string WelcomeMessage { get; }
    string CancelButton   { get; }
}

// 2. Register at application startup via LanguageConfigRegistry (preferred entry point)
using var langConfig = LanguageConfigRegistry.ForFile("myapp")   // ".ini" extension optional
    .AddSearchPath("/path/to/lang")
    .WithBaseLanguage("en-US")
    .WithCurrentLanguage("de-DE")
    .RegisterSection<IMainLanguage>(new MainLanguageImpl())   // generated class
    .Build();

// 3. Use the translations — from anywhere in the app (no reference needed)
var lang = LanguageConfigRegistry.GetSection<IMainLanguage>();
Console.WriteLine(lang.WelcomeMessage);   // "Willkommen bei der Anwendung!"

// 4. Switch language at runtime
langConfig.SetLanguage("fr-FR");
```

> **Tip:** When exactly one language configuration is registered (the common case),
> `LanguageConfigRegistry.GetSection<T>()` and `LanguageConfigRegistry.Get()` work
> without any argument. Use `GetSection<T>("myapp")` only when multiple language
> configurations are registered.

---

## Language file format

Language files are standard `.ini` files. Every key **must** be inside a
`[SectionName]` block — keys outside any section header are silently ignored.

### File naming

| Condition | File pattern |
|-----------|--------------|
| No module | `{basename}.{ietf}.ini` |
| With module | `{basename}.{moduleName}.{ietf}.ini` |

Examples for basename `myapp`:

| File | Contains |
|------|----------|
| `myapp.en-US.ini` | Base language (English) |
| `myapp.de-DE.ini` | German |
| `myapp.de.ini` | Generic German (used as a fallback step) |
| `myapp.core.en-US.ini` | English translations for the `core` module |

### Key rules

- Each line: `key=value` (everything after the first `=` is the raw value; whitespace around
  the `=` is trimmed, so `key = value` gives `value`).
- Keys are **trimmed**; underscores `_` and dashes `-` are removed before
  comparison, so `Welcome_Message`, `WelcomeMessage`, and `welcomemessage` all
  refer to the same property.
- Lookup is **case-insensitive**.
- Dots are kept (`WindowCaptureMode.Auto` is a valid key); see
  [Looking up keys without a section](#looking-up-keys-without-a-section).
- Only **whole lines** starting with `;` or `#` are comments. A `;` or `#` inside a value is part of the
  value: `Hint=Press Ctrl+S; then #1` gives `Press Ctrl+S; then #1`.

### Value escape sequences

| Sequence | Becomes |
|----------|---------|
| `\n` | Newline |
| `\t` | Tab |
| `\\` | Backslash |

Any other backslash is kept as it is (`\x` stays `\x`, a trailing `\` stays). Write `\\n` for a literal
backslash followed by `n`.

### Example file

```ini
; myapp.en-US.ini — all keys must be inside a [SectionName] block

; Optional: describes the language itself (shown in a language picker). Never a translation section.
[__language__]
Description=English

[MainLanguage]
WelcomeMessage=Welcome to the application!
Cancel_Button=Cancel

[CoreLanguage]
CoreTitle=Core Module
CoreStatus=Ready
```

---

## Defining language section interfaces

Annotate an interface with `[IniLanguageSection]`. Every `string` property
(with `get` only) becomes a translatable key.

```csharp
[IniLanguageSection]
public interface IMainLanguage
{
    string WelcomeMessage { get; }
    string CancelButton   { get; }
    string ErrorTitle     { get; }
}
```

Implementing `ILanguageSection` is **optional** — the generated class always
derives from `LanguageSectionBase` regardless.

### `[IniLanguageSection]` attribute properties

The attribute has two independent properties:

| Property | Purpose | Default |
|----------|---------|---------|
| `SectionName` (positional) | `[SectionName]` block to read in the file | Derived from interface name: strip leading `I` — e.g. `IMainLanguage` → `MainLanguage` |
| `ModuleName` (named) | File selector | `null` → `{basename}.{ietf}.ini`; set → `{basename}.{moduleName}.{ietf}.ini` |

Usage patterns:

```csharp
// Derived section name "MainLanguage", no module → reads [MainLanguage] from {basename}.{ietf}.ini
[IniLanguageSection]
public interface IMainLanguage { ... }

// Explicit section name "ui", no module → reads [ui] from {basename}.{ietf}.ini
[IniLanguageSection("ui")]
public interface IUiStrings { ... }

// Derived "PluginLanguage", module "core" → reads [PluginLanguage] from {basename}.core.{ietf}.ini
[IniLanguageSection(ModuleName = "core")]
public interface IPluginLanguage { ... }

// Explicit "ui", module "core" → reads [ui] from {basename}.core.{ietf}.ini
[IniLanguageSection("ui", ModuleName = "core")]
public interface IUiStrings { ... }
```

### Generated class naming

The source generator follows the same convention as the INI section generator:
strip the leading `I` (if present) and append `Impl`.

| Interface | Generated class |
|-----------|----------------|
| `IMainLanguage` | `MainLanguageImpl` |
| `ICoreLanguage` | `CoreLanguageImpl` |
| `IPluginLanguage` | `PluginLanguageImpl` |

Large interfaces are fine: an interface with 600 properties generates, compiles and switches language
without noticeable cost (the change detection for `INotifyPropertyChanged` is table-driven).

---

## LanguageConfigRegistry

`LanguageConfigRegistry` is the recommended entry point for language configurations.
It is a thread-safe global registry that mirrors `IniConfigRegistry` — including the
`.ini`-optional basename convention and no-arg convenience overloads.

| Method | Description |
|--------|-------------|
| `ForFile(basename)` | **Preferred entry point.** Returns a fluent `LanguageConfigBuilder`. The `.ini` extension is stripped if present. |
| `Get(basename)` | Returns the registered `LanguageConfig`; throws `KeyNotFoundException` if absent. |
| `Get()` | Returns the single registered `LanguageConfig`. Throws `InvalidOperationException` when 0 or more than one config is registered. |
| `TryGet(basename, out config)` | Returns `false` if not registered. |
| `GetSection<T>(basename)` | Returns the section from the named config. |
| `GetSection<T>()` | Returns the section from the single registered config (common case). |
| `Unregister(basename)` | Removes a registration. Useful in tests. |
| `Clear()` | Removes all registrations. Useful in tests. |

Both `"myapp"` and `"myapp.ini"` resolve to the same registry entry.

---

## LanguageConfigBuilder — fluent API

`LanguageConfigRegistry.ForFile(name)` is the recommended entry point; it internally
calls `LanguageConfigBuilder.ForBasename(name)`. Both methods name the file pattern
`{basename}.{ietf}.ini`.

```csharp
using var config = LanguageConfigRegistry.ForFile("myapp")   // preferred entry point
    .AddSearchPath("/path/to/lang")      // directory to search for language files
    .WithBaseLanguage("en-US")           // REQUIRED — the reference language
    .WithCurrentLanguage("de-DE")        // optional — defaults to base language
    .RegisterSection<IMainLanguage>(new MainLanguageImpl())
    .RegisterSection<ICoreLanguage>(new CoreLanguageImpl())
    .UseFallbackLanguage("en-US")         // optional — floor for missing keys (default: base language)
    .MonitorFiles()                       // reload when files change on disk
    .Build();                             // load immediately (sync)
```

### Builder methods

| Method | Description |
|--------|-------------|
| `ForFile(name)` | **(on `LanguageConfigRegistry`) Preferred entry point.** Equivalent to `ForBasename(name)` with automatic registry wiring. |
| `ForBasename(name)` | **Static factory on `LanguageConfigBuilder`.** Names the `{basename}.{ietf}.ini` file pattern. |
| `AddSearchPath(path)` | Directory to search for language pack files. |
| `WithBaseLanguage(ietf)` | **Required.** The reference language that is always loaded first. |
| `WithCurrentLanguage(ietf)` | Language to activate on the first load. Defaults to the base language. |
| `RegisterSection<T>(impl, path?)` | Registers a language section; optional path overrides `AddSearchPath`. |
| `UseFallbackLanguage(ietf)` | Uses `ietf` instead of the base language as the floor for keys that are missing in the active language. Without it the base language is the floor. |
| `MonitorFiles()` | Enables file-system monitoring. When any language file changes, all sections are reloaded and `LanguageChanged` is raised. |
| `AllowLateSectionRegistration()` | `LanguageConfig.RegisterSection` after the load loads the section right away. See [Registering sections after the load](#registering-sections-after-the-load). |
| `MergeSearchPaths()` | A language file is merged across all search paths instead of the first one winning. See [Merging search paths](#merging-search-paths). |
| `ResolveLanguages()` | Resolves the requested language to an available one (`de` → `de-DE`, `ptBR` → `pt-BR`). See [Resolving the requested language](#resolving-the-requested-language). |
| `AddListener(listener)` | Registers an `IIniConfigListener` for diagnostic events. See [[Listeners]]. |
| `Create()` | Creates `LanguageConfig` **without** loading any files. Use for plugin/deferred scenarios. |
| `Build()` | Creates and loads `LanguageConfig` synchronously. |
| `BuildAsync(ct?)` | Creates and loads `LanguageConfig` asynchronously. Returns `Task<LanguageConfig>`. |

---

## Loading and language switching

### Synchronous

```csharp
// Reload everything in the active language
config.Load();

// Switch to another language (also reloads all sections)
config.SetLanguage("fr-FR");
```

### Asynchronous

```csharp
// Async load
await config.LoadAsync(cancellationToken);

// Async language switch
await config.SetLanguageAsync("fr-FR", cancellationToken);
```

### LanguageChanged event

Raised after every successful reload — either from `SetLanguage`, `SetLanguageAsync`,
or a file-change notification when `MonitorFiles()` is active:

```csharp
config.LanguageChanged += (sender, _) =>
    Console.WriteLine($"Language is now: {((LanguageConfig)sender!).CurrentLanguage}");
```

---

## Progressive fallback

When a requested language is not fully available, the loader falls back
progressively from most-specific to least-specific:

1. The parent cultures of the fallback language, least specific first (`en` for `en-US`).
2. The fallback language itself — the base language, or the one set with `UseFallbackLanguage`.
   This is the floor for missing keys and is always loaded.
3. Every parent culture of the requested language, least specific first — e.g. `de` when
   requesting `de-DE`, or `zh` and then `zh-Hant` when requesting `zh-Hant-TW`.
4. The requested culture — `de-DE` / `zh-Hant-TW` overwrites keys from everything before it.

A language that appears twice in this chain is loaded once, at its first position.

This means switching to `de-DE` when only a partial `de-DE` file exists will
still show the German base strings from `de.ini` rather than the `###key###`
sentinel.

The translations of a section are built completely and then swapped in at once, so a UI
thread reading a property during a language switch or file-change reload sees either the
old or the new language — never a half-loaded mix.

Reading the files for a load, a language switch, a file-change reload or a section registration is
serialised, so a plugin may register its section on its own thread while another thread switches the
language. The new translations are applied, and `PropertyChanged` / `LanguageChanged` and listeners
are raised, afterwards on the calling thread and outside that lock: a handler may marshal to the UI
thread (`Dispatcher.Invoke`), switch the language again or register a section. Every section always
applies the translations of the latest operation, so all sections end up in the current language.
While one thread applies texts to a section, another thread that switched the language returns at
once and the first one applies the newer texts too. Reading translations never waits.

Loading is designed to create little garbage: files are streamed through a pooled buffer (never read
into one large string), each file is read once for all sections, overridden fallback texts are skipped,
and on .NET 9+ strings of the previous load are reused, so a reload of unchanged files allocates no
strings. Lookups (`TryGetTranslation`, the indexer, `ContainsKey`) do not allocate.

When reading a language file fails, the switch is not committed: `CurrentLanguage` and all sections
keep the previous language. When a listener or a `PropertyChanged` / `LanguageChanged` handler throws,
the remaining handlers and sections still run, every failure is reported via `OnError`, and the first
one is re-thrown. Listener callbacks of concurrent operations can arrive interleaved.

### `UseFallbackLanguage(ietf)`

The fallback language is always loaded first (see the chain above), so a key missing in
the requested language shows the fallback's text instead of the `###key###` sentinel.
By default this is the base language; `UseFallbackLanguage` picks another one, for example
to show English for missing keys while German is the base language:

```csharp
.WithBaseLanguage("de-DE")
.UseFallbackLanguage("en-US")   // missing keys show the en / en-US text
```

The sentinel only appears for keys that are missing in every file of the chain.

> **Breaking change:** `UseFallback()` / `UseFallback(ietf)` were replaced by
> `UseFallbackLanguage(ietf)`. The argument-less `UseFallback()` had no effect (the base
> language was always the floor) and can simply be removed.

---

## Discovering available languages

`GetLanguages()` scans the search paths for language files and returns one `LanguageInfo` per
language, sorted by tag; `GetAvailableLanguages()` returns the same languages as
`(Ietf, NativeName)` tuples (kept for compatibility).

```csharp
foreach (var language in config.GetLanguages())
    picker.Items.Add(new { language.Ietf, Name = language.DisplayName });
```

| `LanguageInfo` member | Meaning |
|-----------------------|---------|
| `Ietf` | The tag as spelled in the file name, e.g. `de-DE`, `de-x-franconia`. |
| `NativeName` | `CultureInfo.NativeName`, or `null` when the system does not know the tag. |
| `Description` | `Description` from the `[__language__]` section of the file, or `null`. |
| `HasBaseFile` | Whether `{basename}.{ietf}.ini` exists. |
| `DisplayName` | `Description`, else `NativeName`, else `Ietf`. This is also the second value of `GetAvailableLanguages()`. |

### Describing a language in its file

Tags Windows does not know (Greenshot's `de-x-franconia`, or `fr-QC` on older systems) would only show
as the bare tag. Give the language a name in its base file:

```ini
; greenshot.de-x-franconia.ini
[__language__]
Description=Fränkisch

[Core]
...
```

`[__language__]` is reserved (like `[__metadata__]` in configuration files): it is never routed to a
registered section, and registering a section with that name throws `ArgumentException`.

### Which files define a language

- **Base files** (`{basename}.{ietf}.ini`) define the languages. Module files
  (`{basename}.{module}.{ietf}.ini`) only add translations; a language that only has module files is
  not listed. Exception: when every registered section is a module section, the files of the
  registered modules define the languages (`HasBaseFile` is then `false` for them).
- A tag must look like an IETF tag: a two or three letter language subtag followed by subtags
  (`de`, `de-DE`, `zh-Hant-TW`, `es-419`, `de-x-franconia`). A file name segment that equals a
  registered module name is never a tag, so `greenshot.box.ini` is not taken for a language `box`.
- The description is read from the highest priority file that has one.

## Plugin / deferred loading

The same three-phase pattern used by `IniConfigBuilder.Create()` is available
for language configs. The host creates the config without loading; plugins
register their own sections; the host triggers loading once:

```csharp
// ── Host startup ──────────────────────────────────────────────────────────────

// Phase 1 — create without loading (registry entry is created immediately)
var langConfig = LanguageConfigRegistry.ForFile("myapp")
    .AddSearchPath(langDir)
    .WithBaseLanguage("en-US")
    .RegisterSection<IMainLanguage>(new MainLanguageImpl())
    .Create();   // no I/O

// Phase 2 — each plugin registers its own section (path is optional)
// Inside a plugin pre-init method:
langConfig.RegisterSection<IPluginLanguage>(new PluginLanguageImpl(), pluginLangDir);

// Phase 3 — host loads all sections at once
langConfig.Load();
// or: await langConfig.LoadAsync(cancellationToken);
```

Sections that live in the same directory as the host need no path override:

```csharp
langConfig.RegisterSection<IPluginLanguage>(new PluginLanguageImpl());
// uses the host's AddSearchPath directory
```

### Registering sections after the load

Plugins often start after the host has loaded the language configuration and shown UI. Without an
option, a section registered after the load stays empty (every property returns `###Key###`) until
the next `Load()` or `SetLanguage()`. With `AllowLateSectionRegistration()` it is loaded right away:

```csharp
// Host
var langConfig = LanguageConfigRegistry.ForFile("greenshot")
    .AddSearchPath(languageDir)
    .WithBaseLanguage("en-US")
    .WithCurrentLanguage(settings.Language)
    .AllowLateSectionRegistration()
    .RegisterSection<ICoreLanguage>(new CoreLanguageImpl())
    .Build();
ShowMainWindow();

// Later, in a plugin (any thread)
var imgurLanguage = LanguageConfigRegistry.Get()
    .RegisterSection<IImgurLanguage>(new ImgurLanguageImpl(), pluginDirectory);
// or: await langConfig.RegisterSectionAsync<IImgurLanguage>(new ImgurLanguageImpl(), pluginDirectory);
imgurLanguage.History;   // already translated
```

- Only the new section reads files: the current language with its fallback chain. Other sections are
  not reloaded and `LanguageChanged` is not raised.
- Later language switches and file-change reloads include the section; with `MonitorFiles()` its
  directory is watched from now on.
- Registering the same type again replaces the earlier instance.
- Listeners implementing `IIniConfigExtendedListener` get `OnSectionAdded(sectionName, loaded)`:
  `true` when it was loaded right away. Without the option, a registration after the load is reported
  as `OnSectionAdded(name, false)` plus `OnError("RegisterSection", …)` (not thrown), so the mistake
  is visible in the log.

---

## Multi-section files

A single `.ini` file can hold sections for several interfaces at once. No
separate file per interface is required — the loader routes each key to the
correct interface by matching the `[SectionName]` block:

```ini
; myapp.en-US.ini

[MainLanguage]
WelcomeMessage=Welcome
CancelButton=Cancel

[CoreLanguage]
CoreTitle=Core Module
CoreStatus=Ready
```

Module sections (those with `ModuleName` set) are only read from their module file
(`{basename}.{moduleName}.{ietf}.ini`), never from the main file.

---

## Merging search paths

By default the first search path that has `{basename}.{ietf}.ini` wins and the others are ignored.
With `MergeSearchPaths()` the file of every search path is read, lowest priority (added last) first,
so a higher priority path overrides single keys instead of the whole file:

```csharp
LanguageConfigRegistry.ForFile("greenshot")
    .AddSearchPath(portableDir)                                   // highest priority
    .AddSearchPath(Path.Combine(appData, "Greenshot", "Languages"))  // user corrections
    .AddSearchPath(Path.Combine(installDir, "Languages"))           // shipped translations
    .MergeSearchPaths()
    ...
```

```ini
; %APPDATA%\Greenshot\Languages\greenshot.de-DE.ini — only fixes three texts
[Core]
SettingsTitle=Einstellungen
```

- This happens for every tag of the load chain (`en`, `en-US`, `de`, `de-DE`, …).
- A section registered with its own path (`RegisterSection<T>(impl, path)`) keeps that path and is not
  merged with the search paths — unless that path is one of the search paths, then it uses (and
  merges) all of them.
- `OnFileLoaded` is reported for every merged file, in the order they are applied.
- `MonitorFiles()` watches every search path that exists when the configuration is loaded; creating,
  changing, deleting or renaming a language file reloads, so a translation dropped into `%APPDATA%`
  shows up without a restart. A folder created later is not watched (create it at startup if needed).

---

## Resolving the requested language

A language from the settings or an installer does not always match a file: `de` when only `de-DE`
exists, the legacy `ptBR`, or a language that is not translated at all. `ResolveLanguage(requested)`
returns the closest available language (see [Discovering available languages](#discovering-available-languages)):

1. An exact match, case-insensitive (returned in the file's spelling).
2. The same tag without hyphens, or with `_`: `ptBR`, `pt_BR` → `pt-BR`; `zhCN` → `zh-CN`.
3. A tag with the same language subtag:
   - one with the same region (`zh-Hant-TW` → `zh-TW`);
   - then for the requested tag and each of its parents in turn (from `CultureInfo.Parent` and by
     removing the last subtag, most specific first): the parent itself (`de-AT` → `de`), its default
     specific culture (`de`, `de-AT` → `de-DE`), one of its children (`zh-HK` → `zh-TW` via `zh-Hant`,
     so Traditional Chinese never ends up as `zh-CN`);
   - finally the first available tag with that language subtag (`de-AT` → `de-CH`).

   Private-use variants (`de-x-franconia`) are only used when requested exactly; a German user does
   not get the dialect.
4. Otherwise the base language.

Parents and specific cultures come from the system's culture data, which can differ between .NET (ICU)
and .NET Framework (NLS) for rare tags.

`ResolveLanguage` is always available, only scans file names and does not change any state. With `ResolveLanguages()` on the
builder, `WithCurrentLanguage` (at load) and `SetLanguage` use it:

```csharp
using var config = LanguageConfigRegistry.ForFile("greenshot")
    .AddSearchPath(languageDir)
    .WithBaseLanguage("en-US")
    .WithCurrentLanguage("de")    // only greenshot.de-DE.ini exists
    .ResolveLanguages()
    ...
    .Build();

config.CurrentLanguage;    // "de-DE"
config.RequestedLanguage;  // "de"
```

Listeners implementing `ILanguageConfigListener` get `OnLanguageResolved(requested, resolved)`.
Without the option the requested tag is used as is (only the files that exist for it are loaded,
the rest comes from the fallback).

---

## Looking up keys without a section

Some keys are built at runtime: enum values (`WindowCaptureMode.Auto`), labels from configuration
files, undo/redo action names, plugin keys with a module prefix (`imgur.history`).

```csharp
var config = LanguageConfigRegistry.Get();
string text = config.GetTranslation($"{nameof(WindowCaptureMode)}.{mode}");   // "###key###" when missing
if (config.TryGetTranslation("imgur.history", out var history)) { ... }

// Single registered configuration:
LanguageConfigRegistry.TryGetTranslation("imgur.history", out var value);
LanguageConfigRegistry.GetTranslation("WindowCaptureMode.Auto");
```

- The key is normalised like any other key; dots are kept.
- `prefix.key`, where `prefix` is the module name or section name of a registered section, only
  searches those sections for `key` (`imgur.history`, `Editor.Title`). A missing key there is not
  looked up elsewhere.
- Any other key, dots included, is searched in all sections.
- When a key exists in more than one section: sections without a module first, then module sections,
  each in registration order; the first match wins. With a prefix, sections whose module name matches
  come before sections whose section name matches.
- `GetTranslation` reports a missing key via `ILanguageConfigListener.OnTranslationNotFound`.

---

## Formatting

`LanguageSectionBase.Format(key, args)` formats a translation with `string.Format`. It never throws:
a missing key returns the `###key###` sentinel, a text that cannot be formatted (bad format string,
too few arguments) is returned unformatted. Both are reported via `ILanguageConfigListener`
(`OnTranslationNotFound`, `OnFormatFailed`).

```csharp
var editor = (LanguageSectionBase)LanguageConfigRegistry.GetSection<IEditorLanguage>();
editor.Format("UndoAction", actionName);   // "Undo {0}" → "Undo Resize"
```

---

## IReadOnlyDictionary support

`LanguageSectionBase` already implements `IReadOnlyDictionary<string, string>`.
If you want your interface to be assignable to that type, simply extend it:

```csharp
[IniLanguageSection]
public interface IMainLanguage : IReadOnlyDictionary<string, string>
{
    string WelcomeMessage { get; }
    string CancelButton   { get; }
}

// Dynamic indexer access — key is normalized before lookup
string val = langConfig.GetSection<IMainLanguage>()["welcome_message"];
string val2 = langConfig.GetSection<IMainLanguage>()["WelcomeMessage"];  // same result
```

---

## Missing-key sentinel

When a key is absent from the loaded language file (and no fallback applies),
the property returns `###PropertyName###`. This makes missing translations
immediately visible in the UI during development.

---

## File-change monitoring

Call `.MonitorFiles()` on the builder to automatically reload all language
sections when any language file in a watched directory changes on disk. The
reload is debounced (200 ms) to handle editors that write files in multiple
steps. An exception during such a reload is reported via `IIniConfigListener.OnError`
(see [[Listeners]]) instead of crashing the process.

```csharp
using var config = LanguageConfigRegistry.ForFile("myapp")
    .AddSearchPath(langDir)
    .WithBaseLanguage("en-US")
    .RegisterSection<IMainLanguage>(new MainLanguageImpl())
    .MonitorFiles()
    .Build();

config.LanguageChanged += (_, _) => RefreshUi();
```

---

## Migrating from XML language files

Applications like Greenshot used XML language files with `<resource name="…">` entries. Moving them to
`.ini` language packs:

| XML | `.ini` language pack |
|-----|----------------------|
| `language-de-DE.xml` | `greenshot.de-DE.ini` (`{basename}.{ietf}.ini`) |
| `<language description="Deutsch" ietf="de-DE">` | `[__language__]` with `Description=Deutsch` (optional; the culture's native name is used otherwise) |
| `<resource name="settings_title">Einstellungen</resource>` | `settings_title=Einstellungen` inside a `[Section]` |
| Plugin file `language_imgur-de-DE.xml` (prefix `imgur`) | Module file `greenshot.imgur.de-DE.ini`, interface with `ModuleName = "imgur"` |
| `Language.GetString("imgur", "settings_title")` | `imgurLanguage.SettingsTitle`, or `config.GetTranslation("imgur.settings_title")` |
| `Language.GetString(LangKey.about_bugs)` | `coreLanguage.AboutBugs` |
| `Language.GetFormattedString(key, args)` | `section.Format(key, args)` (never throws) |
| Text over several lines | One line with `\n` escapes |

- **Key → property:** keys are normalised (`_` and `-` removed, case-insensitive), so the key
  `settings_title` maps to the property `SettingsTitle` without renaming anything in the files.
- **One `[Section]` per interface:** split the keys into interfaces (e.g. core, settings, editor,
  destinations) and put each group under its own section header in the same file. Keys outside a
  section are ignored. Interfaces with hundreds of properties are fine.
- **Module files for plugins:** each plugin ships `greenshot.{module}.{ietf}.ini` in its own folder
  and registers its section with that folder (`RegisterSection<T>(impl, pluginDir)`); enable
  `AllowLateSectionRegistration()` when plugins start after the UI is shown.
- **Multi-line texts and special characters:** use `\n` for a line break, `\t` for a tab and `\\` for a
  backslash. XML entities (`&amp;`, `&lt;`) become the plain characters. `;` and `#` inside a value
  are fine; only lines starting with them are comments.
- **Keys built at runtime** (enum values, plugin keys) use `GetTranslation` / `TryGetTranslation`
  instead of properties; see [Looking up keys without a section](#looking-up-keys-without-a-section).
- **Language selection:** `ResolveLanguages()` takes over "closest available language" logic,
  `GetLanguages()` fills the language picker.
- **User translations:** with `MergeSearchPaths()` a user can drop a partial file into a
  higher priority folder (e.g. `%APPDATA%`).

---

## Complete API reference

### LanguageConfigRegistry

| Method | Description |
|--------|-------------|
| `ForFile(basename)` | Preferred entry point. Returns a `LanguageConfigBuilder`. Strips `.ini` if present. |
| `Get(basename)` | Returns the registered `LanguageConfig`; throws `KeyNotFoundException` if absent. |
| `Get()` | Returns the single registered `LanguageConfig`; throws `InvalidOperationException` when zero or more than one registered. |
| `TryGet(basename, out config)` | Returns `false` if not registered. |
| `GetSection<T>(basename)` | Returns the section of type `T` from the named config. |
| `GetSection<T>()` | Returns the section of type `T` from the single registered config. |
| `TryGetTranslation(key, out value)` / `TryGetTranslation(basename, key, out value)` | Looks up a key without knowing its section. |
| `GetTranslation(key)` | Same, returns `###key###` when missing (single registered config). |
| `Unregister(basename)` | Removes a registration. Useful in tests. |
| `Clear()` | Removes all registrations. Useful in tests. |

### LanguageConfigBuilder

| Method | Description |
|--------|-------------|
| `ForBasename(name)` | Static factory. Names the file pattern `{basename}.{ietf}.ini`. |
| `AddSearchPath(path)` | Adds a search directory for language files. |
| `WithBaseLanguage(ietf)` | Sets the base (reference) language. **Required.** |
| `WithCurrentLanguage(ietf)` | Sets the initial active language. |
| `RegisterSection<T>(impl, path?)` | Registers a section; optional `path` overrides the default search path for this section only. |
| `UseFallbackLanguage(ietf)` | Sets the floor language for missing keys (default: the base language). |
| `MonitorFiles()` | Enables file-system change monitoring with debounce. |
| `AllowLateSectionRegistration()` | `RegisterSection` after the load loads the section right away. |
| `MergeSearchPaths()` | Merges a language file across all search paths (higher priority overrides single keys). |
| `ResolveLanguages()` | `WithCurrentLanguage` / `SetLanguage` use `ResolveLanguage`. |
| `AddListener(listener)` | Registers an `IIniConfigListener` for diagnostic events. See [[Listeners]]. |
| `Create()` | Creates `LanguageConfig` without loading. Plugin-friendly deferred pattern. |
| `Build()` | Creates and loads `LanguageConfig` synchronously. Returns `LanguageConfig`. |
| `BuildAsync(ct?)` | Creates and loads `LanguageConfig` asynchronously. Returns `Task<LanguageConfig>`. |

### LanguageConfig

| Member | Description |
|--------|-------------|
| `CurrentLanguage` | The IETF tag of the currently active language (the resolved one with `ResolveLanguages()`). |
| `RequestedLanguage` | The tag that was last requested. |
| `BaseLanguage` | The base (reference) language supplied at build time. |
| `IsLoaded` | `true` after the first load or language switch. |
| `GetSection<T>()` | Returns the registered section instance for interface `T`; throws if not registered. |
| `RegisterSection<T>(impl, path?)` | Registers a section after `Create()` (plugin pattern), or after the load with `AllowLateSectionRegistration()`. Returns `impl` for chaining. |
| `RegisterSectionAsync<T>(impl, path?, ct?)` | Async variant of `RegisterSection`. |
| `TryGetTranslation(key, out value)` / `GetTranslation(key)` | Looks up a key without knowing its section (`module.key` supported). |
| `ResolveLanguage(requested)` | The closest available language, or the base language. |
| `GetLanguages()` | Available languages as `LanguageInfo` (tag, native name, description, base file). |
| `Load()` | Loads all sections for the current language. |
| `LoadAsync(ct?)` | Async variant of `Load()`. Returns `Task<LanguageConfig>`. |
| `SetLanguage(ietf)` | Switches to a new language and reloads all sections synchronously. |
| `SetLanguageAsync(ietf, ct?)` | Async variant of `SetLanguage`. |
| `GetAvailableLanguages()` | Same languages as `GetLanguages()`, as `IReadOnlyList<(string Ietf, string NativeName)>`; the name is the `DisplayName`. |
| `LanguageChanged` | Event raised after every successful reload or language switch. |
| `Dispose()` | Stops file-system watchers and releases resources. |

### IniLanguageSectionAttribute

| Property | Type | Description |
|----------|------|-------------|
| `SectionName` | `string?` | Positional, optional. The `[SectionName]` block in the file. Derived from interface name when omitted. |
| `ModuleName` | `string?` | Named, optional. When set, the loader reads from `{basename}.{moduleName}.{ietf}.ini`. |

### LanguageSectionBase

Base class for all generated implementations.

| Member | Description |
|--------|-------------|
| `SectionName` | Abstract. The `[SectionName]` block this section reads from. |
| `ModuleName` | Abstract. Optional module name used in file selection. |
| `NormalizeKey(key)` | Static. Trims, lowercases, removes `_` and `-`. |
| `this[key]` | Returns translation for `key` (normalized); falls back to `###key###`. |
| `Format(key, args)` | Formats the translation; never throws (sentinel or unformatted text, reported to listeners). |
| `Count` | Number of loaded translation entries. |
| `ContainsKey(key)` / `TryGetValue(key, out value)` | Dictionary-style lookup (key is normalized). |

---

## See also

- [[Getting-Started]] — INI configuration basics and builder pattern
- [[Plugin-Registrations]] — three-phase `Create` / `RegisterSection` / `Load` pattern (INI config)
- [[File-Change-Monitoring]] — file-system monitoring concepts
- [[Async-Support]] — async build and load patterns
