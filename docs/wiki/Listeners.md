# Listeners

The `IIniConfigListener` interface lets you observe everything that happens inside
Dapplo.Ini — file loads, saves, reloads, missing files, unrecognised keys, and type-conversion
failures — without coupling the library to any specific logging framework.

---

## The interface

```csharp
public interface IIniConfigListener
{
    void OnFileLoaded(string filePath);
    void OnFileNotFound(string fileName);
    void OnSaved(string filePath);
    void OnReloaded(string filePath);
    void OnError(string operation, Exception exception);
    void OnUnknownKey(string sectionName, string key, string? rawValue);
    void OnValueConversionFailed(string sectionName, string key, string? rawValue, Exception exception);
}
```

Implement every method, even the ones you don't need — just leave the body empty.

---

## Callback reference

| Method | When it fires |
|--------|---------------|
| `OnFileLoaded(filePath)` | The INI or language file was found and its values were applied to sections. `filePath` is the absolute path. |
| `OnFileNotFound(fileName)` | The INI or language file could not be found in any search path.  Sections fall back to their compiled defaults. `fileName` is the base name (e.g. `"app.ini"` or `"myapp.en-US.ini"`). |
| `OnSaved(filePath)` | The configuration was written to disk.  Not called by `LanguageConfig` (language files are read-only). |
| `OnReloaded(filePath)` | The file was reloaded, either via an explicit call to `Reload()` / `ReloadAsync()`, an external file-change event, or a language switch (`SetLanguage()`). For language configs the value is the IETF language tag (e.g. `"de-DE"`). |
| `OnError(operation, exception)` | An exception was thrown during `"Load"`, `"Save"`, `"Reload"`, or `"SetLanguage"`. The exception is **always re-thrown** after all listeners have been notified — existing error behaviour is unchanged. |
| `OnUnknownKey(sectionName, key, rawValue)` | A key in the file has no matching property on the registered section interface. Fired alongside the existing `IUnknownKey` / `OnUnknownKey(callback)` mechanisms. |
| `OnValueConversionFailed(sectionName, key, rawValue, exception)` | A raw string from the INI file could not be converted to the target property type. The property retains `default(T)`. Previously these failures were silently swallowed. |

---

## Minimal overhead guarantee

- When no listener is registered, `NotifyListeners` short-circuits immediately (`Count == 0` guard) — the cost is a single integer comparison per operation.
- When one or more listeners are registered, they are invoked in registration order with no synchronisation overhead (the list is built once at startup and never modified at runtime).

---

## Registering a listener — INI configuration

Call `AddListener` on the fluent builder before `Build()` / `BuildAsync()` / `Create()`:

```csharp
IniConfigRegistry.ForFile("appsettings.ini")
    .AddSearchPath(AppContext.BaseDirectory)
    .RegisterSection<IAppSettings>(new AppSettingsImpl())
    .AddListener(new MyDiagnosticListener())
    .Build();
```

Multiple listeners are supported:

```csharp
IniConfigRegistry.ForFile("appsettings.ini")
    .AddSearchPath(AppContext.BaseDirectory)
    .RegisterSection<IAppSettings>(new AppSettingsImpl())
    .AddListener(new FileLogger())
    .AddListener(new MetricsCollector())
    .Build();
```

---

## Registering a listener — language configuration

The same `IIniConfigListener` interface is reused for `LanguageConfig`.  Register via
`LanguageConfigBuilder.AddListener`:

```csharp
LanguageConfigRegistry.ForFile("myapp")        // "myapp" and "myapp.ini" are equivalent
    .AddSearchPath(langDir)
    .WithBaseLanguage("en-US")
    .RegisterSection<IMainLanguage>(new MainLanguageImpl())
    .AddListener(new MyDiagnosticListener())
    .Build();
```

Events fired by `LanguageConfig`:

| Event | Notes |
|-------|-------|
| `OnFileLoaded` | Per language file (base/fallback and active language files are separate calls). |
| `OnFileNotFound` | When a language file does not exist for a given IETF tag. |
| `OnReloaded` | On `SetLanguage()` / `SetLanguageAsync()` and file-change monitoring. The value passed is the IETF language tag, not a file path. |
| `OnError` | On any load or language-switch failure. |
| `OnSaved` | Not called — language files are read-only. |
| `OnUnknownKey` | Not called — `LanguageConfig` accepts any key in the file. |
| `OnValueConversionFailed` | Not called — `LanguageConfig` stores all values as raw strings. |

---

## Complete logging example

Below is a full listener implementation that bridges Dapplo.Ini diagnostics to a
hypothetical `Log` helper.  Adapt the `Log.*` calls to whichever logging framework you use.

```csharp
public sealed class IniDiagnosticLogger : IIniConfigListener
{
    public void OnFileLoaded(string filePath)
        => Log.Info($"[Dapplo.Ini] Loaded: {filePath}");

    public void OnFileNotFound(string fileName)
        => Log.Warn($"[Dapplo.Ini] File not found: {fileName} — using defaults");

    public void OnSaved(string filePath)
        => Log.Info($"[Dapplo.Ini] Saved: {filePath}");

    public void OnReloaded(string filePath)
        => Log.Info($"[Dapplo.Ini] Reloaded: {filePath}");

    public void OnError(string operation, Exception exception)
        => Log.Error($"[Dapplo.Ini] {operation} failed: {exception.Message}", exception);

    public void OnUnknownKey(string sectionName, string key, string? rawValue)
        => Log.Warn($"[Dapplo.Ini] Unknown key [{sectionName}] {key} = {rawValue}");

    public void OnValueConversionFailed(string sectionName, string key, string? rawValue, Exception exception)
        => Log.Warn(
            $"[Dapplo.Ini] Conversion failed [{sectionName}] {key} = \"{rawValue}\" — " +
            $"{exception.Message}");
}
```

Registration:

```csharp
var logger = new IniDiagnosticLogger();

IniConfigRegistry.ForFile("appsettings.ini")
    .AddSearchPath(AppContext.BaseDirectory)
    .RegisterSection<IAppSettings>(new AppSettingsImpl())
    .AddListener(logger)
    .Build();

LanguageConfigRegistry.ForFile("myapp")        // "myapp" and "myapp.ini" are equivalent
    .AddSearchPath(langDir)
    .WithBaseLanguage("en-US")
    .RegisterSection<IMainLanguage>(new MainLanguageImpl())
    .AddListener(logger)   // same listener instance is fine
    .Build();
```

---

## See also

- [[Registry-API]] — `IniConfigBuilder.AddListener` and `LanguageConfigBuilder.AddListener` reference
- [[Migration]] — `OnUnknownKey` callbacks and `IUnknownKey<TSelf>` for schema migration
- [[Lifecycle-Hooks]] — `IAfterLoad`, `IBeforeSave`, `IAfterSave` hooks
- [[Internationalization]] — `LanguageConfig`, `LanguageConfigBuilder`, and language packs
