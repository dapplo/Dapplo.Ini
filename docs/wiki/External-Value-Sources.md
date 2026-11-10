# External Value Sources

`IValueSource` and `IValueSourceAsync` are extensibility points that let you inject
values from **any external system** — Windows Registry, environment variables, a web
service, a secrets vault, etc.

Use `IValueSource` for synchronous sources (in-memory dictionaries, environment
variables, the registry).  Use `IValueSourceAsync` for sources that require async I/O
(REST APIs, cloud configuration services such as Azure App Configuration or AWS
Parameter Store).

---

## Implementing IValueSource (synchronous)

```csharp
public sealed class EnvironmentValueSource : IValueSource
{
    public event EventHandler<ValueChangedEventArgs>? ValueChanged;

    public bool TryGetValue(string sectionName, string key, out string? value)
    {
        // Env var convention: SECTION__KEY (double underscore separator)
        var envVar = $"{sectionName}__{key}".ToUpperInvariant();
        value = Environment.GetEnvironmentVariable(envVar);
        return value is not null;
    }
}
```

---

## Implementing IValueSourceAsync (asynchronous)

Use this when the source must perform network I/O to retrieve values.  Note that `out`
parameters are not permitted in async methods, so the method returns a
`Task<(bool Found, string? Value)>` tuple:

```csharp
public sealed class RemoteConfigSource : IValueSourceAsync
{
    private readonly HttpClient _http;

    public event EventHandler<ValueChangedEventArgs>? ValueChanged;

    public RemoteConfigSource(HttpClient http) => _http = http;

    public async Task<(bool Found, string? Value)> TryGetValueAsync(
        string sectionName, string key, CancellationToken cancellationToken = default)
    {
        var response = await _http.GetAsync(
            $"/config/{sectionName}/{key}", cancellationToken);

        if (!response.IsSuccessStatusCode)
            return (false, null);

        return (true, await response.Content.ReadAsStringAsync(cancellationToken));
    }
}
```

---

## Registering a value source

```csharp
// Synchronous source — used by both Build() and BuildAsync()
using var config = IniConfigRegistry.ForFile("myapp.ini")
    .AddSearchPath(AppContext.BaseDirectory)
    .AddValueSource(new EnvironmentValueSource())
    .RegisterSection<IAppSettings>(new AppSettingsImpl())
    .Build();

// Async source — only consulted by BuildAsync() and ReloadAsync()
var config = await IniConfigRegistry.ForFile("myapp.ini")
    .AddSearchPath(AppContext.BaseDirectory)
    .AddValueSource(new RemoteConfigSource(httpClient))   // IValueSourceAsync overload
    .RegisterSection<IAppSettings>(new AppSettingsImpl())
    .BuildAsync(cancellationToken);
```

By default a value source is applied after the user file and constants files (the `Override` layer).
When multiple sources are registered in a layer, they are applied in registration order with the
last one winning.  Async sources are applied **after** the sync sources of the same layer.
Keys that a constant has set are skipped — an `Override` source never overrides a constant. Listeners implementing `IIniConfigExtendedListener` are told about each skipped value via `OnValueSourceIgnored` (see [[Listeners]]).

Values of value sources are **never written** to the user file (see [[User-Values]]). A user value below an
`Override` or `Constant` source stays in the file, so it comes back when the source stops supplying the key.

---

## Layers

```csharp
builder
    // Below the user file: a default the user can change. A later change of the source reaches
    // every key the user didn't set.
    .AddValueSource(new RecommendedSettings(), ValueSourceLayer.Default)
    // Over the user file, not over constants (the default for AddValueSource(source))
    .AddValueSource(new EnvironmentValueSource(), ValueSourceLayer.Override)
    // Admin-forced: wins over the constants files and locks the key like them
    .AddValueSource(new PolicySource(), ValueSourceLayer.Constant);
```

| Layer | Applied | Saved | Locks the key |
|-------|---------|-------|---------------|
| `Default` | after the defaults files, before the user file; respects `IgnoreDefaults` | no (inherited, written as `; Key = value`) | no |
| `Override` | after the constants files; skipped for constant keys | no | no |
| `Constant` | after the constants files, wins over them; respects `IgnoreConstants` | never | yes: `IsConstant(key)` is `true`, setters throw `AccessViolationException` |

When a `Constant` source replaces a value of a constants file, `OnValueSourceIgnored(section, key, value)` is
raised with the constants-file value that lost, so the application can log the conflict.
Sections registered after the load (`AllowLateSectionRegistration`) get all layers too.

### Where did a value come from?

```csharp
var origin = section.GetValueOrigin("OutputFileFormat");
// origin.Kind: CompiledDefault, DefaultsFile, UserFile, ConstantsFile or ValueSource
// origin.FilePath for the files, origin.Source / origin.SourceName / origin.Layer for a value source
Console.WriteLine(origin);   // e.g. ValueSource "Windows policy (HKLM, mandatory)" (Constant)
```

Implement `INamedValueSource` on a source to give it a readable `SourceName`; otherwise its type name is used.
For a value the application set after the load, `Kind` is `UserFile` and `FilePath` is `null`.

> **Important:** Async value sources (`IValueSourceAsync`) are only consulted during
> `BuildAsync()`, `LoadAsync()`, `ReloadAsync()` and `AddSectionAsync()`.  The synchronous
> `Build()`, `Load()`, `Reload()` and `AddSection()` skip them.

---

## Notifying of runtime value changes

When a source's value changes at runtime, raise `ValueChanged`. After the first load the configuration
subscribes to `ValueChanged` of every registered source (sync and async):

- Events are debounced (default 500 ms, `builder.ValueSourceChangeDebounce(TimeSpan)`), so a burst of changes
  causes one reload.
- The section named by the event is reloaded; a wildcard event (`SectionName == null`) reloads every section.
  The reload goes through the normal path: files and value sources are read again (async sources included),
  `IAfterLoad` hooks run, `PropertyChanged` is raised for the changed values, then `Reloaded` and
  `IIniConfigListener.OnReloaded`.
- Values the application set since the load and did not save yet are kept (unless the key is now a constant or
  supplied by a source above the user file).
- It waits for a running load, reload or save, and never runs at the same time.
- Disposing the configuration unsubscribes.

```csharp
// Notify the framework that a value changed (e.g. from a background polling thread):
ValueChanged?.Invoke(this, new ValueChangedEventArgs(sectionName: "App", key: "FeatureFlag"));
```

---

## Value resolution order

`Override` sources override defaults and user file values, but **not** constants;
`Default` sources sit below the user file; `Constant` sources win over the constants files.
See [[Loading-Life-Cycle]] for the full order.

---

## See also

- [[Loading-Life-Cycle]] — where sources fit in the resolution order
- [[Loading-Configuration]] — `AddValueSource` and other builder methods
- [[Reloading]] — reacting to value source changes at runtime
- [[Async-Support]] — `IValueSourceAsync` in detail
