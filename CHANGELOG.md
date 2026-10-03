# Changelog

All notable changes to **Dapplo.Ini** are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

---

## [Unreleased]

Internationalization improvements for Greenshot's move from XML language files to `Dapplo.Ini.Internationalization`.
All new behaviour is opt-in; existing code keeps working.

### Added
- `LanguageConfigBuilder.AllowLateSectionRegistration()`: `LanguageConfig.RegisterSection<T>()` after the load loads that one section right away for the current language and fallback chain; new `LanguageConfig.RegisterSectionAsync<T>()` does the same asynchronously. Other sections are not reloaded and `LanguageChanged` is not raised; later language switches and file-change reloads include the section (with `MonitorFiles()` its directory is watched from then on). Without the option a section registered after the load still stays empty until the next load/switch, but this is now reported via `IIniConfigExtendedListener.OnSectionAdded(name, loaded: false)` and `IIniConfigListener.OnError("RegisterSection", …)` (not thrown).
- `LanguageConfigBuilder.MergeSearchPaths()`: for every tag of the load chain the language file of every search path is read, lowest priority first, so a higher priority path overrides single keys (e.g. a partial user file in `%APPDATA%`). A section with its own path is only merged when that path is one of the search paths.
- Reserved `[__language__]` section with `Description=…` in a language file; `LanguageConfig.GetLanguages()` returns `LanguageInfo` (`Ietf`, `NativeName`, `Description`, `HasBaseFile`, `DisplayName`), sorted by tag. `GetAvailableLanguages()` now returns the description when present, otherwise the native name, otherwise the tag.
- `LanguageConfig.ResolveLanguage(requested)`: exact match, legacy tags without hyphen (`ptBR` → `pt-BR`), region/parent/child with the same language subtag (`de` → `de-DE`, `de-AT` → `de`/`de-DE`, `zh-Hant-TW` → `zh-TW`, `zh-HK` → `zh-TW`; private-use variants like `de-x-franconia` only on an exact match), else the base language. `LanguageConfigBuilder.ResolveLanguages()` applies it to `WithCurrentLanguage` and `SetLanguage`; `LanguageConfig.RequestedLanguage` keeps the requested tag.
- `LanguageConfig.TryGetTranslation(key, out value)` / `GetTranslation(key)` look up a key without knowing its section; `module.key` / `section.key` only search that module or section. `LanguageConfigRegistry.TryGetTranslation(...)` / `GetTranslation(key)` for the single-config case.
- `ILanguageConfigListener` (`OnLanguageResolved`, `OnTranslationNotFound`, `OnFormatFailed`) and `LanguageConfigListenerBase`.
- `LanguageConfig.IsLoaded`.

### Changed
- `LanguageSectionBase.Format(key, args)` never throws: a missing key returns the `###key###` sentinel, a translation that cannot be formatted is returned unformatted; both are reported via `ILanguageConfigListener`.
- `LanguageConfig` serialises reading the files for load, language switch, file-change reload and section registration; registering on a plugin thread is safe while other threads switch the language or read translations (previously this could corrupt the section list). Translations are applied and `PropertyChanged`, `LanguageChanged` and listeners are raised after that lock is released, on the calling thread, so handlers may marshal to the UI thread, switch the language or register sections; every section ends up in the language of the latest operation. Listener callbacks of an operation (`OnFileLoaded`, `OnFileNotFound`, …) are raised when its files have been read, before the translations are applied. A failed or cancelled language switch keeps the previous language (previously `CurrentLanguage` already returned the new one). When a listener or handler throws, the rest of the operation still runs (all sections are updated), every failure is reported via `OnError`, and the first is re-thrown.
- Language file monitoring also reacts to created, deleted and renamed files (in the folders that exist at load time).
- The files of one load are read once, even when several sections use the same file.
- Fewer heap allocations when reading files (same results; differential-tested against the previous implementation):
  - Language files are streamed through a pooled buffer instead of being read into one string (which landed on the large object heap for big files), each file is read once for all sections, keys are normalized in a stack buffer, files are applied most specific first so overridden fallback texts are never allocated, and on .NET 9+ key strings and unchanged values of the previous load are reused. A language switch with 300 texts allocates ~47 KB instead of ~730 KB; a reload of unchanged files allocates no strings.
  - `TryGetTranslation`, the section indexer, `ContainsKey` and `TryGetValue` do not allocate (on .NET Framework only keys with `_`, `-` or surrounding whitespace allocate their normalized form).
  - `IniFileParser.ParseFile` / `ParseFileAsync` parse from a pooled buffer instead of a string; quotes and escape sequences are processed on spans (one string per value); section dictionaries and lists are created with the right size.
  - Generated sections match keys through a static case-insensitive index instead of `key.ToLowerInvariant()` (which allocated a string per key in `OnRawValueSet`, `IsKnownKey`, `GetValue` and more), and value-type properties are converted without boxing: applying values from a file allocates nothing per key. Rebuild projects that use the generator to get this.
  - `BoolConverter` and the sub-key `%XX` encoding no longer allocate temporary strings.
