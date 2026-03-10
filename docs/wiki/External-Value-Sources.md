# External Value Sources

`IValueSource` is an extensibility point that lets you inject values from **any external
system** — Windows Registry, environment variables, a web service, a secrets vault, etc.

---

## Implementing IValueSource

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

## Registering a value source

```csharp
using var config = IniConfigRegistry.ForFile("myapp.ini")
    .AddSearchPath(AppContext.BaseDirectory)
    .AddValueSource(new EnvironmentValueSource())
    .RegisterSection<IAppSettings>(new AppSettingsImpl())
    .Build();
```

Value sources are applied after the user file and constants files.
When multiple sources are registered, they are applied in registration order and the
last one wins.

---

## Notifying of runtime value changes

When a source's value changes at runtime, raise `ValueChanged` and call `config.Reload()`
to re-apply all sources and update the section properties:

```csharp
// Notify the framework that a value changed (e.g. from a background polling thread):
valueSource.RaiseChanged(sectionName: "App", key: "FeatureFlag");
config.Reload();
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
