# Defining Sections

Every configuration section is a plain C# interface that extends `IIniSection`.
The source generator (`Dapplo.Ini.Generator`) creates a concrete `partial class`
implementation automatically.

---

## `[IniSection]` attribute — optional

`[IniSection]` is **optional**.  The source generator processes any interface that
extends `IIniSection`, with or without the attribute.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `SectionName` (ctor) | `string?` | interface name minus leading `I` | Name of the `[Section]` in the INI file |
| `Description` | `string?` | `null` | Written as a comment above the section header |
| `EmptyWhenNull` | `bool` | `false` | When `true`, every non-value-type property in the section returns an empty value (e.g. `string.Empty`, empty list, empty array) instead of `null` when absent. Equivalent to placing `[IniValue(EmptyWhenNull = true)]` on each property individually. See [[Empty-When-Null]]. |
| `IgnoreDefaults` | `bool` | `false` | When `true`, values from files registered via `AddDefaultsFile` are never applied to any property in this section. Compiled defaults and the user file still apply. See [[Ignore-Defaults-and-Constants]]. |
| `IgnoreConstants` | `bool` | `false` | When `true`, values from files registered via `AddConstantsFile` are never applied to any property in this section. No key in the section will ever be locked by an admin constants file. See [[Ignore-Defaults-and-Constants]]. |

When `[IniSection]` is omitted:

- The section name defaults to the interface name with the leading `I` stripped
  (e.g. `IAppSettings` → `[AppSettings]`).
- Use `[Description("...")]` on the interface to set the section comment.

```csharp
// No [IniSection] needed — section name is "AppSettings", description from [Description]
using System.ComponentModel;

[Description("Application settings")]
public interface IAppSettings : IIniSection
{
    [DefaultValue("MyApp")]
    string? AppName { get; set; }
}

// Use [IniSection] only when you need a custom section name
[IniSection("app")]
public interface IAppSettings : IIniSection { /* … */ }
```

> **Note:** There is no standard .NET attribute that can be applied to an interface
> to set a serialisation name (`[DataMember]` targets fields/properties/methods;
> `[DataContract]` targets classes/structs/enums).  `[IniSection("customName")]` is
> therefore the only way to override the section name when the default is not suitable.

---

## Annotating properties

The source generator recognises **standard .NET attributes** as the preferred way to
annotate properties.  These are the same attributes used by JSON/XML serialisers, so
your interface definitions stay clean and interoperable.

### Standard .NET attributes (preferred)

| Attribute | Effect |
|---|---|
| `[DefaultValue(value)]` | Sets the default value. Accepts any value type; converted to string internally. |
| `[Description("...")]` | Written as a comment above the key in the INI file |
| `[DataMember(Name = "...")]` | Overrides the key name in the INI file |
| `[IgnoreDataMember]` | Excludes the property from all INI read/write operations (and from `ResetToDefaults`) |
| Getter-only `{ get; }` | Value is loaded but never written back to disk; the generated class still has a public setter |

```csharp
using System.ComponentModel;
using System.Runtime.Serialization;

// No [IniSection] — section name = "UserProfileSettings"
[Description("User profile settings")]          // sets the section comment
public interface IUserProfileSettings : IIniSection
{
    [DataMember(Name = "display_name")]          // INI key → "display_name"
    [DefaultValue("Anonymous")]                  // default value
    [Description("The user's display name")]     // key comment
    string? DisplayName { get; set; }

    [DefaultValue(3)]                            // numeric default — no string quoting needed
    int LoginAttempts { get; set; }

    [IgnoreDataMember]                           // never read from or written to the file
    string? CachedToken { get; set; }

    [DefaultValue("1.0.0")]
    string? AppVersion { get; }                  // getter-only: loaded but never saved
}
```

---

### `[IniValue]` attribute — use only when no standard equivalent exists

For the following three capabilities there is no standard .NET attribute; use
`[IniValue]` for these only:

| `[IniValue]` property | Purpose |
|---|---|
| `NotifyPropertyChanged = true` | Raises `INotifyPropertyChanged` / `INotifyPropertyChanging` on every assignment |
| `Transactional = true` | Property participates in `Begin` / `Commit` / `Rollback` — requires `ITransactional` |
| `RuntimeOnly = true` | Property is never loaded from or saved to the INI file but its default **is** restored by `ResetToDefaults` on every reload |
| `EmptyWhenNull = true` | When absent from the file, returns `string.Empty`, an empty list, an empty array, or an empty dictionary instead of `null`. See [[Empty-When-Null]]. |
| `IgnoreDefaults = true` | Values from defaults files are never applied to this property. Compiled defaults and the user file still apply. See [[Ignore-Defaults-and-Constants]]. |
| `IgnoreConstants = true` | Values from constants files are never applied to this property. The key is never locked by an admin constants file. See [[Ignore-Defaults-and-Constants]]. |

```csharp
[IniSection("AppState")]
public interface IAppStateSettings : IIniSection
{
    // Raises property-change events (no standard attribute equivalent)
    [DefaultValue("MyApp")]
    [IniValue(NotifyPropertyChanged = true)]
    string? AppName { get; set; }

    // Never persisted — default is reset on every Reload(); use for session-scoped values
    [DefaultValue("unauthenticated")]
    [IniValue(RuntimeOnly = true)]
    string? CurrentUser { get; set; }
}
```

> **Tip:** `[IniValue]` and the standard attributes can be combined freely.
> When both supply the same information, `[IniValue]` takes precedence.

#### Full `[IniValue]` reference

For completeness, the following properties are also available on `[IniValue]` but each
has a preferred standard-attribute alternative:

| `[IniValue]` property | Standard alternative | Notes |
|---|---|---|
| `KeyName` | `[DataMember(Name = "...")]` | When both are present, `[IniValue]` wins |
| `DefaultValue` | `[DefaultValue(...)]` | When both are present, `[IniValue]` wins |
| `Description` | `[Description("...")]` | When both are present, `[IniValue]` wins |
| `ReadOnly = true` | Getter-only `{ get; }` | Use `[IniValue(ReadOnly = true)]` only when you need the setter on the interface |

---

## Empty-over-null semantics

By default, reference-type properties (`string?`, `List<T>?`, `T[]?`, `Dictionary<K,V>?`) return
`null` when absent from the INI file.  Set `EmptyWhenNull = true` at any of three scopes to
return an empty value instead:

| Scope | How |
|---|---|
| Single property | `[IniValue(EmptyWhenNull = true)]` on the property |
| Entire section | `[IniSection(EmptyWhenNull = true)]` on the interface |
| All sections | `IniConfigBuilder.EmptyWhenNull()` on the builder |

```csharp
// Property level — single property
[IniSection("App")]
public interface IAppSettings : IIniSection
{
    [IniValue(EmptyWhenNull = true)]
    string? Description { get; set; }    // → "" when absent

    [IniValue(EmptyWhenNull = true)]
    List<string>? Tags { get; set; }     // → [] when absent

    string? OptionalNote { get; set; }   // → null when absent (unchanged)
}

// Section level — applies to all non-value-type properties in the section
[IniSection("App", EmptyWhenNull = true)]
public interface IAppSettings : IIniSection
{
    string? Description { get; set; }    // → "" when absent
    List<string>? Tags { get; set; }     // → [] when absent
    int Counter { get; set; }            // value type — unaffected
}
```

> `[DefaultValue]` always wins over `EmptyWhenNull`: when an explicit default is set, that
> default is applied by `ResetToDefaults()` and the empty-when-null behaviour has no effect.

See [[Empty-When-Null]] for the complete guide, precedence rules, and config-level usage.

---

## Read-only properties

The natural C# way to declare a read-only property is to omit the setter from the
interface (`{ get; }` instead of `{ get; set; }`).

| Behaviour | Getter-only `{ get; }` | `[IniValue(ReadOnly = true)]` |
|-----------|----------------------|-------------------------------|
| Default value applied | ✓ | ✓ |
| Value loaded from INI | ✓ | ✓ |
| Value written to INI on save | **✗** | **✗** |
| Setter on **implementation class** | ✓ (public) | ✓ (public) |
| Setter on **interface** | **✗** | ✓ |

### Getter-only interface property (preferred)

