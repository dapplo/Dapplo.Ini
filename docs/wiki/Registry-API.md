# Registry API Reference

## IniConfigRegistry

`IniConfigRegistry` is a thread-safe global registry that maps INI file basenames to their
loaded `IniConfig` instances.

The `.ini` extension is **optional** in every method that accepts a file name — `"myapp"` and
`"myapp.ini"` resolve to the same registry entry.

| Method | Description |
|--------|-------------|
| `ForFile(fileName)` | Returns a fluent `IniConfigBuilder` for the given file. The `.ini` extension is stripped if present. |
| `Get(fileName)` | Returns the `IniConfig` for the file; throws `KeyNotFoundException` if not registered |
| `Get()` | Returns the single registered `IniConfig`. Throws `InvalidOperationException` when 0 or more than one config is registered — use `Get(fileName)` in that case. |
| `TryGet(fileName, out config)` | Returns `false` if the file has not been registered |
| `GetSection<T>(fileName)` | Shortcut for `Get(fileName).GetSection<T>()` |
| `GetSection<T>()` | Shortcut for `Get().GetSection<T>()` — works only when exactly one config is registered |
| `AddSection<T>(fileName, section)` | Registers a section on an existing config without I/O — for plugin pre-init. See [[Plugin-Registrations]]. |
| `Unregister(fileName)` | Removes a registration (useful in tests) |
| `Clear()` | Removes all registrations (useful in tests) |

### No-argument convenience overloads

When an application registers exactly one INI file (the common case), the file name can be
omitted entirely:

```csharp
// Startup
IniConfigRegistry.ForFile("appsettings")   // ".ini" is optional here too
    .AddSearchPath(AppContext.BaseDirectory)
    .RegisterSection<IAppSettings>(new AppSettingsImpl())
    .Build();

// Anywhere in the app — no need to pass the file name
var settings = IniConfigRegistry.GetSection<IAppSettings>();
```

If more than one INI file is registered, `Get()` / `GetSection<T>()` throw
`InvalidOperationException` with a message that points to the named overload.

---

## IniConfig

| Member | Description |
|--------|-------------|
| `GetSection<T>()` | Returns the registered section instance; throws if not found. **Always returns the same object reference.** |
| `AddSection<T>(section)` | Registers a section without any file I/O. Returns `section` for chaining. For use between `Create()` and `Load()`. See [[Plugin-Registrations]]. |
| `AddSection(section)` | Non-generic overload; infers the interface type at runtime. Prefer the generic overload (AOT/trim safe). |
| `Load()` | Reads all files and applies value sources once for every registered section. Returns `this`. |
| `LoadAsync(ct)` | Async variant of `Load()`; also applies `IValueSourceAsync` sources and calls `IAfterLoadAsync` hooks. |
| `Save()` | Writes all section values to disk, honoring `IBeforeSave`/`IAfterSave` hooks |
| `SaveAsync(ct)` | Async variant of `Save()`; prefers `IBeforeSaveAsync`/`IAfterSaveAsync` hooks, falls back to sync hooks |
| `Reload()` | Re-reads all layers in place; section references remain valid |
| `ReloadAsync(ct)` | Async variant of `Reload()`; also applies `IValueSourceAsync` sources and calls `IAfterLoadAsync` hooks |
| `HasPendingChanges()` | Returns `true` when at least one registered section has unsaved changes |
| `RequestPostponedReload()` | Triggers a reload that was earlier postponed by a `FileChangedCallback` |
| `InitialLoadTask` | `Task.CompletedTask` after `Build()`; a pending `Task` after `BuildAsync()` that completes when loading finishes |
| `Metadata` | The `IniMetadata` read from the `[__metadata__]` section on the last load; `null` when the section was absent. See [[Migration]]. |
| `Reloaded` | Event raised after a successful `Reload()` or `ReloadAsync()` |
| `FileName` | The logical file name passed to `ForFile()` |
| `LoadedFromPath` | Resolved absolute path from which the file was actually read |
| `Dispose()` | Releases the file lock (if any) and stops the file-system watcher |

---

## IniConfigBuilder (fluent methods)

