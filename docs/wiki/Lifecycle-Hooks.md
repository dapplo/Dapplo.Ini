# Lifecycle Hooks

Three optional lifecycle hooks let you react to load/save events.

| Interface | Trigger | Return type | Behaviour |
|-----------|---------|-------------|-----------|
| `IAfterLoad<TSelf>` | After `Build()` and `Reload()` | `void` | Normalize, decrypt, derive values |
| `IBeforeSave<TSelf>` | Before writing to disk | `bool` | Return `false` to cancel the save |
| `IAfterSave<TSelf>` | After a successful write | `void` | Notify, audit, log |

---

## Generic static-virtual pattern (recommended, C# 11 / .NET 7+)

Implement the generic interfaces and override the `static` hook methods directly inside
the section interface — no separate partial class file required:

```csharp
[IniSection("Server")]
public interface IServerSettings
    : IIniSection,
      IAfterLoad<IServerSettings>,
      IBeforeSave<IServerSettings>,
      IAfterSave<IServerSettings>
{
    [IniValue(DefaultValue = "8080")]
    int Port { get; set; }

    [IniValue(DefaultValue = "localhost")]
    string? Host { get; set; }

    // ── Lifecycle hook implementations — live inside the interface ─────────────

    /// <summary>Normalize loaded values.</summary>
    static new void OnAfterLoad(IServerSettings self)
    {
        if (self.Host is not null)
            self.Host = self.Host.Trim().ToLowerInvariant();
    }

    /// <summary>Validate before saving. Return false to abort.</summary>
    static new bool OnBeforeSave(IServerSettings self)
        => self.Port is >= 1 and <= 65535;

    /// <summary>Notify other components after a successful save.</summary>
    static new void OnAfterSave(IServerSettings self)
        => Console.WriteLine($"Server settings saved — {self.Host}:{self.Port}");
}
```

The source generator detects these generic interfaces and emits a bridge in the
generated class so the framework can dispatch the hooks at runtime.

### Method signatures

| Interface | Method signature | Default behaviour |
|-----------|-----------------|-------------------|
| `IAfterLoad<TSelf>` | `static virtual void OnAfterLoad(TSelf self)` | No-op |
| `IBeforeSave<TSelf>` | `static virtual bool OnBeforeSave(TSelf self)` | Returns `true` |
| `IAfterSave<TSelf>` | `static virtual void OnAfterSave(TSelf self)` | No-op |

---

## Legacy: partial-class pattern (.NET Framework / instance methods)

If you target **.NET Framework** (4.x), or prefer instance methods in a separate file,
implement the non-generic `IAfterLoad`, `IBeforeSave`, and/or `IAfterSave` interfaces
and provide the implementations in a `partial class` alongside the generated code.

**Step 1 — Declare the interface:**

```csharp
// IMySettings.cs
[IniSection("App")]
public interface IMySettings : IIniSection, IAfterLoad, IBeforeSave, IAfterSave
{
    string? Value { get; set; }
}
```

**Step 2 — Add a partial class file** named after the **generated class** — not the interface.
The generated class for `IMySettings` is `MySettingsImpl`, so create `MySettingsImpl.cs`:

```csharp
// MySettingsImpl.cs  ← consumer-written file; sits alongside MySettingsImpl.g.cs
namespace MyApp;

public partial class MySettingsImpl
{
    // ── IAfterLoad ────────────────────────────────────────────────────────────
    public void OnAfterLoad()
    {
        // Called after Build() and Reload() complete
        Value ??= "loaded-default";
    }

    // ── IBeforeSave ───────────────────────────────────────────────────────────
    public bool OnBeforeSave()
    {
        return Value is not null;  // cancel save if Value is null
    }

    // ── IAfterSave ────────────────────────────────────────────────────────────
    public void OnAfterSave()
    {
        Console.WriteLine("Settings saved!");
    }
}
```

> **Key rule:** The partial class must be in the **same namespace** as the generated class
> (i.e. the same namespace as the interface) and must have the **exact same class name**
> (`{InterfaceName-without-leading-I}Impl`).

**Step 3 — .NET Framework startup pattern:**

```csharp
// Program.cs / App.xaml.cs
private static IMySettings? _settings;

static void Main()
{
    var config = IniConfigRegistry.ForFile("myapp.ini")
        .AddSearchPath(AppDomain.CurrentDomain.BaseDirectory)
        .RegisterSection<IMySettings>(new MySettingsImpl())
        .Build();

    // Store the section reference once — it never changes, even after Reload()
    _settings = config.GetSection<IMySettings>();

    // … rest of startup
}
```

---

## See also

- [[Saving]] — triggering `Save()` and the save hooks
- [[Reloading]] — triggering `Reload()` and the `IAfterLoad` hook
- [[Validation]] — `IDataValidation<TSelf>` for property-level validation