- Generator: the `UpdateTranslations` override of `INotifyPropertyChanged` language sections is table-driven instead of using two locals per property; for 600 properties the first language load no longer spends ~100 ms in the JIT. Events and their order are unchanged.

**Behaviour changes** — check these when upgrading:
- `GetAvailableLanguages()`: module files (`{basename}.{module}.{ietf}.ini`) no longer add languages of their own (unless every registered section is a module section), the name of a registered module is no longer taken for a tag (`greenshot.box.ini` with a registered `box` module), only tags with a two or three letter language subtag are listed, and the result is sorted by tag. The second tuple value is the file's `[__language__]` description when there is one.
- A section named `__language__` cannot be registered (`ArgumentException`).
- `LanguageConfig.RegisterSection` now notifies `IIniConfigExtendedListener.OnSectionAdded(name, loaded)` (also before the load, with `loaded: false`), like `IniConfig.AddSection`.

### Fixed
- Wiki: module sections are only read from their module file; the page claimed a fallback to the main file.

---

## [1.1]

### Added
- Migration support: `IUnknownKey<TSelf>` interface, `OnUnknownKey` callback, `TrackAssemblyVersion`, and optional `[__metadata__]` section (`EnableMetadata`) for version-gated upgrades.
- Async support: `BuildAsync`, `ReloadAsync`, `SaveAsync`, `IAfterLoadAsync`, `IBeforeSaveAsync`, `IAfterSaveAsync`, and `IValueSourceAsync` for remote configuration sources (REST APIs, etc.).
- `InitialLoadTask` property on `IniConfig` for DI-friendly async loading — sections are available as singletons immediately while the load completes in the background.
- Plugin / distributed registrations: three-phase `Create()` + `AddSection<T>()` + `Load()` pattern so plugins can register sections before the single file read.
- Built-in collection converters: `ListConverter<T>`, `ArrayConverter<T>`, and `DictionaryConverter<TKey,TValue>`; the `ValueConverterRegistry` creates them automatically for `List<T>`, `IList<T>`, `T[]`, and `Dictionary<TKey,TValue>`.
- Source generator (`Dapplo.Ini.Generator`) detects `IAfterLoad`, `IBeforeSave`, `IAfterSave`, and `IDataValidation` marker interfaces on section interfaces and emits bridge implementations automatically.
- `IniConfigBuilder.EnableMetadata(version?, applicationName?)` prepends a `[__metadata__]` section (Version, CreatedBy, SavedOn) to saved files; `IniConfig.Metadata` exposes the last-read metadata.
- Targets both `net48` and `net10.0`.
- `IniConfigBuilder.SetOverrideDirectory(directory)` pins the INI file to a directory, e.g. from a `--config-dir` command-line option: the file is read from and saved to that directory only, even when it exists in another search path. Defaults files are looked for there first, constants files never (so users cannot bypass admin constants). `null`/empty/whitespace is a no-op; the path is made absolute and created; an unusable directory is reported via `IIniConfigListener.OnError` (operation `"OverrideDirectory"`) and the search paths are used instead — nothing is thrown. `IniConfig.OverrideDirectory` tells whether the override is active; `IniConfig.LoadedFromPath` is also the save target for a new file.
- `IniConfigBuilder.AllowLateSectionRegistration()`: `IniConfig.AddSection<T>()` / `AddSectionAsync<T>()` also work after the load. The parsed defaults, user and constants files are kept in memory (≈ the size of the files) and a late section is populated from them without file I/O: compiled defaults, defaults files, user file, constants (protected), value sources (async ones too with `AddSectionAsync`), dirty flag cleared, then `IAfterLoad` (`AddSectionAsync` prefers `IAfterLoadAsync`). Implies `PreserveUnknownSections()`.
- `IniConfigBuilder.PreserveUnknownSections()`: `Save()` starts from the parsed user file, so sections nobody registered (e.g. of an excluded or not-yet-loaded plugin) are written back unchanged with their comments; registered sections keep their position and existing comments; undeclared keys of registered sections are still removed.
- `IniConfigBuilder.PreserveFormatting()` (implies `PreserveUnknownSections()`): blank lines, comments exactly as written (including `#`, file header and trailing comments) and unparseable lines are written back where they were; backed by `IniParserOptions.PreserveTrivia` and `LeadingTrivia` / `TrailingTrivia` on `IniSection`, `IniEntry` and `IniFile`.
- `IniConfig.TryGetSection<T>(out T?)`, `IniConfigRegistry.TryGetSection<T>(fileName, out T?)` and `IniConfigRegistry.TryGetSection<T>(out T?)` (searches all registered configs).
- `IniConfig.IsLoaded`.
- `IIniConfigExtendedListener` — optional listener interface (separate because `net48` has no default interface members) with `OnSectionAdded(sectionName, loaded)` for `AddSection` registrations and `OnValueSourceIgnored(sectionName, key, value)` when a constant wins over a value source; `IniConfigListenerBase` implements all listener callbacks as empty virtual methods.
- Converters for `short`, `ushort`, `sbyte` and `char`; `bool` also reads `1`/`0`, `yes`/`no`, `on`/`off`.
- Generator: generic async hooks `IAfterLoadAsync<T>`, `IBeforeSaveAsync<T>`, `IAfterSaveAsync<T>` are bridged; properties inherited from base section interfaces are implemented; nested section interfaces are supported; `[DefaultValue(typeof(T), "…")]` and array defaults work.
- Reload raises `PropertyChanged` (for `INotifyPropertyChanged` sections) for every value the reload changed, after the reload has completed, on the reloading thread.
- Exceptions in background work (auto-save timer, file-change reload, process-exit save, language file watcher) are reported via `IIniConfigListener.OnError` instead of crashing the process.
- Generator diagnostics: `DINI001` duplicate INI key (error), `DINI002` property type without a built-in converter (info), `DINI003` generic section interface (warning), `DINI005` two interfaces generating the same class (error), `DINI101` language property not a get-only `string` (error). See the wiki page Defining-Sections.
- `Dapplo.Ini.Generator.Tests`: runs both generators on source snippets (diagnostics, partial interfaces, caching).

