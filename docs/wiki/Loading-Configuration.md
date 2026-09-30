# Loading Configuration

Use the fluent `IniConfigBuilder` API to configure a single INI file:

```csharp
using var config = IniConfigRegistry.ForFile("myapp.ini")
    // Search directories – tried in order until the file is found
    .AddSearchPath("/etc/myapp")
    .AddSearchPath(AppContext.BaseDirectory)
    // Optional: apply an admin-supplied defaults file first
    .AddDefaultsFile("/etc/myapp/defaults.ini")
    // Optional: apply admin-forced constants last (users cannot override these)
    .AddConstantsFile("/etc/myapp/constants.ini")
    // Optional: keep the file locked against external writes
    .LockFile()
    // Optional: automatically reload when the file changes on disk
    .MonitorFile()
    // Register each section with its generated implementation
    .RegisterSection<IDbSettings>(new DbSettingsImpl())
    .RegisterSection<IAppSettings>(new AppSettingsImpl())
    // Build loads the file, fires IAfterLoad hooks, and registers in the global registry
    .Build();
```

> **Note:** `IniConfig` implements `IDisposable`. Use `using` to ensure the file lock
> and file-system watcher are released when the application exits.

---

## Async build

Use `BuildAsync` to load configuration without blocking the calling thread.
This is recommended for UI applications (WPF, Avalonia, WinForms) and ASP.NET Core
services that load configuration on startup:

```csharp
using var config = await IniConfigRegistry.ForFile("myapp.ini")
    .AddSearchPath(AppContext.BaseDirectory)
    .RegisterSection<IDbSettings>(new DbSettingsImpl())
    .RegisterSection<IAppSettings>(new AppSettingsImpl())
    .BuildAsync(cancellationToken);
```

See [[Async-Support]] for the fire-and-forget DI pattern using `InitialLoadTask`.

---

## Storing configuration in AppData

For desktop applications the natural home for a user INI file is
`%APPDATA%\<ApplicationName>` on Windows (`~/.config/<ApplicationName>` on Linux,
`~/Library/Application Support/<ApplicationName>` on macOS).
Use `AddAppDataPath` to add that directory as a search path and write target in one call:

```csharp
using var config = IniConfigRegistry.ForFile("myapp.ini")
    .AddSearchPath(AppContext.BaseDirectory) // e.g. a portable or admin-provided file next to the exe
    .AddAppDataPath("MyApplication")         // creates the folder if it does not exist
    .RegisterSection<IAppSettings>(new AppSettingsImpl())
    .Build();

// If the file does not exist in any search path it is created in AppData on the first Save().
config.Save();
```

The file is still read from the first search path that contains it, and saved back there.
Only when no search path contains it is AppData the write target — wherever `AddAppDataPath`
is in the search order, so a fresh install never tries to write next to the executable
(e.g. into `Program Files`). `SetWritablePath` takes precedence over this fallback.

---

## Specifying an explicit write target

When you need to read from one location (e.g. a read-only system directory) and write
to a different location, use `SetWritablePath`:

```csharp
var targetPath = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
    "MyCompany", "MyApp", "user.ini");

using var config = IniConfigRegistry.ForFile("defaults.ini")
    .AddSearchPath("/etc/myapp")          // read from here
    .SetWritablePath(targetPath)          // write to here on first Save()
    .RegisterSection<IAppSettings>(new AppSettingsImpl())
    .Build();
```

---

## Pinning the file to a directory (`--config-dir`)

Applications often offer a command-line option that points to the directory with the
configuration. `SetOverrideDirectory` pins the INI file to that directory:

```csharp
using var config = IniConfigRegistry.ForFile("myapp.ini")
    .SetOverrideDirectory(options.ConfigDirectory)   // null/empty: no override
    .AddSearchPath(AppContext.BaseDirectory)
    .AddAppDataPath("MyApplication")
    .AddDefaultsFile("myapp-defaults.ini")
    .AddConstantsFile("myapp-fixed.ini")
    .RegisterSection<IAppSettings>(new AppSettingsImpl())
    .Build();

logger.Info($"Using {config.LoadedFromPath}");
```

| | With an active override directory |
|---|---|
| INI file | Read from **and** saved to the override directory only — even when it does not exist there yet and does exist in a search path (e.g. AppData). `SetWritablePath` and the AppData fallback are not used. |
| Defaults files (bare name) | Looked up in the override directory first, then in the search paths. |
| Constants files (bare name) | **Never** read from the override directory, only from the search paths. A user cannot replace the constants an administrator placed next to the executable by putting a copy into the directory they control. |

- `null`, empty or whitespace does nothing, so an optional command-line value can be passed
  unconditionally.
- A relative path is made absolute against the current directory; a missing directory is created.
- When the directory cannot be created or written to (missing permissions, a file with that
  name, an invalid path), `IIniConfigListener.OnError` is called with the operation
  `"OverrideDirectory"` and the file is located through the search paths as if no override had
  been set. No exception is thrown, so a bad command-line value never stops the application.
