# Plugin / Distributed Registrations

Plugin-based applications face a challenge: plugins are loaded *after* the host has
already called `Build()`, so they cannot call the builder to register their own INI
sections.  `Dapplo.Ini` solves this with a **three-phase Create / AddSection / Load**
pattern that reads all INI files exactly once, after every section — host and plugin —
has been registered.  When the host's own settings must be read before the plugins are
known, use [late registration](#late-registration--adding-sections-after-the-load) instead.

---

## The three-phase pattern

### Phase 1 — host creates the config (no I/O)

Instead of calling `Build()`, the host calls `Create()`.  This constructs the
`IniConfig`, seeds it with the host's own sections, and registers it in the global
`IniConfigRegistry` — all without touching the file system.

```csharp
// Host startup — create, don't load yet
var config = IniConfigRegistry.ForFile("app.ini")
    .AddSearchPath(AppContext.BaseDirectory)
    .RegisterSection<IHostSettings>(new HostSettingsImpl())
    .Create();
```

At this point:
- The `config` reference can be passed directly to plugins — no guessing of file names.
- **No file has been read yet** — section properties return their compiled defaults
  (`[DefaultValue]` / `[IniValue(DefaultValue = …)]`), not `default(T)`. The same applies
  to sections added with `AddSection<T>()` before the load.

### Phase 2 — plugins add their sections (no I/O)

The host **provides the `IniConfig`** to each plugin.  Each plugin calls
`AddSection<T>()` on the config it receives:

```csharp
// Inside a plugin pre-init method — config is provided by the host
public void PreInit(IniConfig config)
{
    config.AddSection<IPluginSettings>(new PluginSettingsImpl());
}
```

`AddSection<T>()` is pure in-memory — it does not read or write any file.
Registering the same type again before the load replaces the earlier instance; a
*different* type that uses the same `SectionName` throws `InvalidOperationException`.

### Phase 3 — host loads everything at once (single file read)

After all plugins have registered their sections, the host calls `Load()` (or
`LoadAsync()`).  The full [[Loading-Life-Cycle]] is applied once for every registered
section:

```csharp
// Phase 3 — single load reads all files for every section
config.Load();

// Or the async equivalent
await config.LoadAsync(cancellationToken);
```

After `Load()` returns, all sections — host and plugin alike — have their values
populated from the INI file.

---

## Full example

```csharp
// ── Program.cs (host) ─────────────────────────────────────────────────────────

// Phase 1: create (no I/O)
var config = IniConfigRegistry.ForFile("app.ini")
    .AddSearchPath(AppContext.BaseDirectory)
    .AddDefaultsFile("/etc/myapp/defaults.ini")
    .RegisterSection<IHostSettings>(new HostSettingsImpl())
    .Create();

// Phase 2: load plugins, pass the config so each plugin can register its section
foreach (var plugin in PluginLoader.LoadAll())
    plugin.PreInit(config);

// Phase 3: load (single file read for every section)
config.Load();

// All sections are now populated
var hostSettings   = config.GetSection<IHostSettings>();
var pluginSettings = config.GetSection<IPluginSettings>();
```

```csharp
// ── PluginA.cs ────────────────────────────────────────────────────────────────

public class PluginA
{
    public void PreInit(IniConfig config)
    {
        // The host provides the config — no need to know or guess the file name
        config.AddSection<IPluginASettings>(new PluginASettingsImpl());
    }
}
```

### Alternative: registry-based lookup

When passing `IniConfig` directly is impractical (e.g. a plugin that is initialised
through a third-party plugin host), the plugin can retrieve the config from the global
registry, provided the host has already called `Create()`:

```csharp
// Plugin retrieves the config from the registry when it cannot be injected
IniConfigRegistry.Get("app.ini").AddSection<IPluginASettings>(new PluginASettingsImpl());
```

Or use the `IniConfigRegistry` convenience overload:

```csharp
IniConfigRegistry.AddSection<IPluginASettings>("app.ini", new PluginASettingsImpl());
```

---

## Late registration — adding sections after the load

The three-phase pattern needs every section to be registered **before** `Load()`. That is
a chicken-and-egg problem when the host's *own* settings decide which plugins are loaded
(e.g. an "excluded plugins" list): the host must read the file before the plugins exist.

Opt in with `AllowLateSectionRegistration()`. The parsed defaults, user and constants files
are then kept in memory after each load and reload, and `AddSection<T>()` /
`AddSectionAsync<T>()` after the load populate the new section **immediately** from that
data — no file is read again.

```csharp
// ── Host startup ──────────────────────────────────────────────────────────────
var config = IniConfigRegistry.ForFile("greenshot.ini")
    .AddAppDataPath("Greenshot")
    .AddDefaultsFile("greenshot-defaults.ini")
    .AddConstantsFile("greenshot-fixed.ini")
    .RegisterSection<ICoreConfiguration>(new CoreConfigurationImpl())
    .AllowLateSectionRegistration()
    .Build();                                   // the files are read once, here

// The host settings are loaded — now they can decide which plugins to start
var core = config.GetSection<ICoreConfiguration>();
foreach (var plugin in PluginLoader.Discover())
{
    if (core.ExcludePlugins?.Contains(plugin.Name) == true)
        continue;                               // its [Section] stays in the file, untouched
    plugin.Initialize(config);
}
```

```csharp
// ── Plugin ────────────────────────────────────────────────────────────────────
public void Initialize(IniConfig config)
{
    // Populated right away from the retained file data, without file I/O
    _settings = config.AddSection<IJiraConfiguration>(new JiraConfigurationImpl());
}
```

A section added after the load goes through the same steps as a load (see
[[Loading-Life-Cycle]]):

1. Reset to its compiled defaults (`[DefaultValue]`)
2. Defaults files, user file, constants files (constant keys are write-protected)
3. Synchronous `IValueSource`s — `AddSectionAsync<T>()` also applies `IValueSourceAsync` sources
4. Dirty flag cleared
5. `IAfterLoad` hook — `AddSectionAsync<T>()` prefers `IAfterLoadAsync`

Rules:

- Without `AllowLateSectionRegistration()`, `AddSection<T>()` after the load throws
  `InvalidOperationException` (the message names the option).
- After the load, adding a type that is already registered throws `InvalidOperationException`
  (other code may hold the existing instance — use `GetSection<T>()` / `TryGetSection<T>()`).
- A different type with the same `SectionName` throws, before and after the load.
- `AddSection<T>()` called from a lifecycle hook or listener (e.g. a host section's
  `IAfterLoad` that starts plugins) runs inline, as part of that operation.
- `IniConfig.IsLoaded` tells whether the first load has completed; `TryGetSection<T>()`
  checks for an optional plugin section without throwing.
- A listener that also implements `IIniConfigExtendedListener` (or derives from
  `IniConfigListenerBase`) is notified of every `AddSection` — see [[Listeners]].
- The option implies `PreserveUnknownSections()` (see below).

**Cost:** the parsed files stay in memory for the lifetime of the `IniConfig` — roughly
proportional to the size of the files on disk. Without the option nothing extra is kept.

### When to use which pattern

| Situation | Pattern |
|-----------|---------|
| All sections are known before the file is read | Three-phase `Create()` + `AddSection<T>()` + `Load()` — no extra memory |
| The host's settings decide which plugins are loaded | `AllowLateSectionRegistration()` |
| Plugins are loaded on demand, long after start-up | `AllowLateSectionRegistration()` |

Both can be combined: sections registered before the load are populated by the load,
later ones from the retained data.

---

## Keeping the settings of plugins that are not loaded

By default `Save()` writes only the registered sections, so the section of a plugin that
is excluded (or not loaded yet) would be **removed** from the file on the next save.
`PreserveUnknownSections()` prevents that — and is implied by `AllowLateSectionRegistration()`:

```csharp
var config = IniConfigRegistry.ForFile("app.ini")
    .AddSearchPath(AppContext.BaseDirectory)
    .RegisterSection<IHostSettings>(new HostSettingsImpl())
    .PreserveUnknownSections()          // keep [Section]s nobody registered
    .Build();
```

With the option, `Save()` starts from the parsed user file that was retained at the last
load, reload or save:

- Sections that are not registered are written back unchanged, in place, with their comments.
- Registered sections keep their position in the file and the comments above existing keys.
- Keys that a registered section does **not** declare are still removed, exactly as without
  the option — so migrations that rename keys keep cleaning up the old ones.
- After a successful save the written file becomes the retained user file.

Limits: blank lines and comments that are not attached to a section or key (a file header
followed by a blank line, trailing comments, comments separated from the next key or section
by a blank line) are not preserved,
and `#` comments are written as `;` comments.

`PreserveUnknownSections()` alone does **not** allow late registration.

---

## Using `Build()` vs `Create()`

| Method | I/O on call | When to use |
|--------|-------------|-------------|
| `Build()` | Immediate | Simple apps — no plugins that need to register sections before loading |
| `Create()` + `Load()` | Deferred | Plugin-based apps — all sections must be registered before the single load |

`Build()` is equivalent to calling `Create()` and then immediately calling `Load()` on
the returned config.  Existing code that uses `Build()` continues to work unchanged.

---

## Async variant

```csharp
// Phase 1
var config = IniConfigRegistry.ForFile("app.ini")
    .AddSearchPath(AppContext.BaseDirectory)
    .RegisterSection<IHostSettings>(new HostSettingsImpl())
    .Create();

// Phase 2 — plugins add their sections (synchronous, no I/O)
foreach (var plugin in PluginLoader.LoadAll())
    plugin.PreInit();

// Phase 3 — async load (applies IValueSourceAsync and IAfterLoadAsync)
await config.LoadAsync(cancellationToken);
```

---

## DI integration with deferred loading

`Create()` + `LoadAsync()` pairs naturally with the DI fire-and-forget pattern.
Sections and the `IniConfig` are added to the DI container immediately after `Create()`;
`LoadAsync()` runs in the background and consumers await `InitialLoadTask` before
reading values:

```csharp
// Phase 1 — create: config + section references are stable and injectable
var hostSection   = new HostSettingsImpl();
var pluginSection = new PluginSettingsImpl();

var config = IniConfigRegistry.ForFile("app.ini")
    .AddSearchPath(AppContext.BaseDirectory)
    .RegisterSection<IHostSettings>(hostSection)
    .Create();

config.AddSection<IPluginSettings>(pluginSection);

// Register as DI singletons before loading completes
builder.Services.AddSingleton<IHostSettings>(hostSection);
builder.Services.AddSingleton<IPluginSettings>(pluginSection);
builder.Services.AddSingleton(config);

// Phase 3 — fire-and-forget async load
var loadTask = config.LoadAsync(cancellationToken);
```

```csharp
// Consumer — await loading before reading values
public class MyWorker
{
    private readonly IHostSettings _settings;
    private readonly IniConfig     _config;

    public MyWorker(IHostSettings settings, IniConfig config)
    {
        _settings = settings;
        _config   = config;
    }

    public async Task StartAsync(CancellationToken ct)
    {
        await _config.InitialLoadTask;          // wait for the load to finish
        Console.WriteLine(_settings.AppName);  // safe to read now
    }
}
```

> **Note:** `InitialLoadTask` completes when the first `Load()` / `LoadAsync()` succeeds —
> also after `Create()` — and faults when that load fails. Until the load has run it
> stays pending, so consumers can await it no matter who starts the load.

---

## API summary

### `IniConfigBuilder`

| Method | Description |
|--------|-------------|
| `Create()` | Creates and registers the `IniConfig` without loading any files. Returns the `IniConfig` for the pre-load phase. |
| `AllowLateSectionRegistration()` | Keeps the parsed files in memory so `AddSection<T>()` also works after the load. Implies `PreserveUnknownSections()`. |
| `PreserveUnknownSections()` | `Save()` writes sections that are in the file but not registered back unchanged. |

### `IniConfig`

| Method | Description |
|--------|-------------|
| `AddSection<T>(section)` | Registers `section` under the interface type `T`; no file I/O. Returns `section` for chaining. After the load (with `AllowLateSectionRegistration()`) the section is populated immediately. |
| `AddSectionAsync<T>(section, ct)` | Like `AddSection<T>()`; after the load it also applies `IValueSourceAsync` sources and prefers `IAfterLoadAsync`. |
| `AddSection(section)` | Non-generic overload; infers the interface type by reflection (AOT-unfriendly — prefer the generic overload). |
| `TryGetSection<T>(out section)` | Returns `false` instead of throwing when `T` is not registered. |
| `IsLoaded` | `true` once the first `Load()` / `LoadAsync()` has completed. |
| `Load()` | Applies the full [[Loading-Life-Cycle]] once for all registered sections. Returns `this` for chaining. |
| `LoadAsync(ct)` | Async variant of `Load()`; also applies `IValueSourceAsync` sources and calls `IAfterLoadAsync` hooks. |

### `IniConfigRegistry`

| Method | Description |
|--------|-------------|
| `AddSection<T>(fileName, section)` | Convenience overload: `IniConfigRegistry.Get(fileName).AddSection<T>(section)` |
| `TryGetSection<T>(fileName, out section)` | Returns `false` when the config or the section is not registered. |
| `TryGetSection<T>(out section)` | Searches all registered configs — useful for optional, plugin-owned sections. |

---

## See also

- [[Loading-Configuration]] — `IniConfigBuilder` fluent API, `Create()` and `Build()`
- [[Loading-Life-Cycle]] — exact resolution order applied by `Load()`
- [[Registry-API]] — complete API reference
- [[Singleton-and-DI]] — using the config and its sections as DI singletons
- [[Async-Support]] — `BuildAsync`, `LoadAsync`, `InitialLoadTask`, and async value sources
