# Empty-Over-Null Semantics (`EmptyWhenNull`)

By default, reference-type properties (`string`, `List<T>`, `T[]`, `Dictionary<K,V>`, …)
return `null` when no value is present in the INI file and no `[DefaultValue]` has been
specified.  `EmptyWhenNull` lets you opt into returning an empty value instead.

**Which empty value is produced depends on the property type:**

| Property type | Empty representation |
|---|---|
| `string` | `string.Empty` |
| `List<T>` / `IList<T>` / collection interfaces | Empty `List<T>` |
| `T[]` | Empty array (`T[0]`) |
| `Dictionary<K,V>` / `IDictionary<K,V>` | Empty `Dictionary<K,V>` |
| Value types (`int`, `bool`, `double`, …) | **Not affected** — always use `default(T)` or `[DefaultValue]` |

> **Precedence:** `[DefaultValue]` (or `[IniValue(DefaultValue=…)]`) **always wins**.
> When a default value is set, that default is applied by `ResetToDefaults()` regardless
> of any `EmptyWhenNull` flag.  `EmptyWhenNull` only controls what happens when there is
> **no** explicit default.

---

## Level 1 — Property (`[IniValue(EmptyWhenNull = true)]`)

The finest-grained scope: opt a single property into empty-over-null semantics.

```csharp
[IniSection("App")]
public interface IAppSettings : IIniSection
{
    // "" instead of null when the key is absent or has no value
    [IniValue(EmptyWhenNull = true)]
    string? Description { get; set; }

    // [] instead of null
    [IniValue(EmptyWhenNull = true)]
    List<string>? Tags { get; set; }

    // [] instead of null
    [IniValue(EmptyWhenNull = true)]
    string[]? Codes { get; set; }

    // DefaultValue takes precedence over EmptyWhenNull in ResetToDefaults:
    [IniValue(DefaultValue = "hello", EmptyWhenNull = true)]
    string? Greeting { get; set; }  // "hello" (not "")

    // No EmptyWhenNull — remains null when absent
    string? OptionalNote { get; set; }
}
```

**When does this apply?**

- `ResetToDefaults()` (called internally at the start of every `Load()` / `Reload()`):
  produces the empty representation when no `DefaultValue` is set.
- `OnRawValueSet` (called when a key is read from the INI file):
  `null` raw values (empty or absent key) are converted to `""` before the converter sees them,
  so the converter also produces an empty collection / string rather than `null`.

---

## Level 2 — Section (`[IniSection(EmptyWhenNull = true)]`)

Apply empty-over-null to **every non-value-type property** in the section at compile time.
This is equivalent to placing `[IniValue(EmptyWhenNull = true)]` on each qualifying property
individually.

```csharp
// Every reference-type property in this section returns empty instead of null
[IniSection("App", EmptyWhenNull = true)]
public interface IAppSettings : IIniSection
{
    string? Description { get; set; }      // → string.Empty when absent
    List<string>? Tags { get; set; }       // → [] when absent
    string[]? Codes { get; set; }          // → [] when absent

    // DefaultValue still wins in ResetToDefaults:
    [DefaultValue("hello")]
    string? Greeting { get; set; }         // → "hello" (not "")

    // Value types are never affected:
    int Counter { get; set; }              // → 0 (default(int))
}
```

> **Implementation note:** The source generator reads `[IniSection(EmptyWhenNull = true)]`
> at compile time and sets `EmptyWhenNull = true` on every non-value-type `PropertyModel`
> before generating the code.  The resulting generated class is identical to one you would
> get by annotating each property with `[IniValue(EmptyWhenNull = true)]`.

---

## Level 3 — IniConfig (`IniConfigBuilder.EmptyWhenNull()`)

Apply empty-over-null to **every reference-type property across all registered sections**
at runtime.  Useful when you want the same semantics everywhere without annotating each
interface.

```csharp
IniConfigRegistry.ForFile("app.ini")
    .AddSearchPath(AppContext.BaseDirectory)
    .EmptyWhenNull()                                   // global flag
    .RegisterSection<IAppSettings>(new AppSettingsImpl())
    .RegisterSection<IDbSettings>(new DbSettingsImpl())
    .Build();
```

After this, every `string?`, `List<T>?`, `T[]?`, and `Dictionary<K,V>?` property in both
`IAppSettings` and `IDbSettings` will return an empty value instead of `null` when absent.
Properties that already have a `[DefaultValue]` or compile-time `EmptyWhenNull` are
unaffected by the runtime flag — their existing behaviour is unchanged.

> **Implementation note:** `EmptyWhenNull()` sets `IniConfig.GlobalEmptyWhenNull = true`.
> Before each `Load()` / `Reload()` cycle the framework propagates this flag to
> `IniSectionBase.GlobalEmptyWhenNull` on every registered section.  The source generator
> emits a `GlobalEmptyWhenNull ? "" : null` runtime check for reference-type properties
> that do not already have compile-time `EmptyWhenNull`.

---

## Precedence summary

The table below shows which value a reference-type property gets when absent from the INI
file, depending on how the flags are set.

| `[DefaultValue]` set? | `EmptyWhenNull` (any level) | Result of `ResetToDefaults` |
|---|---|---|
| ✓ | any | The configured default value |
| ✗ | ✓ | Empty (`""` / `[]` / `new T[0]` / `{}`) |
| ✗ | ✗ | `null` (default C# behaviour) |

---

## Combining levels

All three levels compose cleanly.  When a property already has compile-time
`EmptyWhenNull` (property- or section-level), the runtime `GlobalEmptyWhenNull` flag
has no additional effect on it — the compile-time path runs instead.

```csharp
// Section-level flag applies to Label and Items.
// Config-level flag (EmptyWhenNull()) would also cover them, but the section-level
// compile-time path takes priority — the behaviour is identical.
[IniSection("App", EmptyWhenNull = true)]
public interface IAppSettings : IIniSection
{
    string? Label { get; set; }       // compile-time EmptyWhenNull
    List<string>? Items { get; set; } // compile-time EmptyWhenNull

    // Explicit property-level EmptyWhenNull — same outcome as above, just more explicit.
    [IniValue(EmptyWhenNull = true)]
    string? Extra { get; set; }

    // DefaultValue always wins:
    [DefaultValue("default-label")]
    string? WithDefault { get; set; }
}
```

---

## See also

- [[Defining-Sections]] — `[IniSection]` and `[IniValue]` attribute reference
- [[Loading-Configuration]] — `IniConfigBuilder` fluent API including `EmptyWhenNull()`
- [[Registry-API]] — complete `IniConfigBuilder` method reference
- [[Value-Converters]] — how `ConvertFromRaw` turns `""` into empty collections
