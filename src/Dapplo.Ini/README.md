# Dapplo.Ini

A source-generator–backed INI file configuration framework for .NET (net48, net10.0).

- Configuration sections are annotated interfaces; the classes are generated (package **Dapplo.Ini.Generator**)
- Layered loading: compiled defaults → defaults files → user file → admin constants → external value sources
- The user file keeps only what the user set; a changed default reaches every user
- In-place reload, file locking, file-change monitoring, transactions, validation (INotifyDataErrorInfo),
  INotifyPropertyChanged, lifecycle hooks, async loading, migrations, internationalization

## Install

```shell
dotnet add package Dapplo.Ini
dotnet add package Dapplo.Ini.Generator
```

## Quick start

```csharp
[Description("Application settings")]
public interface IAppSettings : IIniSection
{
    [DefaultValue("MyApp")]
    string? AppName { get; set; }

    [DefaultValue(8080)]
    int Port { get; set; }
}

// AppSettingsImpl is generated
using var config = IniConfigRegistry
    .ForFile("appsettings.ini")
    .AddSearchPath(AppContext.BaseDirectory)
    .RegisterSection<IAppSettings>(new AppSettingsImpl())
    .Build();

var settings = config.GetSection<IAppSettings>();
settings.AppName = "MyApp v2";
config.Save();
```

## Documentation

- [README](https://github.com/dapplo/Dapplo.Ini#readme)
- [Wiki](https://github.com/dapplo/Dapplo.Ini/wiki)
- [Changelog](https://github.com/dapplo/Dapplo.Ini/blob/main/CHANGELOG.md)
