# Validation (INotifyDataErrorInfo)

Dapplo.Ini provides two complementary ways to add validation to a section:

1. **DataAnnotations attributes** — place standard .NET attributes directly on the property and the source generator creates the validation code for you.
2. **`IDataValidation<TSelf>`** — implement a static method inside the interface for custom, per-property rules.

Both approaches surface errors through
`System.ComponentModel.INotifyDataErrorInfo`, so WPF/Avalonia/WinForms bindings
pick up errors automatically.  **No exceptions are ever thrown** — bad values
are loaded normally; errors are stored and available to the consumer.

---

## DataAnnotations attributes (generated rules)

Place standard `System.ComponentModel.DataAnnotations` attributes on any
property of an `[IniSection]` interface.  The source generator automatically:

- implements `INotifyDataErrorInfo` on the generated class;
- runs all attribute-based checks after the file is loaded (`IAfterLoad`); and
- re-runs the check for a property whenever its setter is called.

### Supported attributes

| Attribute | Trigger condition | Default error message |
|---|---|---|
| `[Required]` | `string.IsNullOrEmpty` (strings) / `== null` (nullable refs & nullable value types) | `"{PropertyName} is required."` |
| `[Range(min, max)]` | value outside `[min, max]` (uses `IComparable`) | `"{PropertyName} must be between {min} and {max}."` |
| `[MaxLength(n)]` | `string.Length > n` (null is skipped) | `"{PropertyName} must not exceed {n} characters."` |
| `[RegularExpression(pattern)]` | `Regex.IsMatch` returns `false` (null is skipped) | `"{PropertyName} does not match the required pattern."` |

All attributes support the `ErrorMessage` property to override the default message.

```csharp
[IniSection("Server")]
public interface IServerSettings : IIniSection
{
    // Required — null/empty triggers an error
    [Required(ErrorMessage = "Host is required.")]
    string? Host { get; set; }

    // Range — value outside 1–65535 triggers an error
    [Range(1, 65535, ErrorMessage = "Port must be between 1 and 65535.")]
    [IniValue(DefaultValue = "8080")]
    int Port { get; set; }

    // MaxLength — more than 50 characters triggers an error
    [MaxLength(50)]
    string? Tag { get; set; }

    // RegularExpression — must be alphanumeric; null is always skipped
    [RegularExpression(@"^[a-zA-Z0-9]+$", ErrorMessage = "Code must be alphanumeric.")]
    string? Code { get; set; }
}
```

The generated class derives from `INotifyDataErrorInfo`, so WPF/Avalonia
bindings are satisfied without any additional code.

---

## `IDataValidation<TSelf>` — custom rules (C# 11+ / .NET 7+, recommended)

Use this pattern when your validation logic requires comparing several
properties against each other or calling external services.

```csharp
[IniSection("Server")]
public interface IServerSettings : IIniSection, IDataValidation<IServerSettings>
{
    [IniValue(DefaultValue = "8080", NotifyPropertyChanged = true)]
    int Port { get; set; }

    [IniValue(DefaultValue = "localhost", NotifyPropertyChanged = true)]
    string? Host { get; set; }

    static new IEnumerable<string> ValidateProperty(IServerSettings self, string propertyName)
    {
        return propertyName switch
        {
            nameof(Port) when self.Port is < 1 or > 65535
                => new[] { "Port must be between 1 and 65535." },
            nameof(Host) when string.IsNullOrWhiteSpace(self.Host)
                => new[] { "Host must not be empty." },
            _ => Array.Empty<string>()
        };
    }
}
```

> Validation re-runs whenever a property annotated with
> `NotifyPropertyChanged = true` (or carrying a DataAnnotations attribute)
> changes its value.  For all other properties, call the framework's
> `RunValidation(nameof(MyProp))` helper from a partial-class override to
> trigger validation explicitly.

---

## Combining DataAnnotations and custom rules

Both rule sets are **merged**: generated attribute rules are checked first,
then custom `IDataValidation<TSelf>` rules are appended.

