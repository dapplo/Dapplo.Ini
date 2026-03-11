# Defining Sections

Every configuration section is a plain C# interface annotated with `[IniSection]`.
The source generator (`Dapplo.Ini.Generator`) creates a concrete `partial class`
implementation automatically.

---

## `[IniSection]` attribute

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

---

## `[IniValue]` attribute

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

## Read-only properties

A property can be made **read-only from the consumer's perspective** simply by omitting
the setter from the interface declaration (`{ get; }` instead of `{ get; set; }`).

The source generator automatically detects getter-only properties and treats them as
read-only:

| Behaviour | Getter-only `{ get; }` | `[IniValue(ReadOnly = true)]` |
|-----------|----------------------|-------------------------------|
| Default value applied | ✓ | ✓ |
| Value loaded from INI | ✓ | ✓ |
| Value written to INI on save | **✗** | **✗** |
| Setter on **implementation class** | ✓ (public) | ✓ (public) |
| Setter on **interface** | **✗** | ✓ |

### Getter-only interface property

Declare the property without a setter in the interface. The generated implementation
class still exposes a **public setter** so the framework (and code that references the
concrete class directly) can assign the value programmatically.

```csharp
[IniSection("AppInfo")]
public interface IAppInfo : IIniSection
{
    // Getter-only: cannot be set through the interface.
    // The value is loaded from the INI file but never written back.
    [IniValue(DefaultValue = "1.0.0")]
    string? Version { get; }

    // Regular read-write property — written to disk when saved.
    [IniValue(DefaultValue = "MyApp")]
    string? Name { get; set; }
}
```

Usage:

```csharp
IAppInfo settings = new AppInfoImpl();   // concrete type from source generator

// ✓ Reading always works through the interface:
Console.WriteLine(settings.Version);

// ✗ Compile error — interface does not expose a setter:
// settings.Version = "2.0.0";

// ✓ Setting is still possible via the concrete class:
var impl = (AppInfoImpl)settings;
impl.Version = "2.0.0";
```

### `[IniValue(ReadOnly = true)]` on a read-write property

The `ReadOnly` attribute flag achieves the same no-save behaviour while **keeping the
setter on the interface**. This is useful when you need to set the property through the
interface type but still want to prevent it from being written to disk.

```csharp
[IniSection("AppInfo")]
public interface IAppInfo : IIniSection
{
    // Interface setter is present, but the value is never written to disk.
    [IniValue(DefaultValue = "1.0.0", ReadOnly = true)]
    string? Version { get; set; }
}
```

---

## Generated class naming convention

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
code in a separate file — see [[Lifecycle-Hooks#legacy-partial-class-pattern]].

---

## See also

- [[Lifecycle-Hooks]] — `IAfterLoad`, `IBeforeSave`, `IAfterSave`
- [[Validation]] — `IDataValidation<TSelf>` for WPF/Avalonia binding validation
- [[Transactional-Updates]] — `ITransactional` for atomic updates
- [[Value-Converters]] — supported property types and custom converters
