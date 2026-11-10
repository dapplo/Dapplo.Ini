# Dapplo.Ini.Generator

The Roslyn source generator for [Dapplo.Ini](https://www.nuget.org/packages/Dapplo.Ini): it generates the concrete
section classes (e.g. `AppSettingsImpl` for `IAppSettings`) from your annotated section interfaces, with
change notification, defaults, validation and lifecycle hooks. Works in SDK-style and .NET Framework projects.

```shell
dotnet add package Dapplo.Ini
dotnet add package Dapplo.Ini.Generator
```

```csharp
public interface IAppSettings : IIniSection
{
    [DefaultValue(8080)]
    int Port { get; set; }
}

var settings = new AppSettingsImpl();   // generated
```

See [Defining sections](https://github.com/dapplo/Dapplo.Ini/wiki/Defining-Sections) and the [README](https://github.com/dapplo/Dapplo.Ini#readme).
