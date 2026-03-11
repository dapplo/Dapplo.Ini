# Benefits of async/await for Dapplo.Ini

This document analyses what `async`/`await` support would bring to
`Dapplo.Ini` and where the trade-offs lie.

---

## Current (synchronous) model

Every public API — `Build()`, `Reload()`, `Save()` — is synchronous and blocks the
calling thread until the underlying I/O completes.  For the typical INI file (a few
kilobytes) this is imperceptible, but there are scenarios where the latency matters.

---

## Where async I/O would help

### 1. Application startup (`Build()`)

`Build()` reads one or more files from disk (defaults file, user file, constants file)
before returning.  On a cold SSD, or on a network-mapped drive, each `File.ReadAllText`
call may block for tens of milliseconds.

An `async BuildAsync()` overload would allow:
- Applications with a UI message loop (WPF, Avalonia, WinUI 3) to keep the UI
  **responsive** while loading.
- ASP.NET Core hosts to load configuration **without blocking a thread-pool thread**,
  which matters under high-load startup scenarios.
- Startup tasks to be **parallelised** via `Task.WhenAll` when multiple INI files are
  loaded independently.

```csharp
// Hypothetical async API
var config = await IniConfigRegistry.ForFile("appsettings.ini")
    .AddSearchPath(AppContext.BaseDirectory)
    .RegisterSection<IAppSettings>(new AppSettingsImpl())
    .BuildAsync(cancellationToken);
```

### 2. Saving (`Save()`)

`Save()` writes the entire serialised INI content to disk in one call.  An
`async SaveAsync()` would benefit the same categories of callers as `BuildAsync()`.
It also opens the door to respecting a `CancellationToken` so a shutdown-initiated
save can be aborted gracefully.

```csharp
await config.SaveAsync(cancellationToken);
```

### 3. Reloading (`Reload()`)

`Reload()` re-reads every layer of files.  In a file-change-monitor scenario this
is already called on a thread-pool thread (the `FileSystemWatcher` event), so the
synchronous call does not block the UI.  However, an `async ReloadAsync()` would:
- Give callers the ability to `await` the completion before acting on updated values.
- Integrate naturally with reactive pipelines (e.g., `System.Reactive`, `Channels`).

### 4. External value sources (`IValueSource`)

The current `IValueSource.TryGetValue` contract is synchronous.  A networked source
(e.g., Azure App Configuration, AWS Parameter Store, a REST endpoint) **must** block a
thread while the network round-trip completes.  An `IAsyncValueSource` interface would
let such sources use proper async I/O.

```csharp
// Hypothetical async value source
public interface IAsyncValueSource
{
    ValueTask<(bool found, string? value)> TryGetValueAsync(
        string section, string key, CancellationToken ct = default);
}
```

---

## Trade-offs and why async has not been added yet

| Concern | Detail |
|---------|--------|
| **Surface-area growth** | Every synchronous method needs an `Async` twin (or replacement), roughly doubling the public API. |
| **`async` all-the-way** | `async` in `Build()` means hooks (`IAfterLoad`, etc.) would also need to become async — a breaking change for existing implementors. |
| **Net Framework 4.8** | `Dapplo.Ini` targets `net48` as well as `net10.0`. `async`/`await` itself is available on both, but `ValueTask` and `IAsyncEnumerable` require a package reference on net48. |
| **Tiny files** | For a local INI file of a few kilobytes, async overhead (state machine allocation, scheduling) may exceed the I/O time itself. A synchronous fast-path is often preferable. |
| **Complexity** | Concurrency on `Reload()` requires a `SemaphoreSlim` or similar guard to prevent overlapping reloads — adding non-trivial complexity. |

---

## Recommended approach

1. **Keep the synchronous API as the primary API** for simple desktop and console
   applications where blocking a thread during startup is acceptable.

2. **Add opt-in `*Async` overloads** for `Build`, `Save`, and `Reload` that wrap the
   synchronous implementations on `net48` (via `Task.Run`) and use true async I/O
   (`File.ReadAllTextAsync` / `StreamWriter` with `WriteAsync`) on `net10.0`+.

3. **Introduce `IAsyncValueSource`** alongside the existing synchronous `IValueSource`
   so callers with networked sources are not forced to block.

4. **Make lifecycle hooks async-aware** by adding `ValueTask` overloads of the
   `IAfterLoad`, `IBeforeSave`, and `IAfterSave` interfaces while keeping the
   synchronous versions for backward compatibility.

---

## Summary

Async support would be most impactful for:

- **UI applications** (WPF, Avalonia, WinForms) loading on the UI thread.
- **ASP.NET Core** services loading multiple config files on startup.
- **Remote value sources** (cloud parameter stores, REST APIs).

For the common case of a small local INI file the synchronous API is efficient and
simpler to consume.  Async overloads should therefore be additive rather than
replacing the existing API.
