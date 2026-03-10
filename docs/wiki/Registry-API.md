# Registry API Reference

## IniConfigRegistry

`IniConfigRegistry` is a thread-safe global registry that maps file names to their
loaded configurations.

| Method | Description |
|--------|-------------|
| `ForFile(fileName)` | Returns a fluent `IniConfigBuilder` for the given file name |
| `Get(fileName)` | Returns the `IniConfig` for the file; throws if not registered |
| `TryGet(fileName, out config)` | Returns `false` if the file has not been registered |
| `GetSection<T>(fileName)` | Shortcut for `Get(fileName).GetSection<T>()` |
| `Unregister(fileName)` | Removes a registration (useful in tests) |
| `Clear()` | Removes all registrations (useful in tests) |

---

## IniConfig

| Member | Description |
|--------|-------------|
| `GetSection<T>()` | Returns the registered section instance; throws if not found. **Always returns the same object reference.** |
| `Save()` | Writes all section values to disk, honoring `IBeforeSave`/`IAfterSave` hooks |
| `SaveAsync(ct)` | Async variant of `Save()`; prefers `IBeforeSaveAsync`/`IAfterSaveAsync` hooks, falls back to sync hooks |
| `Reload()` | Re-reads all layers in place; section references remain valid |
| `ReloadAsync(ct)` | Async variant of `Reload()`; also applies `IValueSourceAsync` sources and calls `IAfterLoadAsync` hooks |
| `HasPendingChanges()` | Returns `true` when at least one registered section has unsaved changes |
| `RequestPostponedReload()` | Triggers a reload that was earlier postponed by a `FileChangedCallback` |
| `InitialLoadTask` | `Task.CompletedTask` after `Build()`; a pending `Task` after `BuildAsync()` that completes when loading finishes |
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
| `RegisterSection<T>(impl)` | Registers a section with its generated implementation |
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

## See also

- [[Loading-Configuration]] — builder method examples
- [[Reloading]] — `Reload()` / `ReloadAsync()` and `HasPendingChanges()`
- [[Saving]] — `Save()` / `SaveAsync()` and save hooks
- [[Singleton-and-DI]] — `GetSection<T>()` and the singleton guarantee
- [[Async-Support]] — full async API guide
