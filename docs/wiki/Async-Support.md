# Async Support

`Dapplo.Ini` ships with first-class async support.  Every major I/O operation has an
`*Async` twin while the synchronous API remains fully functional for simple scenarios.

---

## Quick-start — async build

```csharp
var config = await IniConfigRegistry.ForFile("appsettings.ini")
    .AddSearchPath(AppContext.BaseDirectory)
    .RegisterSection<IAppSettings>(new AppSettingsImpl())
    .BuildAsync(cancellationToken);

var settings = config.GetSection<IAppSettings>();
```

---

## BuildAsync and InitialLoadTask

### Basic async build

`BuildAsync` is the async counterpart of `Build`.  It reads all INI layers
(`AddDefaultsFile`, user file, `AddConstantsFile`, external value sources), fires async
lifecycle hooks, and registers the `IniConfig` in the global registry — all without
blocking a thread.

```csharp
var config = await IniConfigRegistry.ForFile("app.ini")
    .AddSearchPath(AppContext.BaseDirectory)
    .RegisterSection<IAppSettings>(new AppSettingsImpl())
    .BuildAsync(cancellationToken);
```

### DI pattern — fire-and-forget build

In dependency-injection scenarios you may need to register section objects (and the
`IniConfig`) as singletons **before** the INI file has been read.

`BuildAsync` handles this by registering the `IniConfig` in `IniConfigRegistry` and
exposing `InitialLoadTask` **before** any I/O starts.  Consumers can await
`InitialLoadTask` to know when values are ready.

```csharp
// Program.cs / Startup.cs
var section = new AppSettingsImpl();

// Start loading — do NOT await here; loading happens in the background
_ = IniConfigRegistry.ForFile("appsettings.ini")
    .AddSearchPath(AppContext.BaseDirectory)
    .RegisterSection<IAppSettings>(section)
    .BuildAsync();                                  // fire-and-forget

// The IniConfig is already registered at this point (before I/O completes)
var iniConfig = IniConfigRegistry.Get("appsettings.ini");

// Register as singletons for injection — references are stable after loading
builder.Services.AddSingleton<IAppSettings>(section);
builder.Services.AddSingleton(iniConfig);
```

```csharp
// A consumer that needs values to be ready before proceeding
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
        // Wait for initial load before reading values
        await _config.InitialLoadTask;

        Console.WriteLine(_settings.AppName);   // safe to read now
    }
}
```

> **Note:** `InitialLoadTask` is `Task.CompletedTask` after a synchronous `Build()`.
> Awaiting it in that case is a no-op and incurs no overhead.

---

## ReloadAsync

```csharp
// Reload all sections from disk without blocking the calling thread
await config.ReloadAsync(cancellationToken);

// The same Reloaded event fires after async reload too
config.Reloaded += (_, _) => Console.WriteLine("Reloaded!");
```

`ReloadAsync` re-applies the full loading life-cycle ([[Loading-Life-Cycle]]) in place,
just like `Reload()`, but asynchronously.  Section object references remain the same
(singleton guarantee is preserved).

### Async lifecycle hooks during reload

When `ReloadAsync` is used, sections that implement `IAfterLoadAsync` have their
`OnAfterLoadAsync` method called.  Sections that implement only the synchronous
`IAfterLoad` hook fall back to that automatically, so mixing sync and async sections in
the same config is supported.

---

## SaveAsync

```csharp
await config.SaveAsync(cancellationToken);
```

`SaveAsync` is the async counterpart of `Save()`.  It respects the cancellation token
and prefers async lifecycle hooks (`IBeforeSaveAsync`, `IAfterSaveAsync`) before falling
back to the synchronous `IBeforeSave` / `IAfterSave` hooks.

---

## Async lifecycle hooks

Three async lifecycle interfaces parallel the existing synchronous ones:

| Interface | Trigger | Return type |
|-----------|---------|-------------|
| `IAfterLoadAsync` | After `BuildAsync()` or `ReloadAsync()` | `Task` |
| `IBeforeSaveAsync` | Before writing to disk during `SaveAsync()` | `Task<bool>` — return `false` to cancel |
| `IAfterSaveAsync` | After a successful async write | `Task` |

These hooks are called only from the async code paths (`BuildAsync`, `ReloadAsync`,
`SaveAsync`).  The synchronous `Build()`, `Reload()`, and `Save()` continue to call the
synchronous hooks only.

### Implementing async hooks (instance methods)

Add the async interface to your section interface, then implement the methods in a
`partial class` file:

```csharp
// IMySettings.cs
[IniSection("App")]
public interface IMySettings : IIniSection, IAfterLoadAsync, IBeforeSaveAsync, IAfterSaveAsync
{
    string? Value { get; set; }
}
```

