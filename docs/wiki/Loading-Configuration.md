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
    .AddAppDataPath("MyApplication")   // creates the folder if it does not exist
    .RegisterSection<IAppSettings>(new AppSettingsImpl())
    .Build();

// If the file does not exist yet it will be created in AppData on the first Save().
config.Save();
```

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
so no unnecessary I/O occurs. The timer is stopped automatically when `config.Dispose()` is called.

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

---

## See also

- [[Plugin-Registrations]] — `Create()` + `AddSection` + `Load()` for plugin-based apps
- [[Loading-Life-Cycle]] — value resolution order
- [[Reloading]] — `Reload()` / `ReloadAsync()` and the singleton guarantee
- [[Saving]] — `Save()` / `SaveAsync()` and `IBeforeSave` / `IAfterSave` hooks
- [[File-Locking]] — `LockFile()`
- [[File-Change-Monitoring]] — `MonitorFile()`
- [[Async-Support]] — `BuildAsync()` and other async APIs