### Changed

**Behaviour changes** — check these when upgrading (e.g. Greenshot):
- `Load`, `Reload`, `Save` and `AddSection` are serialised by one lifecycle gate. `Save()` now **waits** for a running operation instead of silently returning; `Save()` from inside a save hook returns immediately; `Save()` from a load/reload hook or listener runs directly.
- `Load()` / `Reload()` (and async variants) called from a hook or listener of a running operation throw `InvalidOperationException`.
- Dirty flags are cleared **before** the `IAfterLoad` hooks: changes made in hooks (migrations) stay dirty and are saved, also by auto-save.
- Constants win over value sources: value sources are skipped for keys set by a constants file (reported via `IIniConfigExtendedListener.OnValueSourceIgnored`).
- `AddSection<T>()` after the load throws `InvalidOperationException` unless `AllowLateSectionRegistration()` is enabled. After the load, adding an already registered type throws; a different type with an already used `SectionName` throws (also before the load). Before the load, registering the same type again still replaces it.
- Sections registered before the load (builder `RegisterSection` + `Create()`, or `AddSection` before `Load`) return their `[DefaultValue]`s instead of `default(T)`.
- **Breaking:** `LanguageConfigBuilder.UseFallback()` / `UseFallback(ietf)` are replaced by `UseFallbackLanguage(ietf)`. The argument-less form had no effect (the base language is always the floor) and can be removed. The load chain is now: parents of the fallback language, the fallback language, parents of the requested language, the requested language — each language once.
- `AddAppDataPath` is the write target when the INI file is not found in any search path, wherever it is in the search order (previously the first existing search path, so `AddSearchPath(exeDir).AddAppDataPath(...)` tried to create the file next to the executable). `SetWritablePath` still wins.
- `[__metadata__]` `SavedOn` is written as ISO 8601 with UTC offset (`2026-09-30T11:18:00+02:00`) instead of the locale format.
- Dictionary sub-keys (`Property.key = value`) encode `%`, `=`, `:`, line breaks and leading/trailing whitespace in the key as `%XX`, so such keys round-trip; other keys are written as before.
- Generated language section files are named `{Namespace}.{Class}.g.cs` like section files, so equal class names in different namespaces no longer collide.
- `InitialLoadTask` completes for `Create()` + `Load()` / `LoadAsync()` too (previously only `BuildAsync`), and faults when the load fails.
- Registering a config for a file name that is already registered disposes the previous config; `Build()` / `BuildAsync()` unregister and dispose the config when loading fails.
- Interfaces without an `I` prefix keep their name (`Interval` → section `[Interval]`, class `IntervalImpl`; previously `nterval`).
- `double` / `float` / `decimal` no longer accept thousands separators (`"1,5"` was read as `15`).
- `T?` properties keep `null`: an empty value reads as `null`, `null` is written as an empty value.
- List/dictionary elements containing the separator (or starting with a quote or whitespace) are written in double quotes with doubled inner quotes; unquoted input reads as before.
- Values containing line breaks are always written with escape sequences; enable `EscapeSequences` on the parser to read them back as line breaks.
- `[Required]` also rejects whitespace-only strings.
- A repeated `[Section]` header continues the existing section instead of replacing it.

