# Async/Await Background Analysis

This document captures the analysis that was done *before* implementing async support.
Async/await is now fully implemented — see [[Async-Support]] for practical usage.

---

## What async I/O brings to Dapplo.Ini

### 1. Application startup (`BuildAsync`)

`BuildAsync` reads one or more files from disk (defaults file, user file, constants file)
without blocking the calling thread.

- Applications with a UI message loop (WPF, Avalonia, WinUI 3) keep the UI
  **responsive** while loading.
- ASP.NET Core hosts load configuration **without blocking a thread-pool thread**,
  which matters under high-load startup scenarios.
- Startup tasks can be **parallelised** via `Task.WhenAll` when multiple INI files are
  loaded independently.

### 2. Saving (`SaveAsync`)

`SaveAsync` writes the serialised INI content to disk without blocking and respects a
`CancellationToken` so a shutdown-initiated save can be aborted gracefully.

### 3. Reloading (`ReloadAsync`)

`ReloadAsync` gives callers the ability to `await` completion before acting on updated
values and integrates naturally with reactive pipelines.

### 4. External value sources (`IValueSourceAsync`)

The synchronous `IValueSource.TryGetValue` contract forces networked sources (Azure App
Configuration, AWS Parameter Store, REST endpoints) to block a thread on every
round-trip.  `IValueSourceAsync` lets such sources use proper async I/O.

---

## Trade-offs

| Concern | Detail |
|---------|--------|
| **Surface-area growth** | Every synchronous method needs an `Async` twin, growing the public API. |
| **`async` all-the-way** | `async` in `BuildAsync()` means lifecycle hooks can also become async — `IAfterLoadAsync` etc. are provided as opt-in interfaces. |
| **Net Framework 4.8** | `async`/`await` itself is available on net48; `ValueTask` and `IAsyncEnumerable` require extra package references.  Non-generic async interfaces use `Task`/`Task<bool>`. |
| **Tiny files** | For a local INI file of a few kilobytes, async overhead may exceed the I/O time itself. The synchronous API remains the primary API. |
| **Concurrency** | Overlapping reloads are guarded by a `SemaphoreSlim` shared between `Reload()` and `ReloadAsync()`. |

---

## Implemented approach

1. **Synchronous API remains the primary API** for simple desktop and console
   applications where blocking a thread during startup is acceptable.

2. **Opt-in `*Async` overloads** for `Build`, `Save`, and `Reload` use true async I/O
   (`File.ReadAllTextAsync` / `StreamWriter.WriteAsync`) on .NET and streaming
   on .NET Framework 4.8.

3. **`IValueSourceAsync`** alongside the existing synchronous `IValueSource` for
   callers with networked sources.

4. **Async lifecycle hooks** (`IAfterLoadAsync`, `IBeforeSaveAsync`, `IAfterSaveAsync`)
   as opt-in interfaces alongside the synchronous ones.  When the async code path
   encounters a section that only implements the synchronous hook, it falls back to that
   automatically.

---

## See also

- [[Async-Support]] — practical guide to all async APIs
