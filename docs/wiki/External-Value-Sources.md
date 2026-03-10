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

Value sources are applied after the user file and constants files.
When multiple sources are registered, they are applied in registration order with the
last one winning.  Async sources are applied **after** all sync sources.

> **Important:** Async value sources (`IValueSourceAsync`) are only consulted during
> `BuildAsync()` and `ReloadAsync()`.  The synchronous `Build()` and `Reload()` skip them.

---

## Notifying of runtime value changes

When a source's value changes at runtime, raise `ValueChanged` and call `config.Reload()`
(or `config.ReloadAsync()`) to re-apply all sources and update the section properties:

```csharp
// Notify the framework that a value changed (e.g. from a background polling thread):
valueSource.RaiseChanged(sectionName: "App", key: "FeatureFlag");
await config.ReloadAsync();
```

---

## Value resolution order

External value sources are the **highest-priority** layer — they override defaults,
user file values, and constants files.  See [[Loading-Life-Cycle]] for the full order.

---

## See also

- [[Loading-Life-Cycle]] — where sources fit in the resolution order
- [[Loading-Configuration]] — `AddValueSource` and other builder methods
- [[Reloading]] — reacting to value source changes at runtime
- [[Async-Support]] — `IValueSourceAsync` in detail