- `config.OverrideDirectory` is the absolute directory while the override is active, otherwise `null`.
- `config.LoadedFromPath` is the file that was loaded or, when it did not exist, where `Save()`
  will create it — log it or pass it on.

### Where a new file is created

When the INI file is not found, `Save()` creates it in the first of:

1. the override directory (`SetOverrideDirectory`)
2. the explicit write target (`SetWritablePath`)
3. the AppData directory (`AddAppDataPath`)
4. the first search path that exists

---

## Configuring encoding

By default the INI file is read and written as UTF-8. Use `WithEncoding` when working
with legacy files that use a different encoding:

```csharp
using var config = IniConfigRegistry.ForFile("legacy.ini")
    .AddSearchPath(".")
    .WithEncoding(Encoding.Latin1)
    .RegisterSection<IAppSettings>(new AppSettingsImpl())
    .Build();
```

---

## Auto-save on a timer

Call `AutoSaveInterval` to flush dirty sections to disk automatically at a regular interval:

```csharp
using var config = IniConfigRegistry.ForFile("app.ini")
    .AddSearchPath(".")
    .AutoSaveInterval(TimeSpan.FromSeconds(30))
    .RegisterSection<IAppSettings>(new AppSettingsImpl())
    .Build();
```

The internal timer only writes to disk when `HasPendingChanges()` returns `true`,
so no unnecessary I/O occurs. When a load, reload or save is running at that moment, the tick
is skipped and the next one tries again. Exceptions are reported to listeners via `OnError`
instead of crashing the process (see [[Listeners]]). The timer is stopped automatically when
`config.Dispose()` is called.

---

## Save on process exit

Call `SaveOnExit` to automatically flush dirty sections when the process terminates:

```csharp
using var config = IniConfigRegistry.ForFile("app.ini")
    .AddSearchPath(".")
    .SaveOnExit()
    .RegisterSection<IAppSettings>(new AppSettingsImpl())
    .Build();
// config.Dispose() unregisters the ProcessExit handler automatically.
```

An exception during the exit save is reported via `IIniConfigListener.OnError` only.

The lock, monitor, save-on-exit and auto-save options take effect once, after the first
successful load — whether it comes from `Build()`, `BuildAsync()` or `Create()` + `Load()`.

---

## Empty-over-null semantics

Call `EmptyWhenNull()` to make every reference-type property across all registered sections
return an empty value (e.g. `string.Empty`, empty list, empty array) instead of `null` when no
value is present in the INI file and no explicit `[DefaultValue]` is set:

```csharp
using var config = IniConfigRegistry.ForFile("app.ini")
    .AddSearchPath(AppContext.BaseDirectory)
    .EmptyWhenNull()                                    // applies to all sections
    .RegisterSection<IAppSettings>(new AppSettingsImpl())
    .RegisterSection<IDbSettings>(new DbSettingsImpl())
    .Build();
```

To scope the behaviour to a single section use `[IniSection(EmptyWhenNull = true)]`, or to
a single property use `[IniValue(EmptyWhenNull = true)]`.  See [[Empty-When-Null]] for the
complete guide and precedence rules.

---

## Configuring parser behaviour

Use the dedicated fluent methods to control how the INI file is interpreted during
load and reload.  These methods all configure an internal `IniParserOptions` instance
that is forwarded to the parser on every file read.

### Duplicate key handling

```csharp
using var config = IniConfigRegistry.ForFile("app.ini")
    .AddSearchPath(".")
    .WithDuplicateKeyHandling(DuplicateKeyHandling.FirstWins)
    .RegisterSection<IAppSettings>(new AppSettingsImpl())
    .Build();
```

| Method | Behaviour |
|--------|-----------|
| `.WithDuplicateKeyHandling(LastWins)` | Last definition wins **(default)** |
| `.WithDuplicateKeyHandling(FirstWins)` | First definition is kept; later duplicates ignored |
| `.WithDuplicateKeyHandling(ThrowError)` | Throws `InvalidOperationException` on any duplicate |

### Quoted values

```csharp
using var config = IniConfigRegistry.ForFile("app.ini")
    .AddSearchPath(".")
    .EnableQuotedValues()     // key = "value" → value (quotes stripped)
    .RegisterSection<IAppSettings>(new AppSettingsImpl())
    .Build();
```

### Escape sequences

```csharp
using var config = IniConfigRegistry.ForFile("app.ini")
    .AddSearchPath(".")
    .EnableEscapeSequences()  // key = C:\\Path → C:\Path
    .RegisterSection<IAppSettings>(new AppSettingsImpl())
    .Build();
```

Decoded sequences: `\\`, `\n`, `\r`, `\t`, `\0`, `\"`, `\'`, `\a`, `\b`, `\xHH`.

### Line continuation

```csharp
using var config = IniConfigRegistry.ForFile("app.ini")
    .AddSearchPath(".")
    .EnableLineContinuation()  // trailing \ joins the next line
    .RegisterSection<IAppSettings>(new AppSettingsImpl())
    .Build();
```

