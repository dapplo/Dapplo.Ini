# Saving

```csharp
// Saves all section values back to the file that was loaded
// (or the first writable search path when no existing file was found).
config.Save();

// Async variant — does not block the calling thread:
await config.SaveAsync(cancellationToken);
```

> **Note:** Own `Save()` / `SaveAsync()` calls are automatically detected and never
> trigger the file-change monitor, so a save does not cause an unwanted reload loop.

---

## How the file is written

Saving is **atomic**: the content is written to a temporary file next to the INI file
(`<name>.ini.<guid>.tmp`), flushed to disk and then swapped in with `File.Replace` (retried a
few times when a virus scanner or editor briefly holds the file).  A crash or a full disk
during the save never leaves a truncated or empty INI file behind.

The file is written in place instead when it is a symbolic link (replacing it would turn the
link into a plain file) or when no temporary file can be created in its folder (for example
when only the file itself is writable).

Other details:

- A value that contains a line break is always written with escape sequences (`\n`, `\r`),
  because a raw line break could inject keys or sections into the file.  Enable
  `EscapeSequences` on the parser ([[Parser-Options]]) to read it back as a line break.
- A multi-line `Description` is written with `; ` in front of every line.
- By default only registered sections are written.  Use `PreserveUnknownSections()` to keep
  sections of plugins that are not loaded — see [[Plugin-Registrations#keeping-the-settings-of-plugins-that-are-not-loaded]].

---

## Concurrency

`Load`, `Reload`, `Save` and `AddSection` are serialised by one lifecycle gate:

| Situation | Behaviour |
|-----------|-----------|
| `Save()` while another load, reload or save is running | Waits for it to finish, then saves the latest values |
| `Save()` from inside a save (e.g. an `IBeforeSave` hook) | Returns immediately |
| `Save()` from a load/reload hook or a listener | Runs directly, as part of that operation |
| Auto-save tick while another operation is running | The tick is skipped; the next tick tries again |

> **Caution:** do not block the UI thread on `Save()` while an async hook
> (`IBeforeSaveAsync` / `IAfterSaveAsync`) awaits that same UI thread — this sync-over-async
> pattern deadlocks.  Use `await SaveAsync()` in UI code.

---

## Configuring write behavior

Use `IniWriterOptions` (file-level) or convenience methods on `IniConfigBuilder`:

```csharp
using var config = IniConfigRegistry.ForFile("app.ini")
    .AddSearchPath(AppContext.BaseDirectory)
    .WithWriterOptions(new IniWriterOptions
    {
        AssignmentSeparator = " = ",
        QuoteStyle = IniValueQuoteStyle.Double,
        EscapeSequences = true,
        WriteComments = false
    })
    .RegisterSection<IAppSettings>(new AppSettingsImpl())
    .Build();
```

Convenience methods:

- `AssignmentSeparator(...)`
- `EnableEscapeSequencesOnWrite()`
- `QuoteValuesOnWrite(...)`
- `SkipCommentsOnWrite()`

You can also override write behavior per section/property via attributes:

```csharp
[IniSection("Server", QuoteValues = IniValueQuoteStyle.Double, WriteComments = IniBooleanOption.Disabled)]
public interface IServerSettings : IIniSection
{
    [IniValue(EscapeSequences = IniBooleanOption.Enabled)]
    string? Path { get; set; }
}
```

---

## IBeforeSave hook

Implement `IBeforeSave<TSelf>` (or the non-generic `IBeforeSave`) to run logic before
the file is written.  Returning `false` cancels the save.

```csharp
[IniSection("Server")]
public interface IServerSettings
    : IIniSection,
      IBeforeSave<IServerSettings>
{
    [IniValue(DefaultValue = "8080")]
    int Port { get; set; }

    /// <summary>Validate before saving. Return false to abort.</summary>
    static new bool OnBeforeSave(IServerSettings self)
        => self.Port is >= 1 and <= 65535;
}
```

---

## IAfterSave hook

Implement `IAfterSave<TSelf>` (or the non-generic `IAfterSave`) to run logic after a
successful write.

```csharp
[IniSection("Server")]
public interface IServerSettings
    : IIniSection,
      IAfterSave<IServerSettings>
{
    [IniValue(DefaultValue = "8080")]
    int Port { get; set; }

    /// <summary>Notify other components after a successful save.</summary>
    static new void OnAfterSave(IServerSettings self)
        => Console.WriteLine($"Server settings saved — port {self.Port}");
}
```

---

## Change tracking before saving

Use `HasPendingChanges()` to avoid writing to disk when nothing has changed:

```csharp
if (config.HasPendingChanges())
    config.Save();
```

See [[Reloading#change-tracking]] for details on the dirty flag.

---

## Auto-save on a timer

See [[Loading-Configuration#auto-save-on-a-timer]] for the `AutoSaveInterval` builder method.

---

## See also

- [[Lifecycle-Hooks]] — full `IAfterLoad`, `IBeforeSave`, `IAfterSave` documentation including async variants
- [[Loading-Configuration#save-on-process-exit]] — `SaveOnExit()` builder method
- [[Reloading]] — `Reload()` / `ReloadAsync()` and `HasPendingChanges()`
- [[Async-Support]] — `SaveAsync()` and async save hooks
