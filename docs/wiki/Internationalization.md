# Internationalization

`Dapplo.Ini` includes built-in support for `.ini`-based language packs via the
`Dapplo.Ini.Internationalization` namespace. Translation strings are defined as
interface properties; the source generator creates a concrete implementation
automatically.

---

## Quick start

```csharp
using Dapplo.Ini.Internationalization.Attributes;
using Dapplo.Ini.Internationalization.Configuration;

// 1. Define a language section interface
[IniLanguageSection]
public interface IMainLanguage
{
    string WelcomeMessage { get; }
    string CancelButton   { get; }
}

// 2. Load at application startup
using var langConfig = LanguageConfigBuilder.ForBasename("myapp")
    .AddSearchPath("/path/to/lang")
    .WithBaseLanguage("en-US")
    .WithCurrentLanguage("de-DE")
    .RegisterSection<IMainLanguage>(new MainLanguageImpl())   // generated class
    .Build();

// 3. Use the translations
var lang = langConfig.GetSection<IMainLanguage>();
Console.WriteLine(lang.WelcomeMessage);   // "Willkommen bei der Anwendung!"

// 4. Switch language at runtime
langConfig.SetLanguage("fr-FR");
```

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

- Each line: `key=value` (everything after the first `=` is the raw value).
- Keys are **trimmed**; underscores `_` and dashes `-` are removed before
  comparison, so `Welcome_Message`, `WelcomeMessage`, and `welcomemessage` all
  refer to the same property.
- Lookup is **case-insensitive**.

### Value escape sequences

| Sequence | Becomes |
|----------|---------|
| `\n` | Newline |
| `\t` | Tab |
| `\\` | Backslash |

### Example file

```ini
; myapp.en-US.ini — all keys must be inside a [SectionName] block

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

---

## LanguageConfigBuilder — fluent API

`LanguageConfigBuilder.ForBasename(name)` is the single entry point. It names
the file pattern `{basename}.{ietf}.ini`.

```csharp
using var config = LanguageConfigBuilder.ForBasename("myapp")
    .AddSearchPath("/path/to/lang")      // directory to search for language files
    .WithBaseLanguage("en-US")           // REQUIRED — the reference language
    .WithCurrentLanguage("de-DE")        // optional — defaults to base language
    .RegisterSection<IMainLanguage>(new MainLanguageImpl())
    .RegisterSection<ICoreLanguage>(new CoreLanguageImpl())
    .UseFallback()                        // fall back to base language for missing keys
    .MonitorFiles()                       // reload when files change on disk
    .Build();                             // load immediately (sync)
```

### Builder methods

| Method | Description |
|--------|-------------|
| `ForBasename(name)` | **Static factory.** Names the `{basename}.{ietf}.ini` file pattern. |
| `AddSearchPath(path)` | Directory to search for language pack files. |
| `WithBaseLanguage(ietf)` | **Required.** The reference language that is always loaded first. |
| `WithCurrentLanguage(ietf)` | Language to activate on the first load. Defaults to the base language. |
| `RegisterSection<T>(impl, path?)` | Registers a language section; optional path overrides `AddSearchPath`. |
| `UseFallback(ietf?)` | When a key is missing from the active language, use the base language (or the specified `ietf` tag) instead of the `###key###` sentinel. |
| `MonitorFiles()` | Enables file-system monitoring. When any language file changes, all sections are reloaded and `LanguageChanged` is raised. |
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

1. Base / fallback language loaded first (provides the floor for missing keys).
2. Parent culture — e.g. `de` when requesting `de-DE`.
3. Specific culture — `de-DE` overwrites keys from the parent.

This means switching to `de-DE` when only a partial `de-DE` file exists will
still show the German base strings from `de.ini` rather than the `###key###`
sentinel.

### `UseFallback()`

Call `UseFallback()` on the builder to opt in to using the base language as a
second safety net for keys that are absent even in the most-specific file:

```csharp
.UseFallback()               // uses WithBaseLanguage() value as fallback
.UseFallback("en-US")        // uses an explicit fallback language
```

