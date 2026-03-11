# Gap Analysis: Dapplo.Ini vs Dapplo.Config.Ini

This document compares `Dapplo.Ini` (this library) with the INI-file
subsystem of the older [`Dapplo.Config`](https://github.com/dapplo/Dapplo.Config/tree/master/src/Dapplo.Config.Ini)
library.  The goal is to identify features that exist in one library but not the
other so that the most useful ones can be ported or consciously omitted.

---

## Architectural difference — the most important distinction

| Aspect | Dapplo.Config.Ini | Dapplo.Ini |
|--------|-------------------|------------------|
| **Implementation strategy** | Runtime **Castle DynamicProxy** — concrete classes are created at runtime by intercepting interface calls | **C# Source Generator** — concrete classes are created at compile time |
| **AOT / NativeAOT support** | ❌ Not compatible (depends on IL emit) | ✅ Compatible with linker trimming and NativeAOT |
| **Startup overhead** | Higher (proxy instantiation, reflection) | Lower (generated code runs at the same speed as hand-written code) |
| **.NET Framework support** | .NET Framework 4.6+ | `net48` and `net10.0` |
| **Type safety** | Enforced via interface; runtime errors possible for mismatched types | Enforced at compile time by the generator |

---

## Feature comparison

### Features present in Dapplo.Config.Ini **but missing** from Dapplo.Ini

| Feature | Dapplo.Config.Ini | Dapplo.Ini | Notes |
|---------|-------------------|-----------------|-------|
| **Auto-save timer** | ✅ `AutoSaveInterval` (ms) — automatically flushes dirty sections periodically | ✅ `AutoSaveInterval(TimeSpan)` builder method starts an internal `System.Threading.Timer` | Timer checks `HasPendingChanges()` before each tick to avoid unnecessary writes. |
| **Save on process exit** | ✅ `SaveOnExit = true` hooks `AppDomain.CurrentDomain.ProcessExit` | ✅ `SaveOnExit()` builder method hooks `AppDomain.CurrentDomain.ProcessExit`; handler is unregistered on `Dispose()` | Works on both .NET Framework and .NET. |
| **Change tracking / dirty flag** | ✅ `HasChanges()` per section; `HasPendingChanges()` on the container | ✅ `bool HasChanges` on `IIniSection`; `bool HasPendingChanges()` on `IniConfig` | Flag is set by `SetRawValue`, cleared by `Save()` and `Reload()`. Initial load does not mark sections dirty. |
| **Write protection** | ✅ `RemoveWriteProtection()` / implicit protection after load | ❌ Not implemented | Prevents accidental modification before a transaction is started. |
| **Async I/O** | ✅ `ReadFromStreamAsync`, `WriteAsync`, `Task`-based load | ❌ Synchronous only | See [`docs/async-await-benefits.md`](async-await-benefits.md) for a full analysis. |
| **Configurable file encoding** | ✅ `FileEncoding` property (default UTF-8) | ✅ `WithEncoding(Encoding)` builder method; encoding is passed to `IniFileParser.ParseFile` and `IniFileWriter.WriteFile` | Rarely needed, but some legacy systems use ISO-8859-1 or Windows-1252. |
| **Structured logging (Dapplo.Log)** | ✅ Verbose/Debug/Warn log calls throughout | ❌ No logging | Adding logging would aid diagnostics but introduces a dependency. |
| **Postfix-based defaults/constants convention** | ✅ `appname-defaults.ini`, `appname-constants.ini` discovered automatically by naming convention | ❌ Callers must pass explicit paths to `AddDefaultsFile`/`AddConstantsFile` | The convention approach requires less configuration. |
| **Section indexer by name** | ✅ `container["SectionName"]` | ❌ `GetSection<T>()` only (type-keyed) | Useful for generic/dynamic access. Can be added as `GetSection(string)` returning `IIniSection`. |

---

### Features present in Dapplo.Ini **but missing** from Dapplo.Config.Ini

| Feature | Dapplo.Config.Ini | Dapplo.Ini | Notes |
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

**Dapplo.Ini** now supports this natively:

```csharp
var config = IniConfigRegistry.ForFile("app.ini")
    .AddSearchPath(".")
    .AutoSaveInterval(TimeSpan.FromSeconds(1))   // check every second
    .RegisterSection<IAppSettings>(new AppSettingsImpl())
    .Build();
```

The internal `System.Threading.Timer` calls `config.HasPendingChanges()` on each tick
and only writes to disk when at least one section is dirty.  The timer is automatically
stopped when `config.Dispose()` is called.

---

### Save on process exit

Dapplo.Config.Ini hooks `AppDomain.CurrentDomain.ProcessExit` when
`SaveOnExit = true`.

**Dapplo.Ini** now supports this natively:

```csharp
var config = IniConfigRegistry.ForFile("app.ini")
    .AddSearchPath(".")
    .SaveOnExit()
    .RegisterSection<IAppSettings>(new AppSettingsImpl())
    .Build();
// config.Dispose() unregisters the ProcessExit handler automatically.
```

The handler is registered on `AppDomain.CurrentDomain.ProcessExit` and unregistered
when the `IniConfig` instance is disposed.  This works on both .NET Framework and .NET.

---

### Change tracking

Dapplo.Config.Ini tracks whether a section has been modified since the last read or
write via `HasChanges()`.

**Dapplo.Ini** now supports this natively:

```csharp
if (config.HasPendingChanges())
    config.Save();

// Or per section:
var section = config.GetSection<IAppSettings>();
if (section.HasChanges)
    Console.WriteLine("Section has unsaved changes.");
```

- `bool IIniSection.HasChanges` — set inside `IniSectionBase.SetRawValue`, which is
  invoked by every property setter in the generated class.
- `bool IniConfig.HasPendingChanges()` — returns `true` when at least one registered
  section is dirty.
- Both flags are cleared automatically by `IniConfig.Save()` (after a successful
  write) and by `IniConfig.Reload()` (after the fresh data is applied).
- The initial load in `IniConfigBuilder.Build()` also clears the flags, so freshly
  built configurations start in a clean state.

---

### Configurable encoding

The parser and writer in Dapplo.Ini now accept an optional `Encoding` parameter,
and `IniConfigBuilder` exposes `WithEncoding(Encoding)`:

```csharp
var config = IniConfigRegistry.ForFile("legacy.ini")
    .AddSearchPath(".")
    .WithEncoding(Encoding.Latin1)
    .RegisterSection<IAppSettings>(new AppSettingsImpl())
    .Build();
```

When `WithEncoding` is not called the behaviour is unchanged: UTF-8 is used for both
reading and writing.

---

### Postfix-based defaults/constants convention

Dapplo.Config.Ini automatically looks for `appname-defaults.ini` and
`appname-constants.ini` alongside the main file.  Dapplo.Ini requires explicit
`AddDefaultsFile` / `AddConstantsFile` calls.

**Possible addition** to `IniConfigBuilder`:

```csharp
// Automatically add "<fileName without ext>-defaults.ini" and
// "<fileName without ext>-constants.ini" from the same directories.
public IniConfigBuilder UseDefaultPostfixConvention() { … }
```

---

## Summary

`Dapplo.Ini` is the **modern successor** to `Dapplo.Config.Ini`, offering
significantly better AOT/trim support, no runtime proxy overhead, and a richer
feature set for layered loading, external sources, and UI data binding.

The most impactful gaps have been closed:

1. ✅ **Auto-save timer** — `AutoSaveInterval(TimeSpan)` on `IniConfigBuilder`.
2. ✅ **Save on process exit** — `SaveOnExit()` on `IniConfigBuilder`.
3. ✅ **Change tracking** (`HasChanges` / `HasPendingChanges`) — prerequisite for
   auto-save and efficient save-only-when-dirty behaviour.
4. ✅ **Configurable encoding** — `WithEncoding(Encoding)` on `IniConfigBuilder`.
5. **Async I/O** — see [[Async-Await-Benefits]].

All other gaps are either intentionally omitted (proxy model, Dapplo.Log coupling,
fixed postfix convention) or superseded by new Dapplo.Ini features.
