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

---

## Quick start

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

> **Tip:** You can also retrieve the config later without holding a reference:
> ```csharp
> var settings = IniConfigRegistry.GetSection<IAppSettings>("appsettings.ini");
> ```

---

## Documentation pages

| Page | Description |
|------|-------------|
| [[Getting-Started]] | Installation, NuGet package, and first steps |
| [[Defining-Sections]] | `[IniSection]` and `[IniValue]` attribute reference, generated class naming |
| [[Loading-Life-Cycle]] | Complete order in which values are resolved during `Build()` / `Reload()` |
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
| [[Registry-API]] | Complete `IniConfigRegistry`, `IniConfig`, and `IniConfigBuilder` API reference |
| [[Migration]] | Unknown-key callbacks, `IUnknownKey<TSelf>`, `EnableMetadata`, and version-gated upgrades |
| [[Gap-Analysis]] | Feature comparison with the older `Dapplo.Config.Ini` library |
| [[Async-Await-Benefits]] | Background analysis of async/await trade-offs (pre-implementation reference) |
