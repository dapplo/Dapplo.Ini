# Lifecycle Hooks

Three optional lifecycle hooks let you react to load/save events.  Each has both a
synchronous and an asynchronous variant.

## Synchronous hooks

| Interface | Trigger | Return type | Behaviour |
|-----------|---------|-------------|-----------|
| `IAfterLoad<TSelf>` | After `Build()` and `Reload()` | `void` | Normalize, decrypt, derive values |
| `IBeforeSave<TSelf>` | Before writing to disk | `bool` | Return `false` to cancel the save |
| `IAfterSave<TSelf>` | After a successful write | `void` | Notify, audit, log |

## Async hooks

| Interface | Trigger | Return type | Behaviour |
|-----------|---------|-------------|-----------|
| `IAfterLoadAsync` | After `BuildAsync()` and `ReloadAsync()` | `Task` | Async normalize, decrypt, derive values |
| `IBeforeSaveAsync` | Before writing to disk during `SaveAsync()` | `Task<bool>` | Return `false` to cancel the save |
| `IAfterSaveAsync` | After a successful async write | `Task` | Async notify, audit, log |

> **Fallback:** When `BuildAsync` / `ReloadAsync` is used, if a section implements only
> `IAfterLoad` (not `IAfterLoadAsync`), the sync hook is called automatically. The same
> fallback applies to `IBeforeSave` / `IAfterSave` during `SaveAsync`.

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

## Async hooks: partial-class pattern

Use `IAfterLoadAsync`, `IBeforeSaveAsync`, and/or `IAfterSaveAsync` when your hook logic
needs to perform async operations (e.g. fetching a decryption key, calling a remote
validator, writing an audit log via async I/O).

**Step 1 — Declare the interface:**

```csharp
// IMySettings.cs
[IniSection("App")]
public interface IMySettings : IIniSection, IAfterLoadAsync, IBeforeSaveAsync, IAfterSaveAsync
{
    string? Value { get; set; }
}
```

**Step 2 — Implement in a partial class:**

```csharp
// MySettingsImpl.cs  ← consumer-written file; sits alongside MySettingsImpl.g.cs
namespace MyApp;

public partial class MySettingsImpl
{
    // ── IAfterLoadAsync ───────────────────────────────────────────────────────
    public async Task OnAfterLoadAsync(CancellationToken cancellationToken)
    {
        // e.g. decrypt a sensitive value fetched from a secrets vault
        Value = await SecretsVault.DecryptAsync(Value, cancellationToken);
    }

    // ── IBeforeSaveAsync ──────────────────────────────────────────────────────
    public async Task<bool> OnBeforeSaveAsync(CancellationToken cancellationToken)
    {
        // Validate remotely — return false to cancel the save
        return await RemoteValidator.IsValidAsync(Value, cancellationToken);
    }

    // ── IAfterSaveAsync ───────────────────────────────────────────────────────
    public Task OnAfterSaveAsync(CancellationToken cancellationToken)
    {
        Console.WriteLine("Settings saved!");
        return Task.CompletedTask;
    }
}
```

Async hooks are called by `BuildAsync`, `ReloadAsync`, and `SaveAsync`.  If a section
implements only the synchronous variants, those are called as a fallback.  Mixing sync
and async sections in the same `IniConfig` is fully supported.

---

## See also

- [[Saving]] — triggering `Save()` / `SaveAsync()` and the save hooks
- [[Reloading]] — triggering `Reload()` / `ReloadAsync()` and the `IAfterLoad` hook
- [[Validation]] — `IDataValidation<TSelf>` for property-level validation
- [[Async-Support]] — async lifecycle hooks and `IValueSourceAsync`
