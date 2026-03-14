# Singleton Guarantee and Dependency Injection

**`GetSection<T>()` always returns the same object reference**, even after `Reload()`.

This is a deliberate design choice: the framework updates the *properties* of the existing
section object in place during a reload, so any code that holds a reference to the section
will automatically see the new values without re-querying the registry.

---

## ASP.NET Core / Microsoft.Extensions.DependencyInjection

```csharp
var config = IniConfigRegistry.ForFile("appsettings.ini")
    .AddSearchPath(AppContext.BaseDirectory)
    .RegisterSection<IAppSettings>(new AppSettingsImpl())
    .Build();

// Register the section as a singleton — the reference stays valid after Reload()
builder.Services.AddSingleton(config.GetSection<IAppSettings>());

// Alternatively, expose the IniConfig itself for manual reload triggering:
builder.Services.AddSingleton(config);
```

---

## Async DI pattern — BuildAsync and InitialLoadTask

When using `BuildAsync`, configuration loading happens asynchronously.  The `IniConfig`
and its sections are registered in the DI container *before* loading completes;
consumers can await `InitialLoadTask` to know when values are ready.

```csharp
// Program.cs
var section = new AppSettingsImpl();

// Start loading asynchronously — do NOT await here
_ = IniConfigRegistry.ForFile("appsettings.ini")
    .AddSearchPath(AppContext.BaseDirectory)
    .RegisterSection<IAppSettings>(section)
    .BuildAsync();                                     // fire-and-forget

// IniConfig is already registered at this point
var iniConfig = IniConfigRegistry.Get("appsettings.ini");

// Register as singletons — references are stable, loading continues in background
builder.Services.AddSingleton<IAppSettings>(section);
builder.Services.AddSingleton(iniConfig);
```

```csharp
// A consumer that reads config values — waits for the initial load first
public class MyWorker
{
    private readonly IAppSettings _settings;
    private readonly IniConfig _config;

    public MyWorker(IAppSettings settings, IniConfig config)
    {
        _settings = settings;
        _config   = config;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // Block until loading is complete before reading any values
        await _config.InitialLoadTask;

        Console.WriteLine(_settings.AppName);   // safe to read now
    }
}
```

> **Note:** `InitialLoadTask` is `Task.CompletedTask` when `Build()` (sync) is used.
> Awaiting it in that case is a no-op.

---

## Constructor injection

```csharp
// Constructor injection — works seamlessly
public class MyService
{
    private readonly IAppSettings _settings;

    public MyService(IAppSettings settings)
    {
        _settings = settings;  // always up-to-date, even after a reload
    }
}
```

---

## Global registry shortcut

You can retrieve a section without holding a reference to `IniConfig` by using the global
`IniConfigRegistry`:

```csharp
// Anywhere in the application, after Build() has been called:
var settings = IniConfigRegistry.GetSection<IAppSettings>("appsettings.ini");
```

---

## Plugin-based apps — DI with deferred loading

When plugins register their own sections before the INI file is read, use `Create()` +
`AddSection<T>()` + `LoadAsync()`.  Section references are stable and injectable from the
moment `Create()` returns:

```csharp
var hostSection   = new HostSettingsImpl();
var pluginSection = new PluginSettingsImpl();

// Phase 1 — create (no I/O); references are stable for DI
var config = IniConfigRegistry.ForFile("app.ini")
    .AddSearchPath(AppContext.BaseDirectory)
    .RegisterSection<IHostSettings>(hostSection)
    .Create();

// Phase 2 — plugins add their sections
config.AddSection<IPluginSettings>(pluginSection);

// Register as DI singletons
builder.Services.AddSingleton<IHostSettings>(hostSection);
builder.Services.AddSingleton<IPluginSettings>(pluginSection);
builder.Services.AddSingleton(config);

// Phase 3 — load (fire-and-forget if needed)
await config.LoadAsync(cancellationToken);
```

See [[Plugin-Registrations]] for the full pattern and more examples.

---

## See also

- [[Plugin-Registrations]] — three-phase pattern for plugin-based apps
- [[Reloading]] — in-place reload and the `Reloaded` event
- [[Registry-API]] — full `IniConfigRegistry` and `IniConfig` API reference
- [[Async-Support]] — `BuildAsync`, `InitialLoadTask`, and the full async API
