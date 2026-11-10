# Complete Loading Life-Cycle

Understanding the exact order in which values are resolved helps you predict the final
state of any property after `Build()`, `Load()` or `Reload()`.

All files are read first (defaults, user, constants — bare file names are resolved through
the search paths, for `Load()` and `Reload()` alike; with `SetOverrideDirectory` the user file is
only read from the override directory, defaults are looked for there first and constants never); the steps below are then applied to the
registered sections without further file I/O. `Load`, `Reload`, `Save` and `AddSection` are
serialised by one lifecycle gate, so they never interleave.

```
┌─────────────────────────────────────────────────────────────────────┐
│ STEP 1 — Reset to compiled defaults                                 │
│   Each section's properties are set to their [IniValue(DefaultValue │
│   = …)] values (or the type default when DefaultValue is absent).   │
└──────────────────────────────┬──────────────────────────────────────┘
                               │
┌──────────────────────────────▼──────────────────────────────────────┐
│ STEP 2 — Apply defaults files (AddDefaultsFile order)               │
│   Each defaults file is read with IniFileParser and merged into the │
│   sections. Later files win over earlier ones.                      │
│   Sections marked [IniSection(IgnoreDefaults=true)] and properties  │
│   marked [IniValue(IgnoreDefaults=true)] are skipped.               │
│   Then value sources of the Default layer, in registration order    │
│   (same skip flags). Their values are inherited, never saved.       │
└──────────────────────────────┬──────────────────────────────────────┘
                               │
┌──────────────────────────────▼──────────────────────────────────────┐
│ STEP 3 — Locate and apply the user INI file                         │
│   Search directories (AddSearchPath order) are tried until the file │
│   is found. Values in the user file override all defaults.          │
│   If not found, the first writable search directory is stored for   │
│   a future Save().                                                  │
└──────────────────────────────┬──────────────────────────────────────┘
                               │
┌──────────────────────────────▼──────────────────────────────────────┐
│ STEP 4 — Apply constants files (AddConstantsFile order)             │
│   Admin-forced values that cannot be overridden by users.           │
│   These win over everything above.                                  │
│   Sections marked [IniSection(IgnoreConstants=true)] and properties │
│   marked [IniValue(IgnoreConstants=true)] are skipped (and are      │
│   never write-protected).                                           │
│   Then value sources of the Constant layer: they win over the       │
│   constants files and protect the key the same way.                 │
└──────────────────────────────┬──────────────────────────────────────┘
                               │
┌──────────────────────────────▼──────────────────────────────────────┐
│ STEP 5 — Apply value sources of the Override layer                  │
│   Each registered IValueSource is queried for every section/key.    │
│   Sources are applied in registration order; the last one wins.     │
│   Constant keys are skipped — constants always win.                 │
│   During LoadAsync/BuildAsync/ReloadAsync, IValueSourceAsync        │
│   sources are also queried — after all sync sources.                │
└──────────────────────────────┬──────────────────────────────────────┘
                               │
┌──────────────────────────────▼──────────────────────────────────────┐
│ STEP 6 — Clear dirty flags                                          │
│   Freshly loaded data is not an unsaved change.                     │
└──────────────────────────────┬──────────────────────────────────────┘
                               │
┌──────────────────────────────▼──────────────────────────────────────┐
│ STEP 7 — Fire IAfterLoad hooks                                      │
│   OnAfterLoad() is called on every section that implements          │
│   IAfterLoad / IAfterLoad<TSelf>. Use this for normalization,       │
│   decryption, migrations, derived-value calculation, etc.           │
│   Changes made here stay dirty, so they are saved (also by the      │
│   auto-save timer).                                                 │
│   During LoadAsync/BuildAsync/ReloadAsync,                          │
│   IAfterLoadAsync.OnAfterLoadAsync() is preferred; IAfterLoad is    │
│   used as a fallback.                                               │
└──────────────────────────────┬──────────────────────────────────────┘
                               │
┌──────────────────────────────▼──────────────────────────────────────┐
│ STEP 8 — (first successful load only) Post-load setup               │
│   The file lock (LockFile), file monitor (MonitorFile), save-on-exit│
│   handler (SaveOnExit) and auto-save timer (AutoSaveInterval) are   │
│   set up once — for Build(), BuildAsync() and Create() + Load().    │
└──────────────────────────────┬──────────────────────────────────────┘
                               │
                          ✅ Ready
```

**Type conversion** happens at steps 2–5 whenever `SetRawValue` is called.
The raw string from the INI file is passed through the registered `IValueConverter<T>`
for the property's type. Built-in converters cover all common .NET primitive types;
custom converters can be registered with `ValueConverterRegistry.Register()`.

---

## Before the load and late sections

- **Before the load** (after `Create()`, or `AddSection<T>()` before `Load()`) a section
  already returns the result of step 1 — its `[DefaultValue]`s, not `default(T)`.
- **`Reload()`** runs steps 1–7 in place for every registered section (step 8 is not repeated).
- **Late sections:** with `AllowLateSectionRegistration()`, a section added after the load runs
  steps 1–7 on its own, from the file data retained at the last load/reload/save — no file is
  read. See [[Plugin-Registrations#late-registration--adding-sections-after-the-load]].

---

## Value precedence (highest to lowest)

1. Value sources of the `Constant` layer (`AddValueSource(source, ValueSourceLayer.Constant)`) — admin-forced, protected
2. Constants files (`AddConstantsFile`) — admin-forced, protected; win over the layers below
3. Value sources of the `Override` layer (`AddValueSource(source)`) — applied last, but skipped for constant keys
4. User INI file (located via `AddSearchPath`) — user-editable
5. Value sources of the `Default` layer — a default the user can change
6. Defaults files (`AddDefaultsFile`) — baseline values
7. Compiled defaults (`[IniValue(DefaultValue = "…")]`) — fallback when nothing else is set
8. Type default (`default(T)`) — when `DefaultValue` is absent

`section.GetValueOrigin(key)` tells which of these supplied the current value.

Only the values of the user file (and values the application sets) are written back on save; the
other layers stay where they are, so a change there reaches the user. See [[User-Values]].

---

## See also

- [[Loading-Configuration]] — configuring search paths, defaults, and constants files
- [[External-Value-Sources]] — implementing `IValueSource` and `IValueSourceAsync`
- [[Lifecycle-Hooks]] — `IAfterLoad` hooks (Step 7) including async variants
- [[Async-Support]] — how the async code paths differ from the synchronous ones
- [[Ignore-Defaults-and-Constants]] — opt sections/properties out of steps 2 or 4
