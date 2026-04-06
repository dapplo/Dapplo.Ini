# Generic Meta-Model Access

Sometimes an application needs to inspect or traverse the configuration structure
**without knowing the concrete section types at compile time** — for example:

- A **settings UI** that builds input controls dynamically for any section
- A **migration tool** that iterates all sections and their keys to transform values
- A **generic serializer** that needs to know the declared type of each property
- A **diagnostic / inspection tool** that enumerates the registered configuration at runtime

The `IniConfig` and `IIniSection` APIs provide everything needed for these scenarios.

---

## Enumerating sections

### By type (typed access)

The standard typed accessor is `GetSection<T>()`:

```csharp
var settings = config.GetSection<IAppSettings>();
```

### By name (generic access)

Use `GetSection(string sectionName)` to look up a section by its INI name when the
concrete type is not known at compile time:

```csharp
IIniSection? section = config.GetSection("AppSettings");
if (section != null)
{
    string? raw = section.GetRawValue("Port");
    Console.WriteLine($"Port (raw) = {raw}");
}
```

The look-up is **case-insensitive** and returns `null` when no matching section is registered
(rather than throwing), so callers can guard appropriately.

### All sections

Use `GetSections()` to iterate every registered section:

```csharp
foreach (IIniSection section in config.GetSections())
{
    Console.WriteLine($"[{section.SectionName}]");
}
```

---

## Enumerating keys

Once you have an `IIniSection` reference, call `GetKeys()` to enumerate the declared
property key names:

```csharp
foreach (string key in section.GetKeys())
{
    string? rawValue = section.GetRawValue(key);
    Console.WriteLine($"  {key} = {rawValue}");
}
```

**Source-generated sections** return the compile-time property list — exactly the keys
that will be read from and written to the INI file, excluding
`[IgnoreDataMember]`-decorated properties.

**Non-generated sections** (rare; hand-written classes that extend `IniSectionBase`)
return the keys currently present in the raw backing store, i.e. those that were loaded
from the file at the last `Load()` / `Reload()`.

---

## Inspecting property types

Use `GetPropertyType(string key)` to retrieve the .NET `Type` of a declared property:

```csharp
Type? type = section.GetPropertyType("MaxRetries");
// type == typeof(int)
```

Returns `null` when `key` does not correspond to a declared property.
For source-generated sections the type is always exact (e.g. `typeof(int)`,
`typeof(string)`, `typeof(List<string>)`); the returned type is always the underlying
non-nullable type, even when the property is declared as `string?` (nullable reference
types share the same `Type` object as their non-nullable counterpart).

---

## End-to-end example

The following code inspects the full configuration model without any compile-time knowledge
of the registered section types:

```csharp
foreach (IIniSection section in config.GetSections())
{
    Console.WriteLine($"[{section.SectionName}]");

    foreach (string key in section.GetKeys())
    {
        Type?   type  = section.GetPropertyType(key);
        string? value = section.GetRawValue(key);

        Console.WriteLine($"  {key} : {type?.Name ?? "unknown"} = {value}");
    }
}
```

Example output:

```
[General]
  AppName : String = MyApp
  MaxRetries : Int32 = 5
  EnableLogging : Boolean = true
  Threshold : Double = 3.14
```

---

## API summary

### `IniConfig`

| Method | Description |
|--------|-------------|
| `GetSection<T>()` | Returns the registered section of type `T`; throws if not found |
| `GetSection(string sectionName)` | Returns the section whose `SectionName` matches (case-insensitive), or `null` if not registered |
| `GetSections()` | Returns `IEnumerable<IIniSection>` over all registered sections |

### `IIniSection`

| Method | Description |
|--------|-------------|
| `GetKeys()` | Enumerates declared property key names |
| `GetPropertyType(string key)` | Returns the .NET `Type` for the property, or `null` for unknown keys |
| `GetRawValue(string key)` | Returns the raw string stored for `key`, or `null` when absent |
| `SetRawValue(string key, string? value)` | Stores a raw string for `key` |
| `SectionName` | The INI section name (e.g. `"General"`) |

---

## See also

- [[Registry-API]] — complete `IniConfig` and `IIniSection` member reference
- [[Defining-Sections]] — `[IniSection]` / `[IniValue]` attribute reference
- [[Loading-Configuration]] — builder API
