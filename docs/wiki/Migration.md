# Migration

INI files evolve over time.  Dapplo.Ini provides two complementary mechanisms to handle
schema changes without losing user data:

1. **Unknown-key callbacks** — surface unrecognised keys so you can copy old values to new properties.
2. **The `[__metadata__]` section** — persist file-level metadata (version, application name, timestamp)
   so `IAfterLoad` hooks can apply version-gated upgrade steps.

---

## Unknown-key callbacks

A key is *unknown* when it exists in the INI file but has no matching property on the
registered section interface.  This happens when a property is renamed or removed.

### Option A — Static hook in the interface (recommended, .NET 7+)

Implement `IUnknownKey<TSelf>` directly on the section interface using the same
static-virtual pattern as `IAfterLoad<TSelf>`:

```csharp
[IniSection("App")]
public interface IAppSettings : IIniSection, IUnknownKey<IAppSettings>
{
    // New name for what used to be called "OldTimeout"
    [IniValue(DefaultValue = "30")]
    int Timeout { get; set; }

    static void OnUnknownKey(IAppSettings self, string key, string? value)
    {
        if (key.Equals("OldTimeout", StringComparison.OrdinalIgnoreCase))
            self.Timeout = int.TryParse(value, out var t) ? t : 30;
    }
}
```

The source generator detects `IUnknownKey<TSelf>` and emits a bridge in the generated class
so the framework calls the static hook at runtime.

### Option B — Partial-class pattern (.NET Framework / instance methods)

Add `IUnknownKey` to the interface declaration and implement `OnUnknownKey` in a partial class:

```csharp
// IAppSettings.cs
[IniSection("App")]
public interface IAppSettings : IIniSection, IUnknownKey
{
    int Timeout { get; set; }
}
```

```csharp
// AppSettingsImpl.cs  (sits alongside the generated AppSettingsImpl.g.cs)
public partial class AppSettingsImpl
{
    public void OnUnknownKey(string key, string? value)
    {
        if (key.Equals("OldTimeout", StringComparison.OrdinalIgnoreCase))
            Timeout = int.TryParse(value, out var t) ? t : 30;
    }
}
```

### Option C — Global builder callback

When the migration logic is central (e.g. a logger or a shared migration service),
register a callback at the builder level instead:

```csharp
IniConfigRegistry.ForFile("appsettings.ini")
    .AddSearchPath(AppContext.BaseDirectory)
    .RegisterSection<IAppSettings>(new AppSettingsImpl())
    .OnUnknownKey((sectionName, key, value) =>
    {
        // sectionName lets you scope the handler to a particular section
        if (sectionName == "App" && key == "OldTimeout")
        {
            var settings = config.GetSection<IAppSettings>();
            settings.Timeout = int.TryParse(value, out var t) ? t : 30;
        }
    })
    .Build();
```

---

## How to rename a key

Renaming a property changes the key name in the INI file.  Existing user files still contain
the old key name.  Use an unknown-key hook to copy the value:

**Old interface:**
```ini
[App]
OldTimeout = 60
```

**New interface:**
```csharp
[IniSection("App")]
public interface IAppSettings : IIniSection, IUnknownKey<IAppSettings>
{
    [IniValue(DefaultValue = "30")]
    int Timeout { get; set; }          // renamed from OldTimeout

    static void OnUnknownKey(IAppSettings self, string key, string? value)
    {
        if (key.Equals("OldTimeout", StringComparison.OrdinalIgnoreCase))
            self.Timeout = int.TryParse(value, out var t) ? t : 30;
    }
}
```

When the next `Save()` occurs the old key is gone and `Timeout` is written with the migrated value.

---

## How to change the underlying type

Changing a property's type (e.g. `string` → `int`, or `int` → `enum`) requires parsing the
stored string value inside the unknown-key hook or inside `IAfterLoad`.

**Example — `string` → `LogLevel` enum:**

```csharp
[IniSection("Logging")]
public interface ILoggingSettings : IIniSection, IUnknownKey<ILoggingSettings>
{
    [IniValue(DefaultValue = "Information")]
    LogLevel Level { get; set; }

    // Old file had:  LogLevelString = Warning
    static void OnUnknownKey(ILoggingSettings self, string key, string? value)
    {
        if (key.Equals("LogLevelString", StringComparison.OrdinalIgnoreCase)
            && Enum.TryParse<LogLevel>(value, ignoreCase: true, out var parsed))
        {
            self.Level = parsed;
        }
    }
}
```

