# Gap Analysis: Dapplo.IniConfig vs Dapplo.Config.Ini

This document compares `Dapplo.IniConfig` (this library) with the INI-file
subsystem of the older [`Dapplo.Config`](https://github.com/dapplo/Dapplo.Config/tree/master/src/Dapplo.Config.Ini)
library.  The goal is to identify features that exist in one library but not the
other so that the most useful ones can be ported or consciously omitted.

---

## Architectural difference — the most important distinction

| Aspect | Dapplo.Config.Ini | Dapplo.IniConfig |
|--------|-------------------|------------------|
| **Implementation strategy** | Runtime **Castle DynamicProxy** — concrete classes are created at runtime by intercepting interface calls | **C# Source Generator** — concrete classes are created at compile time |
| **AOT / NativeAOT support** | ❌ Not compatible (depends on IL emit) | ✅ Compatible with linker trimming and NativeAOT |
| **Startup overhead** | Higher (proxy instantiation, reflection) | Lower (generated code runs at the same speed as hand-written code) |
| **.NET Framework support** | .NET Framework 4.6+ | `net48` and `net10.0` |
| **Type safety** | Enforced via interface; runtime errors possible for mismatched types | Enforced at compile time by the generator |

---

## Feature comparison

### Features present in Dapplo.Config.Ini **but missing** from Dapplo.IniConfig

| Feature | Dapplo.Config.Ini | Dapplo.IniConfig | Notes |
|---------|-------------------|-----------------|-------|
| **Auto-save timer** | ✅ `AutoSaveInterval` (ms) — automatically flushes dirty sections periodically | ❌ Not implemented | Useful for crash-safety. Can be approximated with a user-managed `System.Timers.Timer` calling `config.Save()`. |
| **Save on process exit** | ✅ `SaveOnExit = true` hooks `AppDomain.CurrentDomain.ProcessExit` | ❌ Not implemented | Convenient safety net; can be done manually with `AppDomain.CurrentDomain.ProcessExit`. |
| **Change tracking / dirty flag** | ✅ `HasChanges()` per section; `HasPendingChanges()` on the container | ❌ Not implemented | Allows skipping an unnecessary write when nothing changed. |
| **Write protection** | ✅ `RemoveWriteProtection()` / implicit protection after load | ❌ Not implemented | Prevents accidental modification before a transaction is started. |
| **Async I/O** | ✅ `ReadFromStreamAsync`, `WriteAsync`, `Task`-based load | ❌ Synchronous only | See [`docs/async-await-benefits.md`](async-await-benefits.md) for a full analysis. |
| **Configurable file encoding** | ✅ `FileEncoding` property (default UTF-8) | ❌ UTF-8 hardcoded in `IniFileWriter`/`IniFileParser` | Rarely needed, but some legacy systems use ISO-8859-1 or Windows-1252. |
| **Structured logging (Dapplo.Log)** | ✅ Verbose/Debug/Warn log calls throughout | ❌ No logging | Adding logging would aid diagnostics but introduces a dependency. |
| **Postfix-based defaults/constants convention** | ✅ `appname-defaults.ini`, `appname-constants.ini` discovered automatically by naming convention | ❌ Callers must pass explicit paths to `AddDefaultsFile`/`AddConstantsFile` | The convention approach requires less configuration. |
| **Section indexer by name** | ✅ `container["SectionName"]` | ❌ `GetSection<T>()` only (type-keyed) | Useful for generic/dynamic access. Can be added as `GetSection(string)` returning `IIniSection`. |

---

### Features present in Dapplo.IniConfig **but missing** from Dapplo.Config.Ini

| Feature | Dapplo.Config.Ini | Dapplo.IniConfig | Notes |
|---------|-------------------|-----------------|-------|
| **Source-generator approach** | ❌ | ✅ | Zero runtime reflection for the common path; trim- and AOT-safe. |
| **Global registry** | ❌ (DI container used instead) | ✅ `IniConfigRegistry` | Allows access anywhere without DI. |
| **Multiple ordered search paths** | ❌ Fixed directory or AppData | ✅ `AddSearchPath`, `AddSearchPaths` | Flexible layering (e.g., system → user → AppData). |
| **External value sources** | ❌ | ✅ `IValueSource` | Pluggable non-file sources (environment variables, registry, cloud config). |
| **Explicit write target** | ❌ | ✅ `SetWritablePath(path)` | Control the write location independently of the search path order. |
| **AppData helper** | ❌ (logic buried inside `IniFileContainer`) | ✅ `AddAppDataPath(applicationName)` | One-liner to use the per-user AppData directory. |
| **Transactional updates** | ❌ | ✅ `ITransactional` with `Begin`/`Commit`/`Rollback` | Atomic multi-property updates. |
| **`INotifyDataErrorInfo` validation** | ❌ | ✅ `IDataValidation<TSelf>` | WPF/Avalonia binding-aware validation. |
| **Static-virtual lifecycle hooks** | ❌ | ✅ `IAfterLoad<TSelf>`, `IBeforeSave<TSelf>`, `IAfterSave<TSelf>` | C# 11+ pattern; no allocating delegate registration. |
| **File-change postponement** | ❌ | ✅ `ReloadDecision.Postpone` + `RequestPostponedReload()` | Consumer decides when to apply external changes. |
| **File locking** | ❌ | ✅ `LockFile()` | Prevents external processes overwriting the file while the application runs. |

---

## Detailed notes on missing features

### Auto-save timer

Dapplo.Config.Ini exposes `AutoSaveInterval` (default 1 000 ms).  When dirty, the
container flushes itself on each tick.

**Workaround in Dapplo.IniConfig** (until natively supported):

```csharp
var autosave = new System.Timers.Timer(interval: 1_000) { AutoReset = true };
autosave.Elapsed += (_, _) =>
{
    if (config.HasUnsavedChanges())   // implement this per your needs
        config.Save();
};
autosave.Start();
```

**Implementation idea**: Add a `bool HasChanges` property to `IIniSection` (set by
`SetRawValue`, cleared by `Save`) and an `AutoSaveInterval(TimeSpan)` builder method
that starts an internal timer.

---

### Save on process exit

Dapplo.Config.Ini hooks `AppDomain.CurrentDomain.ProcessExit` when
`SaveOnExit = true`.

**Workaround**:

```csharp
AppDomain.CurrentDomain.ProcessExit += (_, _) => config.Save();
```

**Implementation idea**: Add `SaveOnExit()` to `IniConfigBuilder`.  The `IniConfig`
instance would register the handler internally and unregister it on `Dispose()`.

---

### Change tracking

Dapplo.Config.Ini tracks whether a section has been modified since the last read or
write via `HasChanges()`.

**Implementation idea**: Set a `_isDirty` flag inside `IniSectionBase.SetRawValue`
and clear it inside `IniConfig.Save()` and `IniConfig.Reload()`.  Expose
`bool HasChanges` on `IIniSection` and `bool HasPendingChanges()` on `IniConfig`.

---

### Configurable encoding

The parser and writer in Dapplo.IniConfig use `Encoding.UTF8` unconditionally.
Adding an `WithEncoding(Encoding)` method to `IniConfigBuilder` that is stored and
passed through to `IniFileParser.ParseFile` and `IniFileWriter.WriteFile` would
cover this gap with minimal effort.

---

### Postfix-based defaults/constants convention

Dapplo.Config.Ini automatically looks for `appname-defaults.ini` and
`appname-constants.ini` alongside the main file.  Dapplo.IniConfig requires explicit
`AddDefaultsFile` / `AddConstantsFile` calls.

**Possible addition** to `IniConfigBuilder`:

```csharp
// Automatically add "<fileName without ext>-defaults.ini" and
// "<fileName without ext>-constants.ini" from the same directories.
public IniConfigBuilder UseDefaultPostfixConvention() { … }
```

---

## Summary

`Dapplo.IniConfig` is the **modern successor** to `Dapplo.Config.Ini`, offering
significantly better AOT/trim support, no runtime proxy overhead, and a richer
feature set for layered loading, external sources, and UI data binding.

The most impactful gaps to close are:

1. **Auto-save timer** — crash-safety for long-running desktop applications.
2. **Save on process exit** — convenience safety net.
3. **Change tracking** (`HasChanges`) — prerequisite for auto-save and needed for
   efficient save-only-when-dirty behaviour.
4. **Configurable encoding** — low-effort addition for legacy-system compatibility.
5. **Async I/O** — see [`docs/async-await-benefits.md`](async-await-benefits.md).

All other gaps are either intentionally omitted (proxy model, Dapplo.Log coupling,
fixed postfix convention) or superseded by new Dapplo.IniConfig features.
