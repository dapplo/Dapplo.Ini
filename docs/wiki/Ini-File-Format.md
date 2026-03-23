# INI File Format

This page describes the exact syntax of the `.ini` files read and written by
`Dapplo.Ini`, and shows how C# interface declarations map to the file content.

---

## Syntax overview

An INI file consists of **sections**, **key-value pairs**, and **comments**.

```ini
; This is a comment — it starts with a semicolon
# A hash sign also introduces a comment

[SectionName]
; Comment on the key below
KeyName = some value
AnotherKey=another value
```

| Element | Syntax |
|---------|--------|
| **Section header** | `[SectionName]` |
| **Key-value pair** | `key = value` or `key=value` (whitespace around `=` is trimmed) |
| **Comment** | A line starting with `;` or `#` |
| **Blank line** | Ignored — resets pending comments so they aren't attached to the next key |

**Case sensitivity:** Section names and key names are looked up **case-insensitively**.

---

## Mapping an interface to a file

Given the following C# interface:

```csharp
[IniSection("Database", Description = "Database connection settings")]
public interface IDbSettings : IIniSection
{
    [IniValue(DefaultValue = "localhost", Description = "Database host")]
    string? Host { get; set; }

    [IniValue(DefaultValue = "5432", Description = "Port number")]
    int Port { get; set; }

    [IniValue(DefaultValue = "True")]
    bool EnableSsl { get; set; }

    [IniValue(DefaultValue = "60")]
    TimeSpan CommandTimeout { get; set; }
}
```

The framework reads/writes the following file:

```ini
; Database connection settings
[Database]
; Database host
Host = localhost
; Port number
Port = 5432
EnableSsl = True
CommandTimeout = 00:01:00
```

The `[IniSection]` attribute's `Description` is written as a comment above the section header.
Each `[IniValue]` `Description` is written as a comment above its key.  Keys with no
description are written without a comment line.

---

## Value formats by type

| .NET type | Example INI value | Notes |
|-----------|------------------|-------|
| `string` | `Hello World` | Stored as-is |
| `bool` | `True` / `False` | Case-insensitive on read |
| `int`, `long`, `uint`, `ulong` | `42`, `-7`, `0` | Invariant culture |
| `double`, `float`, `decimal` | `3.14`, `-0.5` | Invariant culture (`.` decimal separator) |
| `DateTime` | `2024-03-15T10:30:00.0000000` | ISO 8601 round-trip format |
| `TimeSpan` | `00:30:00` | Constant "c" format (`[-][d.]hh:mm:ss[.fffffff]`) |
| `Guid` | `d3b07384-d9b7-4e57-b9c3-7e3a5f1e5e4d` | Standard format |
| `Uri` | `https://example.com/api` | Full URI string |
| Any `enum` | `Warning`, `Information` | Enum member name (case-insensitive on read) |
| `Nullable<T>` | Empty string for `null`; otherwise the inner type's format | |

### Enum example

```csharp
[IniSection("Logging")]
public interface ILoggingSettings : IIniSection
{
    [IniValue(DefaultValue = "Information")]
    LogLevel Level { get; set; }
}
```

```ini
[Logging]
Level = Warning
```

---

## Collection types

### Lists and arrays

`List<T>`, `IList<T>`, `ICollection<T>`, `IEnumerable<T>`, and `T[]` are stored as a
**comma-separated single value** on one line:

```csharp
[IniSection("App")]
public interface IAppSettings : IIniSection
{
    [IniValue(DefaultValue = "Feature1,Feature2,Feature3")]
    List<string>? EnabledFeatures { get; set; }

    [IniValue(DefaultValue = "8080,8081,8082")]
    int[]? ListenPorts { get; set; }
}
```

```ini
[App]
EnabledFeatures = Feature1,Feature2,Feature3
ListenPorts = 8080,8081,8082
```

### Dictionaries

`Dictionary<string, TValue>` and `IDictionary<string, TValue>` use **dotted sub-key
notation** — one line per entry in the form `PropertyName.key = value`:

```csharp
[IniSection("App")]
public interface IAppSettings : IIniSection
{
    [IniValue(DefaultValue = "timeout=30,retries=3")]
    Dictionary<string, int>? ServiceConfig { get; set; }
}
```

```ini
[App]
ServiceConfig.timeout = 30
ServiceConfig.retries = 3
```

> **Note:** The `DefaultValue` string for a dictionary property uses the inline
> comma-separated `key=value` format (e.g. `"timeout=30,retries=3"`).  The actual
> file uses one `PropertyName.key = value` line per entry, which is the canonical
> storage format.

---

## Comments in the written file

When the framework writes an INI file it outputs:

1. A `;` comment line before each **section** that has a `Description`.
2. A `;` comment line before each **key** that has a `Description`.
3. A blank line between sections.

```ini
; General application settings
[General]
; Application name
AppName = MyApp
; Maximum retry attempts
MaxRetries = 5
EnableLogging = True

; Database connection settings
[Database]
; Database host
Host = localhost
Port = 5432
```

---

## Read-only properties

Properties declared with `[IniValue(ReadOnly = true)]`, or with a getter-only
interface (`{ get; }` without a setter), are **read from the file** but **never
written back**.  They do not appear in a newly-saved file if the value still matches
the compiled default.

---

## The `[__metadata__]` section

When `EnableMetadata()` is called on the builder, every `Save()` prepends a special
section:

```ini
[__metadata__]
Version   = 1.2.0
CreatedBy = MyApplication
SavedOn   = 15/03/2024 10:30:00

[General]
AppName = MyApp
```

This section is managed entirely by the framework.  Its keys are never forwarded
to unknown-key callbacks.  See [[Migration]] for how to use this for version-gated upgrades.

---

## Defaults file and constants file

The layered loading model supports two additional INI files that share the **same
format** as the user file:

- **Defaults file** (`AddDefaultsFile`) — supplies baseline values; the user file overrides them.
- **Constants file** (`AddConstantsFile`) — supplies admin-forced values; they override everything.

```ini
; /etc/myapp/defaults.ini — shipped with the application
[General]
MaxRetries = 3
EnableLogging = False

[Database]
Host = db.internal
Port = 5432
```

```ini
; /etc/myapp/constants.ini — set by the administrator; users cannot override these
[Database]
Host = prod-db.corp.example
EnableSsl = True
```

---

## Complete example

The following shows a realistic INI file produced by `config.Save()` for an application
with two sections:

```ini
; General application settings
[General]
; Application name
AppName = MyApp
MaxRetries = 5
EnableLogging = True
Threshold = 3.14

; Server configuration
[Server]
; Hostname or IP address
Host = 0.0.0.0
Port = 8080
; Log level: Trace, Debug, Information, Warning, Error, Critical
LogLevel = Information
; Allowed origins (comma-separated)
AllowedOrigins = https://app.example.com,https://admin.example.com
; Connection timeout
Timeout = 00:00:30
```

---

## Encoding

Files are read and written as **UTF-8** by default.  Use `WithEncoding` on the
builder when interoperating with legacy files that use a different encoding:

```csharp
IniConfigRegistry.ForFile("legacy.ini")
    .AddSearchPath(".")
    .WithEncoding(Encoding.Latin1)
    .RegisterSection<IAppSettings>(new AppSettingsImpl())
    .Build();
```

---

## Language pack files

The internationalization subsystem uses the same INI syntax.  See
[[Internationalization#language-file-format]] for the language-specific conventions
(escape sequences, key normalisation, file naming).

---

## See also

- [[Defining-Sections]] — `[IniSection]` and `[IniValue]` attribute reference
- [[Loading-Configuration]] — search paths, defaults file, constants file
- [[Loading-Life-Cycle]] — value resolution order
- [[Value-Converters]] — built-in converters and adding custom ones
- [[Migration]] — `[__metadata__]`, unknown-key callbacks, version-gated upgrades
- [[Internationalization]] — language pack `.ini` files
