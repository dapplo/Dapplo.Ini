# Getting Started

## Installation

Install the main package and the source generator from NuGet:

```shell
dotnet add package Dapplo.Ini
dotnet add package Dapplo.Ini.Generator
```

The generator package adds the Roslyn source generator that automatically creates a concrete
implementation class for each `[IniSection]`-annotated interface in your project.

### Targeting .NET Framework 4.8

Both packages target `net48` and `net10.0`.  No extra configuration is required for
.NET Framework projects — add the same two NuGet references and the generator works
identically.

---

## Your first configuration section

**Step 1 — Define the interface**

```csharp
using Dapplo.Ini;

[IniSection("App", Description = "Application settings")]
public interface IAppSettings : IIniSection
{
    [IniValue(DefaultValue = "MyApp")]
    string? AppName { get; set; }

    [IniValue(DefaultValue = "8080")]
    int Port { get; set; }
}
```

**Step 2 — Load at startup**

The source generator creates `AppSettingsImpl` automatically.  Reference it when
registering the section:

```csharp
using var config = IniConfigRegistry
    .ForFile("appsettings.ini")
    .AddSearchPath(AppContext.BaseDirectory)
    .RegisterSection<IAppSettings>(new AppSettingsImpl())
    .Build();
```

**Step 3 — Use the settings**

```csharp
var settings = config.GetSection<IAppSettings>();
Console.WriteLine($"{settings.AppName} listening on port {settings.Port}");
```

**Step 4 — Save changes**

```csharp
settings.Port = 9090;
config.Save();
```

---

## Generated class naming convention

The generator derives the concrete class name from the interface name:

| Interface name | Generated class name |
|---------------|---------------------|
| `IAppSettings` | `AppSettingsImpl` |
| `IDbConfig` | `DbConfigImpl` |
| `IUserProfile` | `UserProfileImpl` |
| `ServerConfig` *(no leading I)* | `ServerConfigImpl` |

The rule is: strip a leading `I` (if present) and append `Impl`.

---

## Next steps

- [[Defining-Sections]] — full attribute reference
- [[Loading-Configuration]] — search paths, AppData, write target, constants files
- [[Loading-Life-Cycle]] — understand the exact resolution order