```csharp
[IniSection("Server")]
public interface IServerSettings : IIniSection, IDataValidation<IServerSettings>
{
    [Required(ErrorMessage = "Host is required.")]
    [IniValue(NotifyPropertyChanged = true)]
    string? Host { get; set; }

    [Range(1, 65535, ErrorMessage = "Port must be between 1 and 65535.")]
    [IniValue(DefaultValue = "8080", NotifyPropertyChanged = true)]
    int Port { get; set; }

    // Custom rule — applied in addition to the generated [Required] check
    static new IEnumerable<string> ValidateProperty(IServerSettings self, string propertyName)
    {
        if (propertyName == nameof(Host) &&
            string.Equals(self.Host, "banned", StringComparison.OrdinalIgnoreCase))
            yield return "The hostname 'banned' is not allowed.";
    }
}
```

---

## Legacy instance-method pattern (.NET Framework / non-generic)

For .NET Framework 4.x or when you prefer instance methods in a separate file,
implement the non-generic `IDataValidation` and provide the implementation in a partial class:

```csharp
[IniSection("Server")]
public interface IServerSettings : IIniSection, IDataValidation
{
    [IniValue(DefaultValue = "8080", NotifyPropertyChanged = true)]
    int Port { get; set; }
}

// Partial class provides the instance implementation
public partial class ServerSettingsImpl
{
    public IEnumerable<string> ValidateProperty(string propertyName)
    {
        if (propertyName == nameof(Port) && Port is < 1 or > 65535)
            yield return "Port must be between 1 and 65535.";
    }
}
```

---

## Handling validation results

The generated class implements `INotifyDataErrorInfo`.  There are several ways
to consume the errors depending on your context.

### WPF / Avalonia binding

Bindings automatically call `GetErrors` when `ErrorsChanged` fires.
Add `Validation.ErrorTemplate` to your control to display them:

```xml
<!-- WPF — ValidatesOnNotifyDataErrors is true by default -->
<TextBox Text="{Binding Port, UpdateSourceTrigger=PropertyChanged}" />
```

### Code-behind / service layer

Inspect errors directly through the `INotifyDataErrorInfo` interface:

```csharp
var section = new ServerSettingsImpl();
// … build/load …

var errorInfo = (INotifyDataErrorInfo)section;

// Check whether any property is in error
if (errorInfo.HasErrors)
{
    // Retrieve errors for one property
    var portErrors = errorInfo.GetErrors(nameof(IServerSettings.Port))
                              .Cast<string>()
                              .ToList();

    // Retrieve all errors (pass null or empty string)
    var allErrors = errorInfo.GetErrors(null)
                             .Cast<string>()
                             .ToList();
}

// Subscribe to changes
errorInfo.ErrorsChanged += (_, e) =>
    Console.WriteLine($"Validation changed for: {e.PropertyName}");
```

### IDataValidation dispatch interface

For headless/service scenarios you can call `ValidateProperty` directly via
the non-generic `IDataValidation` dispatch interface:

```csharp
var validation = (IDataValidation)section;
var errors = validation.ValidateProperty(nameof(IServerSettings.Port)).ToList();
```

This works whether the section uses DataAnnotations attributes, `IDataValidation<TSelf>`,
or both.

---

## Validation errors after loading — and settings screens

### How errors survive from load to UI

When the INI file is loaded (`Build()` / `Reload()`), the generator automatically
runs all validation rules via an `IAfterLoad` hook and stores any errors in the
section's `_validationErrors` dictionary.  This happens for **all** validation
approaches — DataAnnotations attributes, `IDataValidation<TSelf>`, or both.
No exceptions are thrown; bad values are stored as-is and the errors are available
immediately after `Build()` returns.

You can query them straight away:

```csharp
var config = IniConfigRegistry.ForFile("myapp.ini")
    .AddSearchPath(appDir)
    .RegisterSection<IServerSettings>()
    .Build();

var section  = config.GetSection<IServerSettings>();
var errorInfo = (INotifyDataErrorInfo)section;

if (errorInfo.HasErrors)
{
    // Errors detected in the loaded file — log or show a notification
    foreach (var err in errorInfo.GetErrors(null).Cast<string>())
        Console.WriteLine(err);
}
```

