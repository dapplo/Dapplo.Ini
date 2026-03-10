# File Locking

Call `.LockFile()` on the builder to hold the INI file open with an exclusive write-lock
for the entire application lifetime:

```csharp
using var config = IniConfigRegistry.ForFile("myapp.ini")
    .AddSearchPath(AppContext.BaseDirectory)
    .LockFile()           // ← prevents external writes while the app is running
    .RegisterSection<IAppSettings>(new AppSettingsImpl())
    .Build();

// The lock is released when config.Dispose() is called (or when the using block exits).
```

> **Note:** The lock allows other processes to **read** the file but prevents writes.

---

## Combining with file-change monitoring

`LockFile()` and `MonitorFile()` can be used together.  The file-change monitor still
fires when another process reads and then another application writes the file *after* the
lock is released (e.g. at application exit).  However, while the lock is held, no external
writes are permitted, so no spurious reload events will occur.

---

## Releasing the lock

The file lock is automatically released when `IniConfig.Dispose()` is called.
Always wrap the configuration in a `using` statement or call `Dispose()` in your
application shutdown path:

```csharp
// using statement — lock released at end of block
using var config = IniConfigRegistry.ForFile("myapp.ini")
    .LockFile()
    .RegisterSection<IAppSettings>(new AppSettingsImpl())
    .Build();

// — or — explicit dispose
var config = IniConfigRegistry.ForFile("myapp.ini")
    .LockFile()
    .RegisterSection<IAppSettings>(new AppSettingsImpl())
    .Build();

// … application runs …

config.Dispose(); // lock released here
```

---

## See also

- [[File-Change-Monitoring]] — `MonitorFile()` and `ReloadDecision`
- [[Loading-Configuration]] — full builder API reference
