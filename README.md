# Dapplo.IniConfig

A powerful, source-generator–backed INI file configuration framework for .NET.

- ✅ Define configuration sections as **annotated interfaces**
- ✅ Concrete classes are **auto-generated** — no boilerplate
- ✅ **Layered** loading: defaults → user file → admin constants
- ✅ **Transactional** updates with rollback support
- ✅ **INotifyPropertyChanged** / **INotifyPropertyChanging** baked in
- ✅ **Lifecycle hooks** implementable directly in the section interface via static virtuals (C# 11+)
- ✅ Extensible **value converter** system

---

## Table of Contents

1. [Quick start](#quick-start)
2. [Defining section interfaces](#defining-section-interfaces)
3. [Loading configuration](#loading-configuration)
4. [Saving configuration](#saving-configuration)
5. [Lifecycle hooks](#lifecycle-hooks)
   - [New: generic static-virtual pattern (recommended)](#new-generic-static-virtual-pattern-recommended)
   - [Legacy: partial-class pattern](#legacy-partial-class-pattern)
6. [Transactional updates](#transactional-updates)
7. [Property-change notifications](#property-change-notifications)
8. [Value converters](#value-converters)
9. [Registry API reference](#registry-api-reference)

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

// 3. Read values
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
The source generator (`Dapplo.IniConfig.Generator`) creates a concrete `partial class`
implementation automatically.

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

---

## Loading configuration

Use the fluent `IniConfigBuilder` API to configure a single INI file:

```csharp
var config = IniConfigRegistry.ForFile("myapp.ini")
    // Search directories – tried in order until the file is found
    .AddSearchPath("/etc/myapp")
    .AddSearchPath(AppContext.BaseDirectory)
    // Optional: apply an admin-supplied defaults file first
    .AddDefaultsFile("/etc/myapp/defaults.ini")
    // Optional: apply admin-forced constants last (users cannot override these)
    .AddConstantsFile("/etc/myapp/constants.ini")
    // Register each section with its generated implementation
    .RegisterSection<IDbSettings>(new DbSettingsImpl())
    .RegisterSection<IAppSettings>(new AppSettingsImpl())
    // Build loads the file, fires IAfterLoad hooks, and registers in the global registry
    .Build();
```

### Loading order (layered configuration)

```
1. Seed each section with [IniValue(DefaultValue = …)] defaults
2. Apply each AddDefaultsFile() in order
3. Apply the resolved user INI file (first match in AddSearchPath directories)
4. Apply each AddConstantsFile() in order  ← cannot be overridden
5. Fire IAfterLoad hooks
```

If the user file is not found, the path of the first writable search directory
is used for a future `Save()` call.

---

## Saving configuration

```csharp
// Saves all section values back to the file that was loaded (or the first writable search path).
config.Save();

// IBeforeSave hooks run first — returning false cancels the save.
// IAfterSave hooks run after a successful write.
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
| `IAfterLoad<TSelf>` | `static virtual void OnAfterLoad(TSelf self)` | Default: no-op. Called after `Build()`. |
| `IBeforeSave<TSelf>` | `static virtual bool OnBeforeSave(TSelf self)` | Default: returns `true`. Return `false` to cancel save. |
| `IAfterSave<TSelf>` | `static virtual void OnAfterSave(TSelf self)` | Default: no-op. Called after a successful write. |

### Legacy: partial-class pattern

If you target frameworks older than .NET 7, or prefer the instance-method approach,
implement the non-generic `IAfterLoad`, `IBeforeSave`, and/or `IAfterSave` interfaces
and provide the implementations in a `partial class` alongside the generated code:

```csharp
// Interface (section definition)
[IniSection]
public interface IMySettings : IIniSection, IAfterLoad, IBeforeSave, IAfterSave
{
    string? Value { get; set; }
}

// Partial class (consumer code — in its own file)
public partial class MySettingsImpl
{
    public void OnAfterLoad()
    {
        Value ??= "loaded-default";
    }

    public bool OnBeforeSave()
    {
        return Value is not null;  // cancel save if Value is null
    }

    public void OnAfterSave()
    {
        Console.WriteLine("Settings saved!");
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

`IniConfig` methods:

| Method | Description |
|--------|-------------|
| `GetSection<T>()` | Returns the registered section instance; throws if not found |
| `Save()` | Writes all section values to disk, honoring `IBeforeSave`/`IAfterSave` hooks |
| `FileName` | The logical file name passed to `ForFile()` |
| `LoadedFromPath` | Resolved absolute path from which the file was actually read |
