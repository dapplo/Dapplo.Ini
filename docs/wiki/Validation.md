# Validation (INotifyDataErrorInfo)

Implement `IDataValidation<TSelf>` on your section interface to enable WPF/Avalonia/WinForms
data binding validation.  The source generator automatically implements
`System.ComponentModel.INotifyDataErrorInfo` on the generated class and re-runs validation
whenever a property annotated with `NotifyPropertyChanged = true` changes.

---

## Generic static-virtual pattern (C# 11+ / .NET 7+, recommended)

```csharp
[IniSection("Server")]
public interface IServerSettings : IIniSection, IDataValidation<IServerSettings>
{
    [IniValue(DefaultValue = "8080", NotifyPropertyChanged = true)]
    int Port { get; set; }

    [IniValue(DefaultValue = "localhost", NotifyPropertyChanged = true)]
    string? Host { get; set; }

    // ── Validation logic — lives directly inside the interface (C# 11+) ──────
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

---

## WPF / Avalonia binding

The generated class automatically implements `INotifyDataErrorInfo`, so WPF/Avalonia
bindings pick up errors without any additional code:

```xml
<!-- WPF XAML — Binding.ValidatesOnNotifyDataErrors=True is the default in .NET -->
<TextBox Text="{Binding Port, UpdateSourceTrigger=PropertyChanged}" />
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

## See also

- [[Property-Change-Notifications]] — `INotifyPropertyChanged` / `INotifyPropertyChanging`
- [[Defining-Sections]] — `[IniValue(NotifyPropertyChanged = true)]`
- [[Lifecycle-Hooks]] — `IAfterLoad`, `IBeforeSave`, `IAfterSave`
