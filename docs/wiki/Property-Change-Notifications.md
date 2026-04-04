# Property-Change Notifications

Property-change notifications let data bindings (WPF, Avalonia, WinForms, MVVM frameworks)
react immediately when a setting value changes.  Dapplo.Ini generates the boilerplate for
`INotifyPropertyChanged` and `INotifyPropertyChanging` automatically — simply extend the
relevant BCL interface(s) on your section interface.

---

## Opting in — extend the BCL interface

Extend `INotifyPropertyChanged` and/or `INotifyPropertyChanging` directly on your section
interface.  The source generator detects these and emits the event declarations and setter
invocations for **every property** in the generated class:

```csharp
[IniSection]
public interface IThemeSettings : IIniSection, INotifyPropertyChanged
{
    [IniValue(DefaultValue = "Light")]
    string? Theme { get; set; }

    [IniValue(DefaultValue = "12")]
    int FontSize { get; set; }
}
```

Both `Theme` and `FontSize` will now fire `PropertyChanged` whenever they change.

If you also want `PropertyChanging` (fired *before* the value changes), extend
`INotifyPropertyChanging` as well:

```csharp
[IniSection]
public interface IThemeSettings
    : IIniSection, INotifyPropertyChanged, INotifyPropertyChanging
{
    // ...
}
```

Interfaces that do **not** extend either BCL interface generate no event declarations
(better performance, lower memory use).

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

## Suppressing events per property

Use `[IniValue(SuppressPropertyChanged = true)]` or
`[IniValue(SuppressPropertyChanging = true)]` to opt individual properties out.
This is useful for internal bookkeeping properties that must not pollute the event stream:

```csharp
[IniSection]
public interface IThemeSettings : IIniSection, INotifyPropertyChanged, INotifyPropertyChanging
{
    [IniValue(DefaultValue = "Light")]
    string? Theme { get; set; }   // fires both events

    /// <summary>Internal cache flag — suppress both kinds of events.</summary>
    [IniValue(SuppressPropertyChanged = true, SuppressPropertyChanging = true)]
    bool IsDirty { get; set; }    // fires neither event
}
```

You can suppress only one event type while keeping the other:

```csharp
// Fires PropertyChanged but NOT PropertyChanging
[IniValue(SuppressPropertyChanging = true)]
string? Theme { get; set; }
```

---

## Same-value optimization

When at least one event is emitted for a property, the generated setter contains an early-return
equality check:

```csharp
// Generated pseudo-code
set
{
    if (EqualityComparer<string?>.Default.Equals(_theme, value)) return;
    PropertyChanging?.Invoke(this, new PropertyChangingEventArgs(nameof(Theme)));
    _theme = value;
    SetRawValue("Theme", ConvertToRaw(value));
    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Theme)));
}
```

Setting a property to its current value is a no-op — no events are fired and the INI
raw-value store is not updated.  This prevents infinite loops in WPF/Avalonia
two-way bindings.

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

## Validation and property-change events are independent

`IDataValidation<TSelf>` validation runs in every property setter regardless of whether
`INotifyPropertyChanged` or `INotifyPropertyChanging` are implemented.  You do not need to
extend these interfaces just to get live validation in your UI — see [[Validation]].

---

## See also

- [[Defining-Sections]] — `[IniValue]` attribute reference
- [[Validation]] — `IDataValidation<TSelf>` — `INotifyDataErrorInfo` integration