```csharp
// MySettingsImpl.cs  ← consumer-written partial alongside the generated MySettingsImpl.g.cs
public partial class MySettingsImpl
{
    public async Task OnAfterLoadAsync(CancellationToken cancellationToken)
    {
        // e.g. decrypt a value fetched from a secrets vault
        Value = await SecretsVault.DecryptAsync(Value, cancellationToken);
    }

    public async Task<bool> OnBeforeSaveAsync(CancellationToken cancellationToken)
    {
        // Validate remotely — return false to cancel the save
        return await RemoteValidator.IsValidAsync(Value, cancellationToken);
    }

    public Task OnAfterSaveAsync(CancellationToken cancellationToken)
    {
        Console.WriteLine("Settings saved!");
        return Task.CompletedTask;
    }
}
```

### Fallback behaviour

When `BuildAsync` / `ReloadAsync` are used:
- If the section implements `IAfterLoadAsync` → `OnAfterLoadAsync` is called.
- Otherwise, if the section implements `IAfterLoad` → `OnAfterLoad` is called (sync fallback).

When `SaveAsync` is used:
- If the section implements `IBeforeSaveAsync` → `OnBeforeSaveAsync` is called.
- Otherwise, if the section implements `IBeforeSave` → `OnBeforeSave` is called (sync fallback).
- Same pattern applies to `IAfterSaveAsync` / `IAfterSave`.

---

## IValueSourceAsync — async external value sources

`IValueSourceAsync` is the async counterpart of `IValueSource`.  Use it when values must
be fetched from an inherently asynchronous store such as a REST API, Azure App
Configuration, AWS Parameter Store, or any other networked service.

```csharp
public interface IValueSourceAsync
{
    Task<(bool Found, string? Value)> TryGetValueAsync(
        string sectionName, string key, CancellationToken cancellationToken = default);

    event EventHandler<ValueChangedEventArgs>? ValueChanged;
}
```

> **Why a tuple instead of `out`?**  `out` parameters are not permitted in async methods,
> so the return type is a `(bool Found, string? Value)` tuple.

### Example — REST API value source

```csharp
public sealed class RemoteConfigSource : IValueSourceAsync
{
    private readonly HttpClient _http;

    public event EventHandler<ValueChangedEventArgs>? ValueChanged;

    public RemoteConfigSource(HttpClient http) => _http = http;

    public async Task<(bool Found, string? Value)> TryGetValueAsync(
        string sectionName, string key, CancellationToken cancellationToken = default)
    {
        var response = await _http.GetAsync(
            $"/config/{sectionName}/{key}", cancellationToken);

        if (!response.IsSuccessStatusCode)
            return (false, null);

        var value = await response.Content.ReadAsStringAsync(cancellationToken);
        return (true, value);
    }
}
```

### Registering an async value source

```csharp
var config = await IniConfigRegistry.ForFile("app.ini")
    .AddSearchPath(AppContext.BaseDirectory)
    .AddValueSource(new RemoteConfigSource(httpClient))   // IValueSourceAsync overload
    .RegisterSection<IAppSettings>(new AppSettingsImpl())
    .BuildAsync(cancellationToken);
```

`AddValueSource` is overloaded to accept either `IValueSource` (sync) or
`IValueSourceAsync`.  Async sources are applied **after** all synchronous sources, in
registration order.

> **Important:** Async value sources are only consulted during `BuildAsync()` and
> `ReloadAsync()`.  The synchronous `Build()` and `Reload()` skip async sources.

### Triggering a reload when remote values change

```csharp
// Wire up a polling or push-notification mechanism:
remoteConfigSource.ValueChanged += async (_, _) =>
    await config.ReloadAsync();
```

---

## Ordering: sync sources vs async sources

When both sync and async sources are registered, values are applied in this order:

1. Synchronous `IValueSource` sources (registration order) — applied first
2. Asynchronous `IValueSourceAsync` sources (registration order) — applied after sync

The last source to set a value wins, so async sources take precedence over sync ones
when both target the same key.

---

## .NET Framework 4.8 compatibility

All async APIs (`Task`-based) are available on .NET Framework 4.8 as well as .NET 10+.
The `ValueTask`-based generic static-virtual variants of the lifecycle hooks (`IAfterLoad<TSelf>`,
etc.) require at least .NET 7 / C# 11 and are not available on .NET Framework 4.8.

---

## See also

- [[Loading-Configuration]] — `BuildAsync()` builder method
- [[Reloading]] — `ReloadAsync()` and the singleton guarantee
- [[Saving]] — `SaveAsync()` and async save hooks
- [[Lifecycle-Hooks]] — full lifecycle hook reference including async variants
- [[External-Value-Sources]] — `IValueSource` and `IValueSourceAsync`
- [[Singleton-and-DI]] — using `InitialLoadTask` in DI scenarios
- [[Registry-API]] — complete API reference including async members
