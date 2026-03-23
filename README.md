# Dapplo.Ini

[![NuGet](https://img.shields.io/nuget/v/Dapplo.Ini.svg)](https://www.nuget.org/packages/Dapplo.Ini)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

A powerful, source-generator–backed INI file configuration framework for .NET.

---

## Features

- ✅ Define configuration sections as **annotated interfaces** — no boilerplate
- ✅ Concrete classes are **auto-generated** by the included Roslyn source generator
- ✅ **Layered** loading: defaults file → user file → admin constants → external value sources
- ✅ **In-place reload** with singleton guarantee (safe for DI containers)
- ✅ **File locking** to prevent external modification while the app is running
- ✅ **File-change monitoring** with an optional consumer hook to control reload behaviour
- ✅ **INotifyDataErrorInfo** validation for WPF / Avalonia / WinForms data binding
- ✅ **Transactional** updates with `Begin()` / `Commit()` / `Rollback()` support
- ✅ **INotifyPropertyChanged** / **INotifyPropertyChanging** baked in
- ✅ **Lifecycle hooks** implementable directly in the section interface via static virtuals (C# 11+)
- ✅ Extensible **value converter** system — add custom converters (e.g. for encryption)
- ✅ **Async support** — `BuildAsync`, `ReloadAsync`, `SaveAsync`, async lifecycle hooks, and `IValueSourceAsync` for REST APIs / remote configuration services
- ✅ **DI-friendly async loading** — `InitialLoadTask` lets consumers await the initial load while sections are injected as singletons immediately
- ✅ **Plugin / distributed registrations** — `Create()` + `AddSection<T>()` + `Load()` lets plugins register sections before the single file read
- ✅ **Migration support** — unknown-key callbacks, `IUnknownKey<TSelf>`, and an optional `[__metadata__]` section for version-gated upgrades
- ✅ Targets **net48** and **net10.0**

---

## Quick start

```shell
dotnet add package Dapplo.Ini
dotnet add package Dapplo.Ini.Generator
```

```csharp
// 1. Define a section interface
[IniSection("App", Description = "Application settings")]
public interface IAppSettings : IIniSection
{
    [IniValue(DefaultValue = "MyApp")]
    string? AppName { get; set; }

    [IniValue(DefaultValue = "8080")]
    int Port { get; set; }
}

// 2. Load at application startup (AppSettingsImpl is generated automatically)
using var config = IniConfigRegistry
    .ForFile("appsettings.ini")
    .AddSearchPath(AppContext.BaseDirectory)
    .RegisterSection<IAppSettings>(new AppSettingsImpl())
    .Build();

// 3. Read values — the section object is a stable singleton
var settings = config.GetSection<IAppSettings>();
Console.WriteLine($"{settings.AppName} is listening on port {settings.Port}");

// 3b. Or retrieve from anywhere in the app — file name is optional when only one is registered
var settings = IniConfigRegistry.GetSection<IAppSettings>();

// 4. Save changes
settings.AppName = "MyApp v2";
config.Save();
```

---

## Documentation

Full documentation is available in the [project wiki](../../wiki):

- [INI File Format](../../wiki/Ini-File-Format) — syntax, value formats, collections, and examples
- [Getting Started](../../wiki/Getting-Started) — installation and first steps
- [Defining Sections](../../wiki/Defining-Sections) — `[IniSection]` and `[IniValue]` reference

## License

[MIT](LICENSE)
