# Reloading

`IniConfig.Reload()` re-applies the full [[Loading-Life-Cycle|loading life-cycle]]
(steps 1–6) **in place**, updating the property values of the already-registered section
objects without creating new instances.

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

// React to the reload completing:
config.Reloaded += (sender, _) =>
    Console.WriteLine($"{((IniConfig)sender!).FileName} was reloaded.");
```

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

Both flags are cleared automatically by `Reload()` (after fresh data is applied)
and by `Save()` (after a successful write).

---

## See also

- [[File-Change-Monitoring]] — automatic reload via `FileSystemWatcher`
- [[Saving]] — `Save()` and `IBeforeSave` / `IAfterSave` hooks
- [[Singleton-and-DI]] — using the singleton guarantee with dependency injection
