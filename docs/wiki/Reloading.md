# Reloading

`IniConfig.Reload()` re-applies the full [[Loading-Life-Cycle|loading life-cycle]]
(steps 1–7) **in place**, updating the property values of the already-registered section
objects — including sections added later via late registration — without creating new
instances.  The user file is re-read from `LoadedFromPath`; defaults and constants files
given as bare file names are resolved through the search paths, exactly like `Load()`.

---

## Singleton guarantee

**`GetSection<T>()` always returns the same object reference**, even after `Reload()`.

The framework updates the *properties* of the existing section object during a reload,
so any code that holds a reference to the section will automatically see the new values
without re-querying the registry.

---

## Triggering a reload

```csharp
// Explicitly trigger a reload at any time:
config.Reload();

// Async reload — does not block the calling thread:
await config.ReloadAsync(cancellationToken);

// React to the reload completing (fires after both Reload() and ReloadAsync()):
config.Reloaded += (sender, _) =>
    Console.WriteLine($"{((IniConfig)sender!).FileName} was reloaded.");
```

### Change notifications after a reload

A reload sets the values without going through the property setters.  Sections that
implement `INotifyPropertyChanged` therefore get a `PropertyChanged` event for every
property whose value the reload changed.  The events are raised **after** the reload has
completed (before `Reloaded`), on the thread that ran the reload — for a file-change reload
that is a thread-pool thread, so UI code must marshal to the UI thread itself.

### Restrictions

- `Reload()` / `ReloadAsync()` must not be called from a lifecycle hook or listener of a
  running load, reload or save: it throws `InvalidOperationException` (instead of
  deadlocking).  Call it after the operation has finished.
- A reload waits for a running save or load to finish, so it never mixes with a half-written state.
- Do not block the UI thread on `Reload()` while an async hook (`IAfterLoadAsync`) awaits
  that same UI thread — this sync-over-async pattern deadlocks. Use `ReloadAsync()` there.

---

## Automatic reload on file change

Use `MonitorFile()` on the builder to install a `FileSystemWatcher` that triggers
`Reload()` automatically when the file changes on disk.
See [[File-Change-Monitoring]] for the full callback API.

---

## Change tracking

`IniConfig.HasPendingChanges()` returns `true` when at least one registered section
has been modified since the last load or save.

```csharp
if (config.HasPendingChanges())
    config.Save();

// Per-section dirty flag:
var section = config.GetSection<IAppSettings>();
if (section.HasChanges)
    Console.WriteLine("Section has unsaved changes.");
```

Both flags are cleared automatically by `Reload()` (after fresh data is applied, *before*
the `IAfterLoad` hooks run) and by `Save()` (after a successful write).  Changes made by an
`IAfterLoad` hook — for example a migration — therefore stay dirty and are saved, also by
the auto-save timer.

Assigning a value-type or `string` property its current value does not mark the section
dirty.

### Manually signalling a change — `MarkAsDirty()`

Property setters set the dirty flag automatically. However, **in-place collection
mutations** (e.g. `section.Tags.Add("item")`) bypass the setter and therefore do not
trigger the flag. Call `MarkAsDirty()` after such mutations so that
`HasPendingChanges()` and the auto-save timer detect the change:

```csharp
// Mutation bypasses the setter — auto-save won't notice without MarkAsDirty()
section.Tags.Add("new-tag");

// Explicitly mark the section as dirty:
section.MarkAsDirty();

// Now HasPendingChanges() returns true and the auto-save timer will pick it up.
```

---

## See also

- [[File-Change-Monitoring]] — automatic reload via `FileSystemWatcher`
- [[Saving]] — `Save()` / `SaveAsync()` and `IBeforeSave` / `IAfterSave` hooks
- [[Singleton-and-DI]] — using the singleton guarantee with dependency injection
- [[Async-Support]] — `ReloadAsync()` and async lifecycle hooks
