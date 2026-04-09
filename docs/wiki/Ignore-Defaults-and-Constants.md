# Ignoring Defaults and Constants (`IgnoreDefaults` / `IgnoreConstants`)

Sometimes a particular section or property should not be affected by the layered overlay
files — either because the defaults file value would be misleading for that property, or
because an administrator constants file must not be allowed to lock a specific value.

`IgnoreDefaults` and `IgnoreConstants` let you opt out at two granularities:

| Scope | How |
|---|---|
| Entire section | `[IniSection(IgnoreDefaults = true)]` / `[IniSection(IgnoreConstants = true)]` |
| Single property | `[IniValue(IgnoreDefaults = true)]` / `[IniValue(IgnoreConstants = true)]` |

---

## `IgnoreDefaults`

When set, values from files registered with `AddDefaultsFile` are **not applied** to the
marked section or property.  The compiled default (`[DefaultValue]`) and any user-file
value still apply normally.

### When to use

- A property whose sensible baseline differs per deployment and must therefore not be
  overridden by a shared defaults file.
- A section whose values are populated entirely from the user file or from an external
  value source, and the defaults file should have no influence.

### Section-level

```csharp
// No value from a defaults file is ever applied to any property in this section.
[IniSection("Telemetry", IgnoreDefaults = true)]
public interface ITelemetrySettings : IIniSection
{
    [DefaultValue("false")]
    bool Enabled { get; set; }

    [DefaultValue("https://telemetry.example.com")]
    string? Endpoint { get; set; }
}
```

### Property-level

```csharp
[IniSection("Database")]
public interface IDatabaseSettings : IIniSection
{
    // Loaded from defaults file normally
    [DefaultValue("localhost")]
    string? Host { get; set; }

    // Never loaded from defaults file — the user must set this explicitly
    [DefaultValue("")]
    [IniValue(IgnoreDefaults = true)]
    string? Password { get; set; }
}
```

### Behaviour when `IgnoreDefaults` is set

| File / source | Applied? |
|---|---|
| Compiled default (`[DefaultValue]`) | ✓ always |
| Defaults file (`AddDefaultsFile`) | **✗** skipped |
| User INI file | ✓ |
| Constants file (`AddConstantsFile`) | ✓ (and key is locked as usual) |
| External value source (`AddValueSource`) | ✓ |

---

## `IgnoreConstants`

When set, values from files registered with `AddConstantsFile` are **not applied** to the
marked section or property.  The key is therefore also **never locked** — runtime
modification remains allowed.

### When to use

- A property (e.g. a user-specific preference) that an administrator should never be
  able to lock, even when a constants file is deployed.
- A section that holds user-owned values that must always be freely editable.

### Section-level

```csharp
// No value from a constants file is ever applied to this section.
// Properties in this section can never be locked by an admin constants file.
[IniSection("UserPreferences", IgnoreConstants = true)]
public interface IUserPreferences : IIniSection
{
    [DefaultValue("Light")]
    string? Theme { get; set; }

    [DefaultValue("en-US")]
    string? Language { get; set; }
}
```

### Property-level

```csharp
[IniSection("App")]
public interface IAppSettings : IIniSection
{
    // Can be locked by an admin constants file (default behaviour)
    [DefaultValue("5")]
    int MaxRetries { get; set; }

    // This property is user-owned — the admin constants file cannot lock it
    [DefaultValue("Light")]
    [IniValue(IgnoreConstants = true)]
    string? Theme { get; set; }
}
```

### Behaviour when `IgnoreConstants` is set

| File / source | Applied? |
|---|---|
| Compiled default (`[DefaultValue]`) | ✓ always |
| Defaults file (`AddDefaultsFile`) | ✓ |
| User INI file | ✓ |
| Constants file (`AddConstantsFile`) | **✗** skipped — key remains unlocked |
| External value source (`AddValueSource`) | ✓ |

---

## Combining both flags

Both flags can be applied together on a single section or property when you want to
exclude it from both overlay layers:

```csharp
[IniSection("UserAuth", IgnoreDefaults = true, IgnoreConstants = true)]
public interface IUserAuthSettings : IIniSection
{
    // Never set from defaults; never lockable by admin constants
    [DefaultValue("")]
    string? ApiToken { get; set; }
}
```

---

## Interaction with `Reload()`

The skip behaviour is re-applied on every `Reload()` / `ReloadAsync()` cycle, exactly
as it is during initial loading.  No extra configuration is required.

---

## Metadata is always read from the user file only

Regardless of whether `IgnoreDefaults` / `IgnoreConstants` are used, the
`[__metadata__]` section (version, application name, timestamp) is **only ever read
from the main user INI file** — never from defaults or constants files.  This ensures
that metadata is always authentic and reflects the real settings file.

---

## See also

- [[Runtime-Only-and-Constants]] — `RuntimeOnly` properties and constants-file protection
- [[Defining-Sections]] — complete `[IniSection]` and `[IniValue]` attribute reference
- [[Loading-Configuration]] — `AddDefaultsFile`, `AddConstantsFile`, and the builder API
- [[Loading-Life-Cycle]] — exact order in which defaults, user file, and constants are applied