```csharp
[IniSection("AppInfo")]
public interface IAppInfo : IIniSection
{
    // Getter-only: loaded from INI, never written back.
    [DefaultValue("1.0.0")]
    string? Version { get; }

    // Regular read-write property — written to disk when saved.
    [DefaultValue("MyApp")]
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

### `[IniValue(ReadOnly = true)]` — keep setter on interface

Use this only when you need to be able to set the property through the interface type:

```csharp
[IniSection("AppInfo")]
public interface IAppInfo : IIniSection
{
    // Interface setter is present, but the value is never written to disk.
    [DefaultValue("1.0.0")]
    [IniValue(ReadOnly = true)]
    string? Version { get; set; }
}
```

---

## Runtime-only properties

A **runtime-only** property behaves like a normal typed property with a default, but is
completely invisible to the INI file.  It is never written to disk, never read from the
file, and its default is re-applied on every `Reload()`.

Use this for values that are meaningful while the application is running (e.g. a
current-user token, an in-memory flag) but must not survive a process restart.

```csharp
[IniSection("Session")]
public interface ISessionSettings : IIniSection
{
    // Persisted normally
    [DefaultValue("DefaultUser")]
    string? LastUser { get; set; }

    // Runtime-only: default is restored on Reload(); never touches the file
    [DefaultValue("unauthenticated")]
    [IniValue(RuntimeOnly = true)]
    string? CurrentUser { get; set; }

    [DefaultValue("0")]
    [IniValue(RuntimeOnly = true)]
    int FailedAttempts { get; set; }
}
```

See [[Runtime-Only-and-Constants]] for the full guide.

---

## Constants protection

Values loaded from a file registered with `AddConstantsFile` are protected against
modification.  Attempting to change them throws `AccessViolationException`.  Call
`section.IsConstant(key)` to query the lock state, e.g. to disable a UI control.

```csharp
if (section.IsConstant("AdminValue"))
    adminValueInput.IsEnabled = false;  // disable the control in the UI

// Throws AccessViolationException:
section.AdminValue = "override";
```

See [[Runtime-Only-and-Constants]] for the full guide.

---

## Ignoring defaults and constants files

Use `IgnoreDefaults` / `IgnoreConstants` to opt a section or individual property out of
the corresponding overlay layer:

```csharp
// Entire section: never touched by the defaults file
[IniSection("UserPreferences", IgnoreDefaults = true)]
public interface IUserPreferences : IIniSection
{
    [DefaultValue("Light")]
    string? Theme { get; set; }
}

// Single property: user-owned value; admin constants file cannot lock it
[IniSection("App")]
public interface IAppSettings : IIniSection
{
    [DefaultValue("5")]
    int MaxRetries { get; set; }

    [DefaultValue("Light")]
    [IniValue(IgnoreConstants = true)]
    string? Theme { get; set; }
}
```

See [[Ignore-Defaults-and-Constants]] for the full guide.

---

## Validation attributes (DataAnnotations)

Place `System.ComponentModel.DataAnnotations` attributes on properties to have the
source generator emit inline validation code.  See [[Validation]] for the full reference.

| Attribute | What is checked |
|---|---|
| `[Required]` | null / empty string |
| `[Range(min, max)]` | numeric (or `IComparable`) range |
| `[MaxLength(n)]` | string length |
| `[RegularExpression(pattern)]` | regex match |

```csharp
[IniSection("App")]
public interface IAppSettings : IIniSection
{
    [Required]
    string? Name { get; set; }

    [Range(1024, 65535, ErrorMessage = "Port must be between 1024 and 65535.")]
    int Port { get; set; }

    [MaxLength(100)]
    string? Description { get; set; }

    [RegularExpression(@"^[a-z0-9_-]+$", ErrorMessage = "Slug must be lowercase alphanumeric.")]
    string? Slug { get; set; }
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

- [[Empty-When-Null]] — `EmptyWhenNull` at property, section, and config levels
- [[Runtime-Only-and-Constants]] — `RuntimeOnly` properties and constants-file protection
- [[Ignore-Defaults-and-Constants]] — opt sections/properties out of defaults and constants overlays
- [[Lifecycle-Hooks]] — `IAfterLoad`, `IBeforeSave`, `IAfterSave`
- [[Validation]] — `IDataValidation<TSelf>`, DataAnnotations attributes, and `INotifyDataErrorInfo`
- [[Transactional-Updates]] — `ITransactional` for atomic updates
- [[Value-Converters]] — supported property types and custom converters
