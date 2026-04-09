# Dapplo.Ini

A powerful, source-generator–backed INI file configuration framework for .NET.

- ✅ Define configuration sections as **annotated interfaces**
- ✅ Concrete classes are **auto-generated** — no boilerplate
- ✅ **Layered** loading: defaults → user file → admin constants → external sources
- ✅ **In-place reload** with singleton guarantee (safe for DI)
- ✅ **File locking** to prevent external modification
- ✅ **File-change monitoring** with optional consumer hook
- ✅ **INotifyDataErrorInfo** validation for WPF / Avalonia / WinForms binding
- ✅ **Transactional** updates with rollback support
- ✅ **INotifyPropertyChanged** / **INotifyPropertyChanging** baked in
- ✅ **Lifecycle hooks** implementable directly in the section interface via static virtuals (C# 11+)
- ✅ Extensible **value converter** system (custom converters for encryption etc.)
- ✅ **Async support** — `BuildAsync`, `ReloadAsync`, `SaveAsync`, async lifecycle hooks, and `IValueSourceAsync` for REST APIs / remote configuration services
- ✅ **DI-friendly async loading** — `InitialLoadTask` lets consumers await the initial load while sections are injected as singletons immediately
- ✅ **Migration support** — unknown-key callbacks, `IUnknownKey<TSelf>`, and an optional `[__metadata__]` section for version-gated upgrades
- ✅ **Internationalization** — `.ini`-based language packs with source-generated type-safe interfaces, progressive fallback, plugin-friendly deferred loading, and optional file monitoring
- ✅ **Listeners** — zero-overhead `IIniConfigListener` interface for logging and diagnostics; notified on load, save, reload, missing files, unknown keys, and type-conversion failures; reused for both INI and i18n subsystems
- ✅ **Runtime-only properties** — in-memory values with defaults that are never loaded from or saved to disk (`[IniValue(RuntimeOnly = true)]`)
- ✅ **Constants protection** — keys loaded from an admin constants file are write-protected; `IsConstant(key)` lets UI code disable the corresponding input control
- ✅ **Empty-over-null semantics** — opt reference-type properties into returning empty values instead of `null` via `[IniValue(EmptyWhenNull = true)]`, `[IniSection(EmptyWhenNull = true)]`, or `IniConfigBuilder.EmptyWhenNull()`
- ✅ **Ignore-defaults / ignore-constants** — mark a section or individual property to be skipped when applying defaults files or constants files (`[IniSection(IgnoreDefaults = true)]`, `[IniValue(IgnoreConstants = true)]`, etc.)
- ✅ **Generic meta-model access** — enumerate all sections by name, iterate their keys, and inspect property types at runtime without needing the concrete section types at compile time

---

## Quick start

```csharp
// 1. Define a section interface — [IniSection] is optional; use standard .NET attributes
using System.ComponentModel;

[Description("Application settings")]   // section comment — no [IniSection] needed
public interface IAppSettings : IIniSection
{
    [DefaultValue("MyApp")]
    string? AppName { get; set; }

    [DefaultValue(8080)]
    int Port { get; set; }
}

// 2. Load at application startup
var config = IniConfigRegistry
    .ForFile("appsettings.ini")
    .AddSearchPath(AppContext.BaseDirectory)
    .RegisterSection<IAppSettings>(new AppSettingsImpl())  // generated class
    .Build();

// 3. Read values — the section object is a stable singleton
var settings = config.GetSection<IAppSettings>();
Console.WriteLine($"{settings.AppName} is listening on port {settings.Port}");

// 4. Save changes
settings.AppName = "MyApp v2";
config.Save();
```

> **Tip:** You can also retrieve the config later without holding a reference.
> When exactly one INI file is registered, the file name is optional:
> ```csharp
> var settings = IniConfigRegistry.GetSection<IAppSettings>();
> // or, when multiple configs are registered:
> var settings = IniConfigRegistry.GetSection<IAppSettings>("appsettings.ini");
> ```

---

## Documentation pages

| Page | Description |
|------|-------------|
| [[Getting-Started]] | Installation, NuGet package, and first steps |
| [[Ini-File-Format]] | INI file syntax, value formats, collections, comments, and a complete example |
| [[Defining-Sections]] | `[IniSection]` and `[IniValue]` attribute reference, generated class naming |
| [[Loading-Life-Cycle]] | Complete order in which values are resolved during `Build()` / `Reload()` |
| [[Plugin-Registrations]] | Three-phase `Create()` / `AddSection` / `Load()` pattern for plugin-based apps |
| [[Loading-Configuration]] | `IniConfigBuilder` fluent API, AppData, write target |
| [[Reloading]] | `Reload()`, `ReloadAsync()`, singleton guarantee, `Reloaded` event |
| [[Saving]] | `Save()`, `SaveAsync()`, `IBeforeSave`, `IAfterSave` |
| [[File-Locking]] | Holding the file open exclusively |
| [[File-Change-Monitoring]] | `FileSystemWatcher`, `ReloadDecision`, postponed reload |
| [[External-Value-Sources]] | `IValueSource` and `IValueSourceAsync` — environment variables, registry, REST APIs |
| [[Validation]] | `IDataValidation<TSelf>` and `INotifyDataErrorInfo` |
| [[Lifecycle-Hooks]] | `IAfterLoad`, `IBeforeSave`, `IAfterSave` and their async variants |
| [[Async-Support]] | `BuildAsync`, `ReloadAsync`, `SaveAsync`, `IValueSourceAsync`, `InitialLoadTask`, and async lifecycle hooks |
| [[Singleton-and-DI]] | Singleton guarantee, ASP.NET Core / Microsoft DI integration, `InitialLoadTask` |
| [[Transactional-Updates]] | `ITransactional`, `Begin()`, `Commit()`, `Rollback()` |
| [[Property-Change-Notifications]] | `INotifyPropertyChanged` / `INotifyPropertyChanging` |
| [[Value-Converters]] | Built-in converters, custom converters, encrypting sensitive values |
| [[Registry-API]] | Complete `IniConfigRegistry`, `LanguageConfigRegistry`, `IniConfig`, and `IniConfigBuilder` API reference |
| [[Migration]] | Unknown-key callbacks, `IUnknownKey<TSelf>`, `EnableMetadata`, and version-gated upgrades |
| [[Internationalization]] | `LanguageConfigRegistry`, `.ini`-based language packs, `LanguageConfigBuilder`, progressive fallback, file monitoring |
| [[Listeners]] | `IIniConfigListener` — zero-overhead observer for load, save, reload, unknown keys, and conversion failures; works for both INI and i18n configs |
| [[Gap-Analysis]] | Feature comparison with the older `Dapplo.Config.Ini` library |
| [[Async-Await-Benefits]] | Background analysis of async/await trade-offs (pre-implementation reference) |
| [[Runtime-Only-and-Constants]] | Runtime-only properties and constants-file protection |
| [[Ignore-Defaults-and-Constants]] | Opt sections or individual properties out of defaults files and/or constants files |
| [[Empty-When-Null]] | `EmptyWhenNull` at property, section, and config levels — return empty instead of `null` for absent reference-type values |
| [[Generic-Access]] | `GetSections()`, `GetSection(name)`, `GetKeys()`, and `GetPropertyType()` — inspect the configuration meta-model at runtime without compile-time type knowledge |
