# Dapplo.Ini

A powerful, source-generator–backed INI file configuration framework for .NET.

- ✅ Define configuration sections as **annotated interfaces**
- ✅ Concrete classes are **auto-generated** — no boilerplate
- ✅ **Layered** loading: defaults → user file → admin constants → external sources
- ✅ **In-place reload** with singleton guarantee (safe for DI)
- ✅ **File locking** to prevent external modification
- ✅ **File-change monitoring** with optional consumer hook
- ✅ **INotifyDataErrorInfo** validation for WPF / Avalonia / WinForms binding
- ✅ **Transactional** updates with rollback support
- ✅ **INotifyPropertyChanged** / **INotifyPropertyChanging** baked in
- ✅ **Lifecycle hooks** implementable directly in the section interface via static virtuals (C# 11+)
- ✅ Extensible **value converter** system (custom converters for encryption etc.)
- ✅ **Async support** — `BuildAsync`, `ReloadAsync`, `SaveAsync`, async lifecycle hooks, and `IValueSourceAsync` for REST APIs / remote stores
- ✅ **DI-friendly async loading** — `InitialLoadTask` lets consumers await the initial load while sections are injected as singletons immediately

---

## Table of Contents

1. [Quick start](#quick-start)
2. [Defining section interfaces](#defining-section-interfaces)
3. [Complete loading life-cycle](#complete-loading-life-cycle)
4. [Loading configuration](#loading-configuration)
    - [Storing configuration in AppData](#storing-configuration-in-appdata)
    - [Specifying an explicit write target](#specifying-an-explicit-write-target)
5. [Reloading configuration](#reloading-configuration)
6. [Saving configuration](#saving-configuration)
7. [File locking](#file-locking)
8. [File-change monitoring](#file-change-monitoring)
9. [External value sources](#external-value-sources)
10. [Validation (INotifyDataErrorInfo)](#validation-inotifydataerrorinfo)
11. [Lifecycle hooks](#lifecycle-hooks)
    - [New: generic static-virtual pattern (recommended)](#new-generic-static-virtual-pattern-recommended)
    - [Legacy: partial-class pattern (.NET Framework / instance methods)](#legacy-partial-class-pattern-net-framework--instance-methods)
12. [Async support](#async-support)
    - [BuildAsync and InitialLoadTask](#buildasync-and-initialloadtask)
    - [ReloadAsync and SaveAsync](#reloadasync-and-saveasync)
    - [IValueSourceAsync — async external sources](#ivaluesourceasync--async-external-sources)
13. [Singleton guarantee and dependency injection](#singleton-guarantee-and-dependency-injection)
14. [Transactional updates](#transactional-updates)
15. [Property-change notifications](#property-change-notifications)
16. [Value converters](#value-converters)
    - [Encrypting sensitive values](#encrypting-sensitive-values)
17. [Registry API reference](#registry-api-reference)

---

## Quick start

```csharp
// 1. Define a section interface
[IniSection("App", Description = "Application settings")]
public interface IAppSettings : IIniSection
{
    [IniValue(DefaultValue = "MyApp")]
    string? AppName { get; set; }

    [IniValue(DefaultValue = "8080")]
    int Port { get; set; }
}

// 2. Load at application startup
var config = IniConfigRegistry
    .ForFile("appsettings.ini")
    .AddSearchPath(AppContext.BaseDirectory)
    .RegisterSection<IAppSettings>(new AppSettingsImpl())  // generated class
    .Build();

// 3. Read values — the section object is a stable singleton
var settings = config.GetSection<IAppSettings>();
Console.WriteLine($"{settings.AppName} is listening on port {settings.Port}");

// 4. Save changes
settings.AppName = "MyApp v2";
config.Save();
```

> **Tip:** You can also retrieve the config later without holding a reference:
> ```csharp
> var settings = IniConfigRegistry.GetSection<IAppSettings>("appsettings.ini");
> ```

---

## Defining section interfaces

Every configuration section is a plain C# interface annotated with `[IniSection]`.
The source generator (`Dapplo.Ini.Generator`) creates a concrete `partial class`
implementation automatically.

### Generated class naming convention

The generator derives the concrete class name from the interface name:

| Interface name | Generated class name | Generated file |
|---------------|---------------------|----------------|
| `IAppSettings` | `AppSettingsImpl` | `AppSettingsImpl.g.cs` |
| `IDbConfig` | `DbConfigImpl` | `DbConfigImpl.g.cs` |
| `IUserProfile` | `UserProfileImpl` | `UserProfileImpl.g.cs` |
| `ServerConfig` *(no leading I)* | `ServerConfigImpl` | `ServerConfigImpl.g.cs` |

The rule is: strip a leading `I` (if present) and append `Impl`.
The file is generated into your project's intermediate output folder and compiled automatically.

Because the generated class is declared `partial`, you can extend it with your own
code in a separate file — see [Legacy: partial-class pattern](#legacy-partial-class-pattern-net-framework--instance-methods) below.

### `[IniSection]` attribute

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `SectionName` (ctor) | `string?` | interface name minus leading `I` | Name of the `[Section]` in the INI file |
| `Description` | `string?` | `null` | Written as a comment above the section header |

```csharp
// Section name derived from interface name → "UserProfile"
[IniSection]
public interface IUserProfile : IIniSection { /* … */ }

// Explicit section name
[IniSection("user")]
public interface IUserProfile : IIniSection { /* … */ }
```

### `[IniValue]` attribute

Annotate each property with `[IniValue]` to control its INI representation.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `KeyName` | `string?` | property name | Key name in the INI file |
| `DefaultValue` | `string?` | `null` | Raw string parsed via the type's converter |
| `Description` | `string?` | `null` | Written as a comment above the key |
| `ReadOnly` | `bool` | `false` | When `true`, the value is never written to disk |
| `Transactional` | `bool` | `false` | When `true`, the property participates in transactions |
| `NotifyPropertyChanged` | `bool` | `false` | Raises `INotifyPropertyChanged` / `INotifyPropertyChanging` |

```csharp
[IniSection("Database")]
public interface IDbSettings : IIniSection
{
    [IniValue(DefaultValue = "localhost", Description = "Database host", KeyName = "host")]
    string? Host { get; set; }

    [IniValue(DefaultValue = "5432")]
    int Port { get; set; }

    [IniValue(DefaultValue = "True", NotifyPropertyChanged = true)]
    bool EnableSsl { get; set; }
}
```

### Read-only properties

A property declared with **only a getter** (`{ get; }`) is automatically treated as
read-only by the source generator: its value is loaded from the INI file and defaults
are applied, but it is **never written back to disk** when the config is saved.

The generated implementation class still exposes a **public setter** so the framework
and any code holding a reference to the concrete class can assign values; the setter is
simply absent from the interface.

```csharp
[IniSection("AppInfo")]
public interface IAppInfo : IIniSection
{
    // Getter-only: loaded from INI, never saved back.
    [IniValue(DefaultValue = "1.0.0")]
    string? Version { get; }

    // Regular read-write property.
    [IniValue(DefaultValue = "MyApp")]
    string? Name { get; set; }
}
```

The same "never save" behaviour can also be requested explicitly on a `{ get; set; }`
property via `[IniValue(ReadOnly = true)]`, which keeps the setter on the interface
while still preventing saves.

See [[Defining-Sections#read-only-properties]] for a full comparison.

---

## Complete loading life-cycle

Understanding the exact order in which values are resolved helps you predict the final
state of any property after `Build()` or `Reload()`.

```
┌─────────────────────────────────────────────────────────────────────┐
│ STEP 1 — Reset to compiled defaults                                 │
│   Each section's properties are set to their [IniValue(DefaultValue │
│   = …)] values (or the type default when DefaultValue is absent).   │
└──────────────────────────────┬──────────────────────────────────────┘
                               │
┌──────────────────────────────▼──────────────────────────────────────┐
│ STEP 2 — Apply defaults files (AddDefaultsFile order)               │
│   Each defaults file is read with IniFileParser and merged into the │
│   sections. Later files win over earlier ones.                      │
└──────────────────────────────┬──────────────────────────────────────┘
                               │
┌──────────────────────────────▼──────────────────────────────────────┐
│ STEP 3 — Locate and apply the user INI file                         │
│   Search directories (AddSearchPath order) are tried until the file │
│   is found. Values in the user file override all defaults.          │
│   If not found, the first writable search directory is stored for   │
│   a future Save().                                                  │
└──────────────────────────────┬──────────────────────────────────────┘
                               │
┌──────────────────────────────▼──────────────────────────────────────┐
│ STEP 4 — Apply constants files (AddConstantsFile order)             │
│   Admin-forced values that cannot be overridden by users.           │
│   These win over everything above.                                  │
└──────────────────────────────┬──────────────────────────────────────┘
                               │
┌──────────────────────────────▼──────────────────────────────────────┐
│ STEP 5 — Apply external value sources (AddValueSource order)        │
│   Each registered IValueSource is queried for every section/key.    │
│   Sources are applied in registration order; the last one wins.     │
└──────────────────────────────┬──────────────────────────────────────┘
                               │
┌──────────────────────────────▼──────────────────────────────────────┐
│ STEP 6 — Fire IAfterLoad hooks                                      │
│   OnAfterLoad() is called on every section that implements          │
│   IAfterLoad / IAfterLoad<TSelf>. Use this for normalization,       │
│   decryption, derived-value calculation, etc.                       │
└──────────────────────────────┬──────────────────────────────────────┘
                               │
┌──────────────────────────────▼──────────────────────────────────────┐
│ STEP 7 — (Build only) Acquire file lock / start file monitor        │
│   If LockFile() or MonitorFile() was configured, the file lock is   │
│   acquired and/or the FileSystemWatcher is started.                 │
└──────────────────────────────┬──────────────────────────────────────┘
                               │
                          ✅ Ready
```

**Type conversion** happens at step 3 and 4 whenever `SetRawValue` is called.
The raw string from the INI file is passed through the registered `IValueConverter<T>`
for the property's type. Built-in converters cover all common .NET primitive types;
custom converters can be registered with `ValueConverterRegistry.Register()`.

---

## Loading configuration

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

### Storing configuration in AppData

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

### Specifying an explicit write target

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

## Reloading configuration

`IniConfig.Reload()` re-applies the full loading life-cycle (steps 1–6 above) **in place**,
updating the property values of the already-registered section objects without creating new
instances. This is the **singleton guarantee**: object references obtained from `GetSection<T>()`
remain valid forever — including after a reload.

```csharp
// Explicitly trigger a reload at any time:
config.Reload();

// Async reload — does not block the calling thread:
await config.ReloadAsync(cancellationToken);

// React to the reload completing (fires after both Reload() and ReloadAsync()):
config.Reloaded += (sender, _) =>
    Console.WriteLine($"{((IniConfig)sender!).FileName} was reloaded.");
```

---

## Saving configuration

```csharp
// Saves all section values back to the file that was loaded (or the first writable search path).
config.Save();

// Async variant — does not block the calling thread:
await config.SaveAsync(cancellationToken);

// IBeforeSave / IBeforeSaveAsync hooks run first — returning false cancels the save.
// IAfterSave / IAfterSaveAsync hooks run after a successful write.
```

> **Note:** Own `Save()` / `SaveAsync()` calls are automatically detected and never trigger
> the file-change monitor, so a save does not cause an unwanted reload loop.

---

## File locking

Call `.LockFile()` on the builder to hold the INI file open with an exclusive write-lock
for the entire application lifetime:

```csharp
using var config = IniConfigRegistry.ForFile("myapp.ini")
    .AddSearchPath(AppContext.BaseDirectory)
    .LockFile()           // ← prevents external writes while the app is running
    .RegisterSection<IAppSettings>(new AppSettingsImpl())
    .Build();

// The lock is released when config.Dispose() is called (or when the using block exits).
```

> **Note:** The lock allows other processes to **read** the file but prevents writes.

---

## File-change monitoring

Call `.MonitorFile()` to automatically reload when the file is changed by another process.
An optional `FileChangedCallback` lets you control the reload decision:

```csharp
using var config = IniConfigRegistry.ForFile("myapp.ini")
    .AddSearchPath(AppContext.BaseDirectory)
    .MonitorFile(filePath =>
    {
        // Decide what to do when the file changes externally
        if (AppIsStartingUp)
            return ReloadDecision.Postpone;   // reload later
        if (UserIsEditing)
            return ReloadDecision.Ignore;     // skip this change
        return ReloadDecision.Reload;         // reload immediately (default)
    })
    .RegisterSection<IAppSettings>(new AppSettingsImpl())
    .Build();

// When you are ready to apply a postponed reload:
config.RequestPostponedReload();
```

| `ReloadDecision` value | Effect |
|------------------------|--------|
| `Reload` | Reload immediately (this is the default when no callback is supplied) |
| `Ignore` | Skip this notification — no reload occurs |
| `Postpone` | Delay until `RequestPostponedReload()` is called |

---

## External value sources

`IValueSource` is an extensibility point that lets you inject values from **any external
system** — Windows Registry, environment variables, a web service, a secrets vault, etc.
For async sources such as REST APIs, use `IValueSourceAsync` (see [Async support](#async-support)).

```csharp
// Implement IValueSource (synchronous)
public sealed class EnvironmentValueSource : IValueSource
{
    public event EventHandler<ValueChangedEventArgs>? ValueChanged;

    public bool TryGetValue(string sectionName, string key, out string? value)
    {
        // Env var convention: SECTION__KEY (double underscore separator)
        var envVar = $"{sectionName}__{key}".ToUpperInvariant();
        value = Environment.GetEnvironmentVariable(envVar);
        return value is not null;
    }
}

// Register it — sources are applied after the user file and constants
using var config = IniConfigRegistry.ForFile("myapp.ini")
    .AddSearchPath(AppContext.BaseDirectory)
    .AddValueSource(new EnvironmentValueSource())
    .RegisterSection<IAppSettings>(new AppSettingsImpl())
    .Build();
```

When a source's value changes at runtime, raise `ValueChanged` and call `config.Reload()`
to re-apply all sources and update the section properties.

```csharp
// Notify the framework that a value changed (e.g. from a background polling thread):
valueSource.RaiseChanged(sectionName: "App", key: "FeatureFlag");
config.Reload();
```

---

## Validation (INotifyDataErrorInfo)

Implement `IDataValidation<TSelf>` on your section interface to enable WPF/Avalonia/WinForms
data binding validation.  The source generator automatically implements
`System.ComponentModel.INotifyDataErrorInfo` on the generated class and re-runs validation
whenever a property annotated with `NotifyPropertyChanged = true` changes.

```csharp
[IniSection("Server")]
public interface IServerSettings : IIniSection, IDataValidation<IServerSettings>
{
    [IniValue(DefaultValue = "8080", NotifyPropertyChanged = true)]
    int Port { get; set; }

    [IniValue(DefaultValue = "localhost", NotifyPropertyChanged = true)]
    string? Host { get; set; }

    // ── Validation logic — lives directly inside the interface (C# 11+) ──────
    static new IEnumerable<string> ValidateProperty(IServerSettings self, string propertyName)
    {
        return propertyName switch
        {
            nameof(Port) when self.Port is < 1 or > 65535
                => new[] { "Port must be between 1 and 65535." },
            nameof(Host) when string.IsNullOrWhiteSpace(self.Host)
                => new[] { "Host must not be empty." },
            _ => Array.Empty<string>()
        };
    }
}
```

The generated class automatically implements `INotifyDataErrorInfo`, so WPF/Avalonia
bindings pick up errors without any additional code:

```xml
<!-- WPF XAML — Binding.ValidatesOnNotifyDataErrors=True is the default in .NET -->
<TextBox Text="{Binding Port, UpdateSourceTrigger=PropertyChanged}" />
```

For .NET Framework / instance-method style, implement the non-generic `IDataValidation`:

```csharp
[IniSection("Server")]
public interface IServerSettings : IIniSection, IDataValidation
{
    [IniValue(DefaultValue = "8080", NotifyPropertyChanged = true)]
    int Port { get; set; }
}

// Partial class provides the instance implementation
public partial class ServerSettingsImpl
{
    public IEnumerable<string> ValidateProperty(string propertyName)
    {
        if (propertyName == nameof(Port) && Port is < 1 or > 65535)
            yield return "Port must be between 1 and 65535.";
    }
}
```

---

## Lifecycle hooks

Three optional lifecycle hooks let you react to load/save events.
Use the **generic static-virtual pattern** (C# 11 / .NET 7+) to keep all logic
inside the interface itself — no separate partial class file required.

### New: generic static-virtual pattern (recommended)

Implement `IAfterLoad<TSelf>`, `IBeforeSave<TSelf>`, and/or `IAfterSave<TSelf>`
on your section interface and override the `static` hook methods directly:

```csharp
[IniSection("Server")]
public interface IServerSettings
    : IIniSection,
      IAfterLoad<IServerSettings>,
      IBeforeSave<IServerSettings>,
      IAfterSave<IServerSettings>
{
    [IniValue(DefaultValue = "8080")]
    int Port { get; set; }

    [IniValue(DefaultValue = "localhost")]
    string? Host { get; set; }

    // ── Lifecycle hook implementations — live inside the interface ─────────────

    /// <summary>Normalize loaded values.</summary>
    static new void OnAfterLoad(IServerSettings self)
    {
        if (self.Host is not null)
            self.Host = self.Host.Trim().ToLowerInvariant();
    }

    /// <summary>Validate before saving. Return false to abort.</summary>
    static new bool OnBeforeSave(IServerSettings self)
        => self.Port is >= 1 and <= 65535;

    /// <summary>Notify other components after a successful save.</summary>
    static new void OnAfterSave(IServerSettings self)
        => Console.WriteLine($"Server settings saved — {self.Host}:{self.Port}");
}
```

The source generator detects these generic interfaces and emits a bridge in the
generated class so the framework can dispatch the hooks at runtime.

#### How it works

| Interface | Method signature | Behaviour |
|-----------|-----------------|-----------|
| `IAfterLoad<TSelf>` | `static virtual void OnAfterLoad(TSelf self)` | Default: no-op. Called after `Build()` and `Reload()`. |
| `IBeforeSave<TSelf>` | `static virtual bool OnBeforeSave(TSelf self)` | Default: returns `true`. Return `false` to cancel save. |
| `IAfterSave<TSelf>` | `static virtual void OnAfterSave(TSelf self)` | Default: no-op. Called after a successful write. |

### Legacy: partial-class pattern (.NET Framework / instance methods)

If you target **.NET Framework** (4.x), or prefer instance methods in a separate file,
implement the non-generic `IAfterLoad`, `IBeforeSave`, and/or `IAfterSave` interfaces
and provide the implementations in a `partial class` alongside the generated code.

#### Step-by-step

**1. Declare the interface** (as usual):

```csharp
// IMySettings.cs
[IniSection("App")]
public interface IMySettings : IIniSection, IAfterLoad, IBeforeSave, IAfterSave
{
    string? Value { get; set; }
}
```

**2. Add a partial class file** named after the **generated class** — not the interface.
The generated class for `IMySettings` is `MySettingsImpl`, so create `MySettingsImpl.cs`
(or any other file name — what matters is that the class name and namespace match):

```csharp
// MySettingsImpl.cs  ← consumer-written file; sits alongside MySettingsImpl.g.cs
namespace MyApp;

public partial class MySettingsImpl
{
    // ── IAfterLoad ────────────────────────────────────────────────────────────
    public void OnAfterLoad()
    {
        // Called after Build() and Reload() complete
        Value ??= "loaded-default";
    }

    // ── IBeforeSave ───────────────────────────────────────────────────────────
    public bool OnBeforeSave()
    {
        return Value is not null;  // cancel save if Value is null
    }

    // ── IAfterSave ────────────────────────────────────────────────────────────
    public void OnAfterSave()
    {
        Console.WriteLine("Settings saved!");
    }
}
```

> **Key rule:** The partial class must be in the **same namespace** as the generated class
> (i.e. the same namespace as the interface) and must have the **exact same class name**
> (`{InterfaceName-without-leading-I}Impl`).

#### .NET Framework startup pattern

```csharp
// Program.cs / App.xaml.cs
private static IMySettings? _settings;

static void Main()
{
    var config = IniConfigRegistry.ForFile("myapp.ini")
        .AddSearchPath(AppDomain.CurrentDomain.BaseDirectory)
        .RegisterSection<IMySettings>(new MySettingsImpl())
        .Build();

    // Store the section reference once — it never changes, even after Reload()
    _settings = config.GetSection<IMySettings>();

    // … rest of startup
}
```

---

## Async support

All major operations have async variants that avoid blocking threads.

### BuildAsync and InitialLoadTask

```csharp
// Simple async build
var config = await IniConfigRegistry.ForFile("appsettings.ini")
    .AddSearchPath(AppContext.BaseDirectory)
    .RegisterSection<IAppSettings>(new AppSettingsImpl())
    .BuildAsync(cancellationToken);
```

For DI scenarios where you need to register sections before loading completes, use the
**fire-and-forget** pattern with `InitialLoadTask`:

```csharp
// Start loading without awaiting
var section = new AppSettingsImpl();
_ = IniConfigRegistry.ForFile("appsettings.ini")
    .AddSearchPath(AppContext.BaseDirectory)
    .RegisterSection<IAppSettings>(section)
    .BuildAsync();

// IniConfig is already in the registry — register before loading finishes
var iniConfig = IniConfigRegistry.Get("appsettings.ini");
builder.Services.AddSingleton<IAppSettings>(section);
builder.Services.AddSingleton(iniConfig);

// Consumer awaits InitialLoadTask before reading values
await iniConfig.InitialLoadTask;
Console.WriteLine(section.AppName);   // safe to read now
```

> `InitialLoadTask` is `Task.CompletedTask` when synchronous `Build()` is used — awaiting
> it is always safe regardless of which build method was called.

### ReloadAsync and SaveAsync

```csharp
await config.ReloadAsync(cancellationToken);
await config.SaveAsync(cancellationToken);
```

### Async lifecycle hooks

Add `IAfterLoadAsync`, `IBeforeSaveAsync`, or `IAfterSaveAsync` to your section interface
when hook logic needs async operations (e.g. secrets vault, remote validation):

```csharp
[IniSection("App")]
public interface IMySettings : IIniSection, IAfterLoadAsync, IBeforeSaveAsync
{
    string? Secret { get; set; }
}

// Implement in a partial class
public partial class MySettingsImpl
{
    public async Task OnAfterLoadAsync(CancellationToken ct)
        => Secret = await SecretsVault.DecryptAsync(Secret, ct);

    public async Task<bool> OnBeforeSaveAsync(CancellationToken ct)
        => await RemoteValidator.IsValidAsync(Secret, ct);  // false cancels the save
}
```

### IValueSourceAsync — async external sources

Use `IValueSourceAsync` for sources that fetch values over the network:

```csharp
public sealed class RemoteConfigSource : IValueSourceAsync
{
    private readonly HttpClient _http;
    public event EventHandler<ValueChangedEventArgs>? ValueChanged;
    public RemoteConfigSource(HttpClient http) => _http = http;

    public async Task<(bool Found, string? Value)> TryGetValueAsync(
        string sectionName, string key, CancellationToken ct = default)
    {
        var response = await _http.GetAsync($"/config/{sectionName}/{key}", ct);
        if (!response.IsSuccessStatusCode) return (false, null);
        return (true, await response.Content.ReadAsStringAsync(ct));
    }
}

// Register via the IValueSourceAsync overload — consulted during BuildAsync/ReloadAsync
var config = await IniConfigRegistry.ForFile("app.ini")
    .AddSearchPath(AppContext.BaseDirectory)
    .AddValueSource(new RemoteConfigSource(httpClient))
    .RegisterSection<IAppSettings>(new AppSettingsImpl())
    .BuildAsync(cancellationToken);
```

> Async sources are only consulted during `BuildAsync()` and `ReloadAsync()`.  Sync sources
> work with both `Build()` and `BuildAsync()`.

---

## Singleton guarantee and dependency injection

**`GetSection<T>()` always returns the same object reference**, even after `Reload()`.

This is a deliberate design choice: the framework updates the *properties* of the existing
section object in place during a reload, so any code that holds a reference to the section
will automatically see the new values without re-querying the registry.

This makes it safe to register sections as **singletons** in a DI container:

```csharp
// ASP.NET Core / Microsoft.Extensions.DependencyInjection
var config = IniConfigRegistry.ForFile("appsettings.ini")
    .AddSearchPath(AppContext.BaseDirectory)
    .RegisterSection<IAppSettings>(new AppSettingsImpl())
    .Build();

// Register the section as a singleton — the reference stays valid after Reload()
builder.Services.AddSingleton(config.GetSection<IAppSettings>());

// Alternatively, expose the IniConfig itself for manual reload triggering:
builder.Services.AddSingleton(config);
```

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

## Transactional updates

Implement `ITransactional` on your section interface to enable atomic, rollback-capable updates.
Mark individual properties with `[IniValue(Transactional = true)]` to opt them in.

```csharp
[IniSection]
public interface ICredentials : IIniSection, ITransactional
{
    [IniValue(DefaultValue = "guest", Transactional = true)]
    string? Username { get; set; }

    [IniValue(DefaultValue = "", Transactional = true)]
    string? Password { get; set; }

    // Non-transactional properties are updated immediately
    [IniValue(DefaultValue = "0")]
    int LoginCount { get; set; }
}
```

```csharp
var creds = config.GetSection<ICredentials>();

creds.Begin();          // Start transaction — old values remain visible to readers

creds.Username = "alice";
creds.Password = "secret";

if (valid)
    creds.Commit();     // Make new values visible
else
    creds.Rollback();   // Discard changes — old values restored
```

---

## Property-change notifications

Set `[IniValue(NotifyPropertyChanged = true)]` on any property.
The generated class will implement `INotifyPropertyChanging` and `INotifyPropertyChanged`:

```csharp
[IniSection]
public interface IThemeSettings : IIniSection
{
    [IniValue(DefaultValue = "Light", NotifyPropertyChanged = true)]
    string? Theme { get; set; }
}

// Usage
var theme = config.GetSection<IThemeSettings>();
((INotifyPropertyChanged)theme).PropertyChanged += (_, e)
    => Console.WriteLine($"{e.PropertyName} changed");
```

---

## Value converters

The following types are supported out of the box:

| .NET type | Converter class |
|-----------|----------------|
| `string` | `StringConverter` |
| `bool` | `BoolConverter` |
| `int` | `Int32Converter` |
| `long` | `Int64Converter` |
| `uint` | `UInt32Converter` |
| `ulong` | `UInt64Converter` |
| `double` | `DoubleConverter` |
| `float` | `FloatConverter` |
| `decimal` | `DecimalConverter` |
| `DateTime` | `DateTimeConverter` (ISO 8601 round-trip) |
| `TimeSpan` | `TimeSpanConverter` (constant "c" format) |
| `Guid` | `GuidConverter` |
| `Uri` | `UriConverter` |
| Any `enum` | `EnumConverter` (auto-registered on first use) |
| `Nullable<T>` | Wraps the inner converter |

### Adding a custom converter

```csharp
// 1. Implement IValueConverter<T>
public sealed class VersionConverter : ValueConverterBase<Version>
{
    public override Version? ConvertFromString(string? raw, Version? defaultValue = default)
        => raw is null ? defaultValue : Version.Parse(raw.Trim());
}

// 2. Register before calling Build()
ValueConverterRegistry.Register(new VersionConverter());

// 3. Use the type in your section interface
[IniSection]
public interface IAppInfo : IIniSection
{
    [IniValue(DefaultValue = "1.0.0.0")]
    Version? AppVersion { get; set; }
}
```

### Encrypting sensitive values

Sensitive values (passwords, API keys, connection strings) can be stored encrypted in the
INI file by combining a **custom converter** that encrypts/decrypts on the fly with the
`IAfterLoad` / `IBeforeSave` hooks.

#### Option A — Encrypt/decrypt in a custom converter (recommended)

The converter is responsible for the raw string stored on disk. Everything else in the
framework (defaults, reload, transactional) continues to work normally.

```csharp
/// <summary>
/// Stores a string value AES-encrypted (Base64) in the INI file.
/// Replace the key derivation with your own secure mechanism (e.g. DPAPI, Azure KeyVault).
/// </summary>
public sealed class EncryptedStringConverter : IValueConverter
{
    // ⚠️  Hard-coded key for illustration only — use a proper key-management solution!
    private static readonly byte[] Key = Convert.FromBase64String("your-32-byte-key-base64==");
    private static readonly byte[] IV  = Convert.FromBase64String("your-16-byte-iv-base64=");

    public Type TargetType => typeof(string);

    public object? ConvertFromString(string? raw)
    {
        if (raw is null) return null;
        using var aes = Aes.Create();
        aes.Key = Key; aes.IV = IV;
        var cipher = Convert.FromBase64String(raw);
        using var decryptor = aes.CreateDecryptor();
        var plain = decryptor.TransformFinalBlock(cipher, 0, cipher.Length);
        return System.Text.Encoding.UTF8.GetString(plain);
    }

    public string? ConvertToString(object? value)
    {
        if (value is not string s) return null;
        using var aes = Aes.Create();
        aes.Key = Key; aes.IV = IV;
        var plain = System.Text.Encoding.UTF8.GetBytes(s);
        using var encryptor = aes.CreateEncryptor();
        var cipher = encryptor.TransformFinalBlock(plain, 0, plain.Length);
        return Convert.ToBase64String(cipher);
    }
}
```

```csharp
// Register before Build()
ValueConverterRegistry.Register(new EncryptedStringConverter());

[IniSection("Credentials")]
public interface ICredentials : IIniSection
{
    [IniValue(DefaultValue = "")]
    string? ApiKey { get; set; }   // stored as encrypted Base64 in the file
}
```

#### Option B — Decrypt in `IAfterLoad`, re-encrypt in `IBeforeSave`

This approach stores the encrypted value in the INI file and keeps the plaintext only
in memory.  It is useful when the property type must remain `string` without a custom converter.

```csharp
[IniSection("Credentials")]
public interface ICredentials
    : IIniSection,
      IAfterLoad<ICredentials>,
      IBeforeSave<ICredentials>
{
    // Raw (encrypted) value as stored in the file — treated as opaque by the framework
    [IniValue(DefaultValue = "")]
    string? ApiKeyEncrypted { get; set; }

    // Plaintext — marked ReadOnly so it is never written back to the file
    [IniValue(ReadOnly = true)]
    string? ApiKeyPlain { get; set; }

    static new void OnAfterLoad(ICredentials self)
    {
        // Decrypt once after loading
        self.ApiKeyPlain = Decrypt(self.ApiKeyEncrypted);
    }

    static new bool OnBeforeSave(ICredentials self)
    {
        // Re-encrypt before writing; keep plaintext in memory only
        self.ApiKeyEncrypted = Encrypt(self.ApiKeyPlain);
        return true;
    }

    private static string? Decrypt(string? cipher) => /* … your crypto … */ cipher;
    private static string? Encrypt(string? plain)  => /* … your crypto … */ plain;
}
```

---

## Registry API reference

`IniConfigRegistry` is a thread-safe global registry that maps file names to their loaded configurations.

| Method | Description |
|--------|-------------|
| `ForFile(fileName)` | Returns a fluent `IniConfigBuilder` for the given file name |
| `Get(fileName)` | Returns the `IniConfig` for the file; throws if not registered |
| `TryGet(fileName, out config)` | Returns `false` if the file has not been registered |
| `GetSection<T>(fileName)` | Shortcut for `Get(fileName).GetSection<T>()` |
| `Unregister(fileName)` | Removes a registration (useful in tests) |
| `Clear()` | Removes all registrations (useful in tests) |

`IniConfig` methods and properties:

| Member | Description |
|--------|-------------|
| `GetSection<T>()` | Returns the registered section instance; throws if not found. **Always returns the same object reference.** |
| `Save()` | Writes all section values to disk, honoring `IBeforeSave`/`IAfterSave` hooks |
| `Reload()` | Re-reads all layers in place; section references remain valid |
| `RequestPostponedReload()` | Triggers a reload that was earlier postponed by a `FileChangedCallback` |
| `Reloaded` | Event raised after a successful `Reload()` |
| `FileName` | The logical file name passed to `ForFile()` |
| `LoadedFromPath` | Resolved absolute path from which the file was actually read |
| `Dispose()` | Releases the file lock (if any) and stops the file-system watcher |

`IniConfigBuilder` fluent methods:

| Method | Description |
|--------|-------------|
| `AddSearchPath(path)` | Adds a directory to search for the INI file |
| `AddAppDataPath(applicationName)` | Adds `%APPDATA%\applicationName` (Linux: `~/.config/applicationName`) as a search path; creates the directory if absent |
| `SetWritablePath(path)` | Overrides the write target for new files when no existing file is found in any search path |
| `AddDefaultsFile(path)` | Registers a file that supplies default values (applied before the user file) |
| `AddConstantsFile(path)` | Registers a file that supplies admin-forced constants (applied last) |
| `AddValueSource(source)` | Registers an `IValueSource` (applied after constants) |
| `LockFile()` | Holds the file open read-exclusively for the process lifetime |
| `MonitorFile([callback])` | Installs a `FileSystemWatcher`; optional callback controls reload decision |
| `RegisterSection<T>(impl)` | Registers a section with its generated implementation |
| `Build()` | Loads the file, fires hooks, and registers the config in the global registry |