### Case-sensitive lookups

```csharp
using var config = IniConfigRegistry.ForFile("app.ini")
    .AddSearchPath(".")
    .CaseSensitiveKeys()       // AppName ≠ appname
    .CaseSensitiveSections()   // [General] ≠ [GENERAL]
    .RegisterSection<IAppSettings>(new AppSettingsImpl())
    .Build();
```

### Combining all options

```csharp
using var config = IniConfigRegistry.ForFile("app.ini")
    .AddSearchPath(".")
    .EnableEscapeSequences()
    .EnableQuotedValues()
    .EnableLineContinuation()
    .CaseSensitiveKeys()
    .WithDuplicateKeyHandling(DuplicateKeyHandling.ThrowError)
    .RegisterSection<IAppSettings>(new AppSettingsImpl())
    .Build();
```

Alternatively, build an `IniParserOptions` object and supply it in one call:

```csharp
var opts = new IniParserOptions
{
    EscapeSequences      = true,
    QuotedValues         = true,
    CaseSensitiveKeys    = true,
    DuplicateKeyHandling = DuplicateKeyHandling.ThrowError,
};

using var config = IniConfigRegistry.ForFile("app.ini")
    .AddSearchPath(".")
    .WithParserOptions(opts)
    .RegisterSection<IAppSettings>(new AppSettingsImpl())
    .Build();
```

See [[Parser-Options]] for the complete reference, including tables showing the exact
effect of each option.

---

## Deferred loading for plugin scenarios

When plugins need to register their own INI sections before the file is read, use
`Create()` instead of `Build()`.  `Create()` constructs the `IniConfig`, registers it in
the global registry, and returns it — **without reading any file**.  Plugins can then
call `AddSection<T>()` on the config, and the host calls `Load()` once when all sections
are registered.

See [[Plugin-Registrations]] for the full three-phase pattern and examples.

```csharp
// Phase 1 — create (no I/O); config is immediately visible in IniConfigRegistry
var config = IniConfigRegistry.ForFile("myapp.ini")
    .AddSearchPath(AppContext.BaseDirectory)
    .RegisterSection<IHostSettings>(new HostSettingsImpl())
    .Create();

// Phase 2 — plugins add their sections (no I/O)
foreach (var plugin in LoadPlugins())
    plugin.PreInit();   // calls config.AddSection<IPluginSettings>(...)

// Phase 3 — single load reads all files for every section
config.Load();
// Or: await config.LoadAsync(cancellationToken);
```

Sections registered before the load already return their `[DefaultValue]`s.
`config.IsLoaded` tells whether the load has completed, and `config.InitialLoadTask`
completes (or faults) with it.

---

## Late section registration and preserving unknown sections

When the host must read its own settings before the plugins are known, opt in to adding
sections **after** the load:

```csharp
var config = IniConfigRegistry.ForFile("myapp.ini")
    .AddSearchPath(AppContext.BaseDirectory)
    .RegisterSection<IHostSettings>(new HostSettingsImpl())
    .AllowLateSectionRegistration()     // keep the parsed files; implies PreserveUnknownSections()
    .Build();

// Later — populated immediately from the retained data, no file I/O
var plugin = config.AddSection<IPluginSettings>(new PluginSettingsImpl());

// Optional sections can be looked up without exceptions
if (config.TryGetSection<IPluginSettings>(out var settings)) { /* … */ }
```

| Method | Description |
|--------|-------------|
| `AllowLateSectionRegistration()` | Keeps the parsed defaults, user and constants files in memory after each load/reload, so `AddSection<T>()` / `AddSectionAsync<T>()` work after the load. Without it, `AddSection<T>()` after the load throws `InvalidOperationException`. |
| `PreserveUnknownSections()` | `Save()` writes sections that are in the file but not registered (e.g. of a plugin that is not loaded) back unchanged, instead of dropping them. |

See [[Plugin-Registrations#late-registration--adding-sections-after-the-load]] for the rules
and a complete plugin-host example.

---

## See also

- [[Plugin-Registrations]] — `Create()` + `AddSection` + `Load()` for plugin-based apps
- [[Loading-Life-Cycle]] — value resolution order
- [[Listeners]] — `OnError`, including `"OverrideDirectory"`
- [[Reloading]] — `Reload()` / `ReloadAsync()` and the singleton guarantee
- [[Saving]] — `Save()` / `SaveAsync()` and `IBeforeSave` / `IAfterSave` hooks
- [[File-Locking]] — `LockFile()`
- [[File-Change-Monitoring]] — `MonitorFile()`
- [[Async-Support]] — `BuildAsync()` and other async APIs
- [[Runtime-Only-and-Constants]] — constants-file protection and `IsConstant(key)`
- [[Empty-When-Null]] — `EmptyWhenNull()` builder method and property/section-level equivalents
- [[Parser-Options]] — `IniParserOptions` — configurable duplicate-key handling, quoted values, escape sequences, line continuation, and case sensitivity
