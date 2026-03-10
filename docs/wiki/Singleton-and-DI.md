# Singleton Guarantee and Dependency Injection

**`GetSection<T>()` always returns the same object reference**, even after `Reload()`.

This is a deliberate design choice: the framework updates the *properties* of the existing
section object in place during a reload, so any code that holds a reference to the section
will automatically see the new values without re-querying the registry.

---

## ASP.NET Core / Microsoft.Extensions.DependencyInjection

```csharp
var config = IniConfigRegistry.ForFile("appsettings.ini")
    .AddSearchPath(AppContext.BaseDirectory)
    .RegisterSection<IAppSettings>(new AppSettingsImpl())
    .Build();

// Register the section as a singleton — the reference stays valid after Reload()
builder.Services.AddSingleton(config.GetSection<IAppSettings>());

// Alternatively, expose the IniConfig itself for manual reload triggering:
builder.Services.AddSingleton(config);
```

---

## Constructor injection

```csharp
// Constructor injection — works seamlessly
public class MyService
{
    private readonly IAppSettings _settings;

    public MyService(IAppSettings settings)
    {
        _settings = settings;  // always up-to-date, even after a reload
    }
}
```

---

## Global registry shortcut

You can retrieve a section without holding a reference to `IniConfig` by using the global
`IniConfigRegistry`:

```csharp
// Anywhere in the application, after Build() has been called:
var settings = IniConfigRegistry.GetSection<IAppSettings>("appsettings.ini");
```

---

## See also

- [[Reloading]] — in-place reload and the `Reloaded` event
- [[Registry-API]] — full `IniConfigRegistry` and `IniConfig` API reference
