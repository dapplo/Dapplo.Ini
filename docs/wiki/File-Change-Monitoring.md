# File-Change Monitoring

Call `.MonitorFile()` to automatically reload when the file is changed by another process.
An optional `FileChangedCallback` lets you control the reload decision:

```csharp
using var config = IniConfigRegistry.ForFile("myapp.ini")
    .AddSearchPath(AppContext.BaseDirectory)
    .MonitorFile(filePath =>
    {
        // Decide what to do when the file changes externally
        if (AppIsStartingUp)
            return ReloadDecision.Postpone;   // reload later
        if (UserIsEditing)
            return ReloadDecision.Ignore;     // skip this change
        return ReloadDecision.Reload;         // reload immediately (default)
    })
    .RegisterSection<IAppSettings>(new AppSettingsImpl())
    .Build();

// When you are ready to apply a postponed reload:
config.RequestPostponedReload();
```

The watcher reacts to `Changed`, `Created` and `Renamed` events, so editors that save by
writing a new file and swapping it in (and a file that is created after start-up) also
trigger a reload.  Rapid successive events are debounced (`debounceMs`, 200 ms by default)
into a single reload.

The reload runs on a thread-pool thread:

- An exception during that reload (e.g. the file is still locked by the editor) is reported
  via `IIniConfigListener.OnError("Reload", …)` and does not crash the process — see [[Listeners]].
- `PropertyChanged` events for the changed values are raised on that thread, so UI code must
  marshal to the UI thread itself — see [[Reloading#change-notifications-after-a-reload]].

---

## ReloadDecision values

| Value | Effect |
|-------|--------|
| `Reload` | Reload immediately — this is the default when no callback is supplied |
| `Ignore` | Skip this notification — no reload occurs |
| `Postpone` | Delay until `RequestPostponedReload()` is called |

---

## Reloaded event

Subscribe to `IniConfig.Reloaded` to be notified after each successful reload:

```csharp
config.Reloaded += (sender, _) =>
    Console.WriteLine($"{((IniConfig)sender!).FileName} was reloaded.");
```

---

## Interaction with Save()

Own `Save()` calls are automatically detected and never trigger the file-change monitor.
This means saving the file from within your application does **not** cause an unwanted
reload loop.

---

## See also

- [[Reloading]] — `Reload()` and the singleton guarantee
- [[File-Locking]] — `LockFile()`
- [[Loading-Configuration]] — full builder API reference
