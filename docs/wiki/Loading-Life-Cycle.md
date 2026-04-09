# Complete Loading Life-Cycle

Understanding the exact order in which values are resolved helps you predict the final
state of any property after `Build()` or `Reload()`.

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
└──────────────────────────────┬──────────────────────────────────────┘
                               │
┌──────────────────────────────▼──────────────────────────────────────┐
│ STEP 5 — Apply external value sources (AddValueSource order)        │
│   Each registered IValueSource is queried for every section/key.    │
│   Sources are applied in registration order; the last one wins.     │
│   During BuildAsync/ReloadAsync, IValueSourceAsync sources are also │
│   queried — after all sync sources, in registration order.          │
└──────────────────────────────┬──────────────────────────────────────┘
                               │
┌──────────────────────────────▼──────────────────────────────────────┐
│ STEP 6 — Fire IAfterLoad hooks                                      │
│   OnAfterLoad() is called on every section that implements          │
│   IAfterLoad / IAfterLoad<TSelf>. Use this for normalization,       │
│   decryption, derived-value calculation, etc.                       │
│   During BuildAsync/ReloadAsync, IAfterLoadAsync.OnAfterLoadAsync() │
│   is preferred; IAfterLoad is used as a fallback.                   │
└──────────────────────────────┬──────────────────────────────────────┘
                               │
┌──────────────────────────────▼──────────────────────────────────────┐
│ STEP 7 — (Build only) Acquire file lock / start file monitor        │
│   If LockFile() or MonitorFile() was configured, the file lock is   │
│   acquired and/or the FileSystemWatcher is started.                 │
└──────────────────────────────┬──────────────────────────────────────┘
                               │
                          ✅ Ready
```

**Type conversion** happens at steps 3 and 4 whenever `SetRawValue` is called.
The raw string from the INI file is passed through the registered `IValueConverter<T>`
for the property's type. Built-in converters cover all common .NET primitive types;
custom converters can be registered with `ValueConverterRegistry.Register()`.

---

## Value precedence (highest to lowest)

1. External value sources (`AddValueSource`) — applied last, win over everything
2. Constants files (`AddConstantsFile`) — admin-forced, override user file
3. User INI file (located via `AddSearchPath`) — user-editable
4. Defaults files (`AddDefaultsFile`) — baseline values
5. Compiled defaults (`[IniValue(DefaultValue = "…")]`) — fallback when nothing else is set
6. Type default (`default(T)`) — when `DefaultValue` is absent

---

## See also

- [[Loading-Configuration]] — configuring search paths, defaults, and constants files
- [[External-Value-Sources]] — implementing `IValueSource` and `IValueSourceAsync`
- [[Lifecycle-Hooks]] — `IAfterLoad` hooks (Step 6) including async variants
- [[Async-Support]] — how the async code paths differ from the synchronous ones
- [[Ignore-Defaults-and-Constants]] — opt sections/properties out of steps 2 or 4
