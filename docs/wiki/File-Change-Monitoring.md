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