| Method | Description |
|--------|-------------|
| `AddSearchPath(path)` | Adds a directory to search for the INI file |
| `AddSearchPaths(paths)` | Adds multiple directories at once |
| `AddAppDataPath(applicationName)` | Adds `%APPDATA%\applicationName` (Linux: `~/.config/applicationName`) as a search path; creates the directory if absent |
| `SetWritablePath(path)` | Overrides the write target for new files when no existing file is found in any search path |
| `AddDefaultsFile(path)` | Registers a file that supplies default values (applied before the user file) |
| `AddConstantsFile(path)` | Registers a file that supplies admin-forced constants (applied last) |
| `AddValueSource(source)` | Registers an `IValueSource` (sync) — applied after constants during both `Build()` and `BuildAsync()` |
| `AddValueSource(asyncSource)` | Registers an `IValueSourceAsync` — applied after sync sources, only during `BuildAsync()` and `ReloadAsync()` |
| `LockFile()` | Holds the file open read-exclusively for the process lifetime |
| `MonitorFile([callback])` | Installs a `FileSystemWatcher`; optional callback controls reload decision |
| `WithEncoding(encoding)` | Sets the file encoding for reading and writing (default: UTF-8) |
| `AutoSaveInterval(interval)` | Starts an internal timer that saves when `HasPendingChanges()` is `true` |
| `SaveOnExit()` | Hooks `AppDomain.CurrentDomain.ProcessExit` to save on process termination |
| `OnUnknownKey(callback)` | Registers a global `UnknownKeyCallback` invoked for keys that have no matching section property. Used for migration scenarios. See [[Migration]]. |
| `EnableMetadata(version?, applicationName?)` | Opts in to writing a `[__metadata__]` section as the first section in the file on every save. Exposes `IniConfig.Metadata` to `IAfterLoad` hooks for version-gated migrations. See [[Migration]]. |
| `RegisterSection<T>(impl)` | Registers a section with its generated implementation |
| `AddListener(listener)` | Registers an `IIniConfigListener` for diagnostic events (file loaded/not found/saved/reloaded, unknown keys, conversion failures, errors). Zero overhead when no listener is registered. See [[Listeners]]. |
| `Create()` | Creates and registers the `IniConfig` without loading any files. Enables plugin sections to be added via `AddSection<T>()` before the first `Load()`. See [[Plugin-Registrations]]. |
| `Build()` | Loads the file synchronously, fires hooks, and registers the config in the global registry |
| `BuildAsync(ct)` | Async variant of `Build()`; also applies `IValueSourceAsync` sources and calls async lifecycle hooks. Registers in the global registry and sets `InitialLoadTask` before I/O starts, enabling DI fire-and-forget patterns |

---

## IIniSection

All generated section classes implement `IIniSection`:

| Member | Description |
|--------|-------------|
| `HasChanges` | `true` when the section has been modified since the last load or save |
| `SectionName` | The INI section name (`[SectionName]`) |

---

## LanguageConfigRegistry

`LanguageConfigRegistry` is a thread-safe global registry for language configurations.
It mirrors `IniConfigRegistry` exactly — including the `.ini`-optional basename convention
and the no-arg convenience overloads.

| Method | Description |
|--------|-------------|
| `ForFile(basename)` | Returns a fluent `LanguageConfigBuilder`. The `.ini` extension is stripped if present. Preferred entry point over `LanguageConfigBuilder.ForBasename()`. |
| `Get(basename)` | Returns the `LanguageConfig` for the given basename; throws `KeyNotFoundException` if not registered |
| `Get()` | Returns the single registered `LanguageConfig`. Throws `InvalidOperationException` when zero or more than one config is registered. |
| `TryGet(basename, out config)` | Returns `false` if not registered |
| `GetSection<T>(basename)` | Shortcut for `Get(basename).GetSection<T>()` |
| `GetSection<T>()` | Shortcut for `Get().GetSection<T>()` — works only when exactly one language config is registered |
| `Unregister(basename)` | Removes a registration (useful in tests) |
| `Clear()` | Removes all registrations (useful in tests) |

### Typical usage

```csharp
// Startup — register via LanguageConfigRegistry (preferred)
LanguageConfigRegistry.ForFile("myapp")        // "myapp" and "myapp.ini" are equivalent
    .AddSearchPath(langDir)
    .WithBaseLanguage("en-US")
    .RegisterSection<IMainLanguage>(new MainLanguageImpl())
    .Build();

// Anywhere in the app — single-registration shorthand
var lang = LanguageConfigRegistry.GetSection<IMainLanguage>();

// Or with an explicit basename when multiple language configs are registered:
var lang = LanguageConfigRegistry.GetSection<IMainLanguage>("myapp");
```

`LanguageConfigBuilder.Build()` / `BuildAsync()` register into `LanguageConfigRegistry`
automatically, so `ForBasename()` and `ForFile()` produce registry entries in the same way.

---

## IIniSection

---

## See also

- [[Plugin-Registrations]] — `Create()` + `AddSection<T>()` + `Load()` for plugin-based apps
- [[Migration]] — unknown-key callbacks, `IUnknownKey<TSelf>`, `EnableMetadata`, and version-gated upgrades
- [[Loading-Configuration]] — builder method examples
- [[Reloading]] — `Reload()` / `ReloadAsync()` and `HasPendingChanges()`
- [[Saving]] — `Save()` / `SaveAsync()` and save hooks
- [[Singleton-and-DI]] — `GetSection<T>()` and the singleton guarantee
- [[Async-Support]] — full async API guide
- [[Internationalization]] — `LanguageConfigRegistry`, language packs, and i18n builder API
