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

## Reading values generically

### Raw string — `GetRawValue`

`GetRawValue(string key)` returns the raw string exactly as it appears in the INI file,
or `null` when the key is absent from the store (e.g. the file was loaded without that
key and no default was written back):

```csharp
string? raw = section.GetRawValue("MaxRetries");
// raw == "5" (string)
```

### Typed value — `GetValue<T>`

`GetValue<T>(string key)` returns the typed value that the property getter would
return — including any default configured via `[IniValue(DefaultValue = "...")]`:

```csharp
int retries = section.GetValue<int>("MaxRetries");
// retries == 5 (int, same as section.MaxRetries)

string? name = section.GetValue<string>("AppName");
// name == "MyApp" (default applied even when absent from the file)
```

- Returns `default(T)` when the key does not exist or cannot be cast to `T`.
- For **dictionary properties**, the entire dictionary object is returned (same as the
  property getter). Use `GetRawValue("PropertyName.subkey")` to read individual entries.

```csharp
// Dictionary property: GetValue<T> returns the whole dictionary
var dict = section.GetValue<Dictionary<string, int>>("Tags");
// dict == { "a": 1, "b": 2 }

// Individual sub-key access via GetRawValue:
string? raw = section.GetRawValue("Tags.a");  // "1"
```

### Untyped value — `GetValue` (no type parameter)

`GetValue(string key)` returns the same value as the property getter but as an
untyped `object?`. This is the overload to use when iterating over all properties in a
generic loop where the .NET type of each property is not known at compile time:

```csharp
// Generic loop — no compile-time knowledge of property types needed:
foreach (string key in section.GetKeys())
{
    object? value = section.GetValue(key);       // always works
    Type?   type  = section.GetPropertyType(key); // for labelling
    Console.WriteLine($"  {key} ({type?.Name}) = {value}");
}
```

Returns `null` when the key is not found. When the type *is* known at compile time,
prefer `GetValue<T>` to avoid boxing overhead for value types.

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
        Type?   type     = section.GetPropertyType(key);
        string? rawValue = section.GetRawValue(key);

        Console.WriteLine($"  {key} : {type?.Name ?? "unknown"} = {rawValue}");
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

For typed or untyped access without knowing the concrete section type at compile time,
use `GetValue` / `GetValue<T>`:

```csharp
IIniSection? section = config.GetSection("General");
if (section != null)
{
    // Untyped — useful in generic loops
    foreach (string key in section.GetKeys())
    {
        object? value = section.GetValue(key);
        Console.WriteLine($"  {key} = {value}");
    }

    // Typed — when the type is known at compile time
    int retries = section.GetValue<int>("MaxRetries");
    string? name = section.GetValue<string>("AppName");
    Console.WriteLine($"App={name}, Retries={retries}");
}
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
| `GetValue(string key)` | Returns the current value for `key` as `object?` (same as the property getter, including defaults); ideal for generic loops |
| `GetValue<T>(string key)` | Returns the typed value for `key` (same as the property getter, including defaults), or `default(T)` when not found |
| `SetRawValue(string key, string? value)` | Stores a raw string for `key` |
| `MarkAsDirty()` | Marks the section as having unsaved changes (use after in-place collection mutations) |
| `SectionName` | The INI section name (e.g. `"General"`) |

---

## See also

- [[Registry-API]] — complete `IniConfig` and `IIniSection` member reference
- [[Defining-Sections]] — `[IniSection]` / `[IniValue]` attribute reference
- [[Loading-Configuration]] — builder API
