# Dapplo.Ini

[![NuGet](https://img.shields.io/nuget/v/Dapplo.Ini.svg)](https://www.nuget.org/packages/Dapplo.Ini)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

A powerful, source-generator–backed INI file configuration framework for .NET.

---

## Features

- ✅ Define configuration sections as **annotated interfaces** — no boilerplate
- ✅ Concrete classes are **auto-generated** by the included Roslyn source generator
- ✅ **Layered** loading: defaults file → user file → admin constants → external value sources (see below)
- ✅ The user file keeps **only user values**; inherited values are written as commented-out lines, so a changed default reaches every user
- ✅ **Flexible file location** — ordered search paths, AppData as write fallback, and `SetOverrideDirectory` to pin the file to a `--config-dir` style directory without letting users bypass admin constants
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
- ✅ **Generic meta-model access** — enumerate sections by name, iterate keys, and inspect property types at runtime without knowing the concrete types at compile time
- ✅ Targets **net48** and **net10.0**

---

## Quick start

```shell
dotnet add package Dapplo.Ini
dotnet add package Dapplo.Ini.Generator
```

```csharp
// 1. Define a section interface — [IniSection] is optional; use standard .NET attributes
[Description("Application settings")]   // section comment — no [IniSection] needed
public interface IAppSettings : IIniSection
{
    [DefaultValue("MyApp")]
    string? AppName { get; set; }

    [DefaultValue(8080)]
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

## Layered loading

Every load applies these layers in order; a later layer wins:

1. Compiled defaults (`[DefaultValue]` / `[IniValue(DefaultValue = …)]`)
2. Defaults files (`AddDefaultsFile`)
3. Value sources in the `Default` layer (`AddValueSource(source, ValueSourceLayer.Default)`) — defaults the user can change
4. The user file
5. Constants files (`AddConstantsFile`) — protected: `IsConstant(key)` is `true`, setters throw
6. Value sources in the `Constant` layer — win over the constants files and are protected the same way
7. Value sources in the `Override` layer (`AddValueSource(source)`) — skipped for constant keys

Inside a layer, sources apply in registration order (later wins). `section.GetValueOrigin(key)` tells where a
value came from (kind, file path or source, layer). A `ValueChanged` event of a source reloads the named section
(debounced, `ValueSourceChangeDebounce`). See the wiki page [External Value Sources](../../wiki/External-Value-Sources).

On save only the **user values** are written: values read from the user file or set by the application.
Values of the other layers are inherited: they are written as commented-out lines (`; Key = value`),
constants not at all. A changed defaults file or a new compiled default therefore reaches every user who
didn't choose a value. `IsUserValue(key)`, `ResetKey(key)`, `WriteInheritedValuesAsComments(false)`,
`SaveAllValues()` and the migration of files written by Dapplo.Ini 1.x are described on the wiki page
[User Values](../../wiki/User-Values).

### Windows policies

The package **Dapplo.Ini.WindowsPolicies** makes every setting administrable through Group Policy or Intune:

```csharp
builder.AddWindowsPolicies("Dapplo", "Greenshot");
// HKLM|HKCU\SOFTWARE\Policies\Dapplo\Greenshot\<Section>\<Key>               mandatory   (Constant layer)
// HKLM|HKCU\SOFTWARE\Policies\Dapplo\Greenshot\Recommended\<Section>\<Key>   recommended (Default layer)

AdmxGenerator.Generate(config, new AdmxOptions("Dapplo", "Greenshot")).WriteTo("policies"); // ADMX + ADML
```

Values are mapped by the property type, changes are picked up while the application runs, and the dotnet tool
`dapplo-ini-admx` generates the administrative template in a build. See the wiki page
[Windows Policies](../../wiki/Windows-Policies).

---

## Documentation

Full documentation is available in the [project wiki](../../wiki):

- [INI File Format](../../wiki/Ini-File-Format) — syntax, value formats, collections, and examples
- [Getting Started](../../wiki/Getting-Started) — installation and first steps
- [Defining Sections](../../wiki/Defining-Sections) — `[IniSection]` and `[IniValue]` reference
- [Generic Access](../../wiki/Generic-Access) — enumerate sections/keys and inspect property types at runtime
- [Windows Policies](../../wiki/Windows-Policies) — Group Policy / Intune, ADMX/ADML generator, `.reg` export

## License

[MIT](LICENSE)