### Opening a WPF / Avalonia settings screen

WPF and Avalonia only re-query `INotifyDataErrorInfo.GetErrors()` for a binding
when the `ErrorsChanged` event fires **after** the binding is established.
If errors were stored during `Build()` but the settings window was opened *later*,
the bindings set up by that window will not see the pre-existing errors — because
`ErrorsChanged` was fired before the bindings existed.

**Solution:** call `RunAllValidations()` after the window is shown.  This method
re-validates every property and fires `ErrorsChanged` for each one, allowing all
bindings in the newly opened window to pick up the current error state.

```csharp
// ViewModel or code-behind for the settings window:
public partial class SettingsWindow : Window
{
    private readonly ServerSettingsImpl _settings;

    public SettingsWindow(ServerSettingsImpl settings)
    {
        _settings = settings;
        DataContext = settings;
        InitializeComponent();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        // Re-raise ErrorsChanged for every property so that bindings in this
        // window can display any validation errors that were detected on load.
        _settings.RunAllValidations();
    }
}
```

> **Why `ServerSettingsImpl` and not `IServerSettings`?**
> `RunAllValidations()` is a generated method on the concrete implementation class,
> not part of the section interface.  There are two idiomatic ways to obtain it:
>
> **Option 1 — hold the reference at registration time** (simplest):
> ```csharp
> var settingsImpl = new ServerSettingsImpl();
> var config = IniConfigRegistry.ForFile("myapp.ini")
>     .RegisterSection<IServerSettings>(settingsImpl)
>     .Build();
> // Store settingsImpl in your DI container / service locator alongside IServerSettings
> services.AddSingleton<IServerSettings>(settingsImpl);
> services.AddSingleton<ServerSettingsImpl>(settingsImpl); // same instance!
> ```
>
> **Option 2 — cast from the interface** (when you only have `IServerSettings`):
> ```csharp
> var section = config.GetSection<IServerSettings>();
> if (section is ServerSettingsImpl impl)
>     impl.RunAllValidations();
> ```

### Calling `RunAllValidations()` explicitly in lifecycle hooks

If you implement `IAfterLoad` in a partial class (for example when also doing
post-load normalisation), call `RunAllValidations()` inside `OnAfterLoad()` to
ensure errors are populated:

```csharp
// In a separate partial class file:
public partial class ServerSettingsImpl : IAfterLoad
{
    public void OnAfterLoad()
    {
        // e.g. normalise the host name
        if (Host is not null)
            Host = Host.Trim().ToLowerInvariant();

        // Re-validate after normalisation so errors reflect the final values
        RunAllValidations();
    }
}
```

> **Note:** When a section does **not** explicitly implement `IAfterLoad`, the
> generator emits its own `IAfterLoad` bridge that calls `RunAllValidations()`
> automatically, so you only need the explicit call above when you override the
> hook yourself.

---

## Post-load validation — DataAnnotations only (legacy helper)

The generated class also exposes a narrower helper, `RunAllAttributeValidations()`,
that re-validates only the properties annotated with DataAnnotations attributes
(skipping any custom `IDataValidation<TSelf>` rules).  Prefer `RunAllValidations()`
in new code; `RunAllAttributeValidations()` is retained for backward compatibility.

---

## WPF / Avalonia binding

The generated class automatically implements `INotifyDataErrorInfo`, so WPF/Avalonia
bindings pick up errors without any additional code:

```xml
<!-- WPF XAML — Binding.ValidatesOnNotifyDataErrors=True is the default in .NET -->
<TextBox Text="{Binding Port, UpdateSourceTrigger=PropertyChanged}" />
```

---

## See also

- [[Property-Change-Notifications]] — `INotifyPropertyChanged` / `INotifyPropertyChanging`
- [[Defining-Sections]] — standard .NET attribute support (`[DefaultValue]`, `[Description]`, `[DataMember]`, `[IgnoreDataMember]`)
- [[Lifecycle-Hooks]] — `IAfterLoad`, `IBeforeSave`, `IAfterSave`