---

## How to limit or correct values

Use `IAfterLoad<TSelf>` to clamp, sanitise, or substitute invalid values after loading:

```csharp
[IniSection("Server")]
public interface IServerSettings : IIniSection, IAfterLoad<IServerSettings>
{
    [IniValue(DefaultValue = "8080")]
    int Port { get; set; }

    static void OnAfterLoad(IServerSettings self)
    {
        // Clamp port to valid range; silently correct invalid user edits
        if (self.Port is < 1 or > 65535)
            self.Port = 8080;
    }
}
```

Validation that should *surface* errors to the UI instead belongs in `IDataValidation<TSelf>` —
see [[Validation]].

---

## Version-gated migrations with `[__metadata__]`

When you need to know *which version of the application wrote the INI file* you can opt in to
the metadata section.  On every `Save()` the framework prepends a `[__metadata__]` section to
the file:

```ini
[__metadata__]
Version   = 1.2.0
CreatedBy = Greenshot
SavedOn   = 12/03/2026 07:43:32
```

`SavedOn` is formatted in the user's locale — it is intended for human inspection only and
should not be parsed programmatically.

### Enabling metadata

Call `EnableMetadata()` on the builder.  Both parameters are optional; the framework falls
back to `Assembly.GetEntryAssembly()` when they are not supplied:

```csharp
IniConfigRegistry.ForFile("appsettings.ini")
    .AddSearchPath(AppContext.BaseDirectory)
    .RegisterSection<IAppSettings>(new AppSettingsImpl())
    .EnableMetadata(version: "1.2.0", applicationName: "Greenshot")
    .Build();
```

### Accessing metadata in `IAfterLoad`

After `Build()` or `Reload()` completes, `IniConfig.Metadata` holds the values read from the
file (or `null` when the section was absent — for example on first run or when a user removed it):

```csharp
[IniSection("App")]
public interface IAppSettings : IIniSection, IAfterLoad<IAppSettings>
{
    string? DisplayName { get; set; }

    static void OnAfterLoad(IAppSettings self)
    {
        var config = IniConfigRegistry.Get("appsettings.ini");
        var meta   = config.Metadata;

        // meta is null when the file has no [__metadata__] section yet (first run).
        if (meta is null) return;

        // Parse the stored version; treat missing / unparseable as "very old".
        var stored  = Version.TryParse(meta.Version, out var v) ? v : new Version(0, 0);
        var current = typeof(IAppSettings).Assembly.GetName().Version!;

        if (stored < new Version(1, 2, 0) && current >= new Version(1, 2, 0))
        {
            // Upgrade step: apply default for a newly added property
            self.DisplayName ??= "Migrated App";
        }
    }
}
```

### Order guarantee

The `[__metadata__]` section is always written as the **first** section in the file, so its
values are available when `IAfterLoad` hooks run — even for properties declared before
other sections.

### Using metadata together with unknown-key callbacks

Both features can be active at the same time.  Keys from `[__metadata__]` are never
forwarded to `OnUnknownKey` callbacks; only keys from registered application sections trigger them:

```csharp
IniConfigRegistry.ForFile("appsettings.ini")
    .AddSearchPath(AppContext.BaseDirectory)
    .RegisterSection<IAppSettings>(new AppSettingsImpl())
    .EnableMetadata(version: "2.0.0", applicationName: "MyApp")
    .OnUnknownKey((section, key, value) =>
        logger.Warning("Unknown key {Key} in [{Section}]", key, section))
    .Build();
```

---

## Summary — choosing the right approach

| Scenario | Recommended approach |
|----------|---------------------|
| Rename a key | `IUnknownKey<TSelf>` or `IUnknownKey` partial class |
| Remove a key | `IUnknownKey<TSelf>` (no-op handler silences it) |
| Change a type | `IUnknownKey<TSelf>` with type conversion in the hook |
| Clamp / sanitise a value | `IAfterLoad<TSelf>` |
| Version-gated step (e.g. set a new default) | `EnableMetadata` + `IAfterLoad<TSelf>` |
| Central logging of unrecognised keys | `OnUnknownKey(callback)` on the builder |

---

## See also

- [[Lifecycle-Hooks]] — `IAfterLoad`, `IBeforeSave`, `IAfterSave` and async variants
- [[Validation]] — `IDataValidation<TSelf>` and `INotifyDataErrorInfo`
- [[Registry-API]] — `EnableMetadata`, `OnUnknownKey`, and `IniConfig.Metadata` reference
