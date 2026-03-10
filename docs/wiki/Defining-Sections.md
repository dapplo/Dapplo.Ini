# Defining Sections

Every configuration section is a plain C# interface annotated with `[IniSection]`.
The source generator (`Dapplo.IniConfig.Generator`) creates a concrete `partial class`
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