Other changes:
- Saving is atomic: written to a temp file next to the INI and swapped in with `File.Replace` (with retries); falls back to writing in place for symbolic links or when no temp file can be created in the folder.
- Multi-line comments get `; ` on every line.
- Assigning an unchanged value to a value-type or `string` property no longer marks the section dirty.
- `Reload()` / `ReloadAsync()` resolve bare-named defaults/constants files through the search paths, like `Load()`.
- Post-load setup (file lock, monitor, save-on-exit, auto-save) runs once, after the first successful load; the auto-save timer skips a tick while another operation is running.
- File monitoring also reacts to `Created` and `Renamed` events (editors that save by replacing the file).
- `Uri` keeps relative URIs and is written as `OriginalString`; empty list elements are `""` on all frameworks; string-keyed dictionaries read from the inline `key=value,…` form are case-insensitive.
- Transactions: `Commit()` marks changes dirty, updates raw values and raises the property events; only properties assigned during the transaction are committed. Setters no longer round-trip through converters and check constants before changing anything.
- Generator: `[Range]` works on `double`, `long`, `decimal`, nullable types and the `typeof` form; `[MaxLength]` on lists; `[RegularExpression]` on non-strings; the non-generic `IDataValidation` can be implemented with a normal public `ValidateProperty` method in a partial class, also together with validation attributes (previously a duplicate-member compile error or endless recursion).
- i18n: the fallback chain loads every parent culture (`zh`, `zh-Hant`, then `zh-Hant-TW`); translations are swapped atomically; values are trimmed after `=`.
- Project renamed from `Dapplo.IniConfig` / `Dapplo.Ini.Config` to **`Dapplo.Ini`**; all namespaces updated accordingly.
- `IniConfig`, `IniConfigRegistry`, and `IniConfigBuilder` moved to the `Dapplo.Ini` namespace; `IniSectionBase` remains in `Dapplo.Ini.Configuration`.

### Fixed
- The parser no longer treats two consecutive `\n` line breaks as one (`\n` followed by `\n` was handled like `\r\n`), so empty lines are counted correctly.
- Line continuation needs an odd number of trailing backslashes (`C:\Temp\\` no longer continues) and never swallows a following section header.
- With `QuotedValues` and without `EscapeSequences` the parser undoes the writer's quote escaping, so quoted values round-trip.
- A leading BOM in `IniFileParser.Parse(string)` is ignored.
- `IniFileWriter` without explicit options keeps the file's `AssignmentSeparator`; explicit options always win.
- Generator: incremental caching works (the pipeline carries only strings and values, so unrelated edits no longer regenerate every section); a `partial` section interface is generated once; string literals (section names, descriptions, defaults) escape line breaks, `\u2028`/`\u2029` and control characters; language sections support nested interfaces and skip static members and indexers.
- Registering a converter updates list/array/dictionary converters that were created for that type earlier.
- An auto-save can no longer write a half-reloaded state, and a section registered on another thread no longer disturbs a running save (lifecycle gate).

---

## [1.0.0-beta] — Initial beta release

### Added
- Define configuration sections as annotated interfaces (`[IniSection]`, `[IniValue]`).
- Roslyn source generator creates concrete `*Impl` classes automatically — no boilerplate.
- Layered loading: defaults file → user file → admin constants file → external value sources (`IValueSource`).
- In-place reload with singleton guarantee — section object references stay valid after `Reload()`.
- File locking (`LockFile()`) to prevent external modification while the application runs.
- File-change monitoring (`MonitorFile()`) via `FileSystemWatcher` with optional `ReloadDecision` callback to postpone or skip reloads.
- `INotifyDataErrorInfo` validation through `IDataValidation<TSelf>`.
- Transactional updates via `ITransactional` — `Begin()`, `Commit()`, `Rollback()`.
- `INotifyPropertyChanged` / `INotifyPropertyChanging` baked into every generated section.
- Lifecycle hooks (`IAfterLoad`, `IBeforeSave`, `IAfterSave`) implementable directly in the section interface using C# 11 static virtuals.
- Extensible value converter system (`IValueConverter`, `ValueConverterRegistry`).
- `IniConfigRegistry` — thread-safe global registry mapping file names to loaded configurations.
- `AddAppDataPath(applicationName)` helper that resolves `%APPDATA%\<app>` (Linux: `~/.config/<app>`) and creates the directory if absent.

[Unreleased]: https://github.com/dapplo/Dapplo.Ini/compare/v1.0.0-beta...HEAD
[1.0.0-beta]: https://github.com/dapplo/Dapplo.Ini/releases/tag/v1.0.0-beta
