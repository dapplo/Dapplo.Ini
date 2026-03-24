# Runtime-Only Properties and Constants

This page covers two complementary features for stricter configuration control:

1. **Runtime-only properties** — in-memory values with defaults that are never persisted.
2. **Constants-file protection** — admin-enforced values that cannot be overridden at runtime.

---

## Runtime-only properties

A **runtime-only** property behaves like a normal typed property:

- Its default value is applied by `ResetToDefaults` on every load and reload.
- It can be read and changed freely at runtime.

But it is completely invisible to the INI file:

- It is **never loaded** from any file (user file, defaults file, constants file).
- It is **never saved** to disk.

### When to use

Use runtime-only properties for values that:

- Are meaningful only while the application is running (e.g. a session token, an
  in-memory counter, a transient flag).
- Should not survive a process restart.
- Still benefit from having a sensible default that is re-applied on every `Reload()`.

### Declaration

Annotate the property with `[IniValue(RuntimeOnly = true)]`. Use a standard
`[DefaultValue(...)]` attribute to supply the default:

```csharp
using System.ComponentModel;
using Dapplo.Ini.Attributes;

[IniSection("Session")]
public interface ISessionSettings : IIniSection
{
    // Persisted normally
    [DefaultValue("DefaultUser")]
    string? LastUser { get; set; }

    // Runtime-only — default is restored on Reload(); never touches the file
    [DefaultValue("unauthenticated")]
    [IniValue(RuntimeOnly = true)]
    string? CurrentUser { get; set; }

    [DefaultValue("0")]
    [IniValue(RuntimeOnly = true)]
    int FailedAttempts { get; set; }
}
```

### Behaviour summary

| Operation | Regular property | RuntimeOnly property |
|-----------|-----------------|---------------------|
| Default applied on load | ✓ | ✓ |
| Loaded from INI file | ✓ | **✗** (file value ignored) |
| Written to INI file on save | ✓ | **✗** |
| Default re-applied on `Reload()` | ✓ | ✓ |
| Modifiable at runtime | ✓ | ✓ |

### Difference from `[IgnoreDataMember]`

`[IgnoreDataMember]` performs a **full exclusion** — the property is excluded from all
INI operations, including `ResetToDefaults`.  If you need a default to be re-applied on
every reload, use `[IniValue(RuntimeOnly = true)]` instead.

| | `[IgnoreDataMember]` | `[IniValue(RuntimeOnly = true)]` |
|---|---|---|
| Loaded from INI | **✗** | **✗** |
| Saved to INI | **✗** | **✗** |
| Default applied by `ResetToDefaults` | **✗** | ✓ |

---

## Constants-file protection

Values loaded from a file registered via `AddConstantsFile` are **write-protected**
after loading.  Any attempt to modify them — through the property setter or through
`SetRawValue` — throws `AccessViolationException`.

This models an admin-policy scenario: an administrator deploys a constants file that
locks certain values so end users cannot override them.

### Registering a constants file

```csharp
using var config = IniConfigRegistry.ForFile("myapp.ini")
    .AddSearchPath(AppContext.BaseDirectory)
    .AddConstantsFile("/etc/myapp/constants.ini")   // admin-managed; applied last
    .RegisterSection<IAppSettings>(new AppSettingsImpl())
    .Build();
```

The loading order is:

1. Defaults files (`AddDefaultsFile`) — lowest priority
2. User INI file
3. **Constants files** (`AddConstantsFile`) — highest priority; keys become write-protected

### Querying constant state

Call `section.IsConstant(key)` to check whether a key is currently protected.  The key
name is case-insensitive.  This is intended for UI code that wants to disable an input
control when the corresponding setting is locked by an administrator:

```csharp
// Disable the input control when the admin has locked the value
if (section.IsConstant("MaxRetries"))
    maxRetriesInput.IsEnabled = false;
```

`IsConstant` is also accessible via the `IIniSection` interface:

```csharp
Dapplo.Ini.Interfaces.IIniSection iface = section;
if (iface.IsConstant("MaxRetries"))
    maxRetriesInput.IsEnabled = false;
```

### Protection behaviour

```csharp
// Loads from "/etc/myapp/constants.ini" which contains:
//   [AppSettings]
//   MaxRetries = 5

var settings = config.GetSection<IAppSettings>();

// ✓ Reading the constant value always works:
Console.WriteLine(settings.MaxRetries);   // "5"

// ✓ Non-constant properties can still be changed:
settings.AppName = "My App";

// ✗ Throws AccessViolationException — the key is locked by the constants file:
settings.MaxRetries = 10;
```

### Protection is re-established on Reload

Constants are cleared at the beginning of every `Reload()` / `ReloadAsync()` cycle and
re-established when the constants file is re-applied.  This means:

- If the constants file is updated on disk, the new values are applied and their keys
  are protected again after the next reload.
- If the constants file is removed, the protection is lifted and the user file wins.

```csharp
config.Reload();
// MaxRetries is still constant (and still throws on write) if the file is still there.
```

---

## See also

- [[Defining-Sections]] — full attribute reference including `[IniValue(RuntimeOnly = true)]`
- [[Loading-Configuration]] — `AddConstantsFile`, `AddDefaultsFile`, and loading order
- [[Loading-Life-Cycle]] — the exact order in which values are applied during a load cycle
- [[Reloading]] — how `Reload()` interacts with defaults and constants