---

## Discovering available languages

`GetAvailableLanguages()` scans the configured search path(s) for files matching
the `{basename}.*.ini` pattern and returns the IETF tags together with the
native name of each culture:

```csharp
var languages = config.GetAvailableLanguages();
// e.g. [("en-US", "English (United States)"), ("de-DE", "Deutsch (Deutschland)")]

foreach (var (ietf, nativeName) in languages)
    Console.WriteLine($"{ietf}: {nativeName}");
```

This is suitable for populating a language picker in a settings UI.

---

## Plugin / deferred loading

The same three-phase pattern used by `IniConfigBuilder.Create()` is available
for language configs. The host creates the config without loading; plugins
register their own sections; the host triggers loading once:

```csharp
// ── Host startup ──────────────────────────────────────────────────────────────

// Phase 1 — create without loading
var langConfig = LanguageConfigBuilder.ForBasename("myapp")
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

For module sections (those with `ModuleName` set), the loader first looks for
the dedicated module file (`{basename}.{moduleName}.{ietf}.ini`). If it does not
exist the keys are still read from the main file under the matching
`[SectionName]` block.

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
steps.

```csharp
using var config = LanguageConfigBuilder.ForBasename("myapp")
    .AddSearchPath(langDir)
    .WithBaseLanguage("en-US")
    .RegisterSection<IMainLanguage>(new MainLanguageImpl())
    .MonitorFiles()
    .Build();

config.LanguageChanged += (_, _) => RefreshUi();
```

---

## Complete API reference

### LanguageConfigBuilder

| Method | Description |
|--------|-------------|
| `ForBasename(name)` | Static factory. Names the file pattern `{basename}.{ietf}.ini`. |
| `AddSearchPath(path)` | Adds a search directory for language files. |
| `WithBaseLanguage(ietf)` | Sets the base (reference) language. **Required.** |
| `WithCurrentLanguage(ietf)` | Sets the initial active language. |
| `RegisterSection<T>(impl, path?)` | Registers a section; optional `path` overrides the default search path for this section only. |
| `UseFallback(ietf?)` | Enables base-language fallback for missing keys. |
| `MonitorFiles()` | Enables file-system change monitoring with debounce. |
| `Create()` | Creates `LanguageConfig` without loading. Plugin-friendly deferred pattern. |
| `Build()` | Creates and loads `LanguageConfig` synchronously. Returns `LanguageConfig`. |
| `BuildAsync(ct?)` | Creates and loads `LanguageConfig` asynchronously. Returns `Task<LanguageConfig>`. |

### LanguageConfig

| Member | Description |
|--------|-------------|
| `CurrentLanguage` | The IETF tag of the currently active language. |
| `BaseLanguage` | The base (reference) language supplied at build time. |
| `GetSection<T>()` | Returns the registered section instance for interface `T`; throws if not registered. |
| `RegisterSection<T>(impl, path?)` | Registers a section after `Create()` (plugin pattern). Returns `impl` for chaining. |
| `Load()` | Loads all sections for the current language. |
| `LoadAsync(ct?)` | Async variant of `Load()`. Returns `Task<LanguageConfig>`. |
| `SetLanguage(ietf)` | Switches to a new language and reloads all sections synchronously. |
| `SetLanguageAsync(ietf, ct?)` | Async variant of `SetLanguage`. |
| `GetAvailableLanguages()` | Scans directories and returns `IReadOnlyList<(string Ietf, string NativeName)>`. |
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
| `Count` | Number of loaded translation entries. |
| `ContainsKey(key)` / `TryGetValue(key, out value)` | Dictionary-style lookup (key is normalized). |

---

## See also

- [[Getting-Started]] — INI configuration basics and builder pattern
- [[Plugin-Registrations]] — three-phase `Create` / `RegisterSection` / `Load` pattern (INI config)
- [[File-Change-Monitoring]] — file-system monitoring concepts
- [[Async-Support]] — async build and load patterns
