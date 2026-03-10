# Property-Change Notifications

Set `[IniValue(NotifyPropertyChanged = true)]` on any property.
The generated class will implement `INotifyPropertyChanging` and `INotifyPropertyChanged`:

```csharp
[IniSection]
public interface IThemeSettings : IIniSection
{
    [IniValue(DefaultValue = "Light", NotifyPropertyChanged = true)]
    string? Theme { get; set; }
}
```

---

## Subscribing to change events

```csharp
var theme = config.GetSection<IThemeSettings>();

((INotifyPropertyChanged)theme).PropertyChanged += (_, e)
    => Console.WriteLine($"{e.PropertyName} changed");

((INotifyPropertyChanging)theme).PropertyChanging += (_, e)
    => Console.WriteLine($"{e.PropertyName} is about to change");
```

---

## Integration with WPF / Avalonia data binding

Because the generated class implements both `INotifyPropertyChanging` and
`INotifyPropertyChanged`, it integrates naturally with WPF, Avalonia, WinForms, and
any other MVVM framework that relies on these interfaces:

```xml
<!-- WPF XAML — binds directly to the section object -->
<TextBlock Text="{Binding Theme}" />
```

```csharp
// Set the section as the DataContext
DataContext = config.GetSection<IThemeSettings>();
```

---

## See also

- [[Defining-Sections]] — `[IniValue]` attribute reference
- [[Validation]] — `IDataValidation<TSelf>` — requires `NotifyPropertyChanged = true`
