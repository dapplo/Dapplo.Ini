# Drafts (edit dialogs)

A settings dialog that binds its controls straight to the live sections changes the configuration with every
keystroke: the rest of the application sees half-finished edits, auto-save writes them to the `.ini` while the dialog
is still open, and **Cancel** can't undo anything.

A **draft** is a detached copy of a section. The dialog binds to the draft; the rest of the application keeps seeing
the live section until the user confirms with **OK**.

```csharp
using Dapplo.Ini.Interfaces;   // CreateDraft(), ApplyDraft(), RefreshDraft(), ...

ICoreConfiguration draft = coreConfiguration.CreateDraft();
draft.OutputFilePath = @"C:\Screenshots";      // only the draft changes

iniConfig.Apply(draft);                         // OK: write the changed keys to the live section, save once
// Cancel: simply drop the draft
```

---

## Drafts versus transactions

| | Draft | [[Transactional-Updates]] (`ITransactional`) |
|---|---|---|
| The dialog sees its own edits at once (bindings, `MultiBinding`s, previews, validation) | ✅ — the dialog is bound to the draft | ❌ — during a transaction the getters return the old values |
| The rest of the application sees the edits before OK | ❌ | ❌ |
| Auto-save writes the edits before OK | ❌ — a draft is never saved | ✅ non-transactional properties are written immediately |
| Cancel | drop the draft | `Rollback()` |
| Works for every property, without `[IniValue(Transactional = true)]` | ✅ | ❌ |

A transaction hides pending values from **everyone**, including the dialog that makes them. A draft gives the dialog a
copy of its own. The two work together: applying a draft to an `ITransactional` section wraps the writes in
`Begin()` / `Commit()`, so readers see all applied transactional properties switch at once.

---

## What a draft is

`section.CreateDraft()` returns a new instance of the same generated class (typed as the section interface, or as
`IIniSection` when that is all the caller knows — e.g. sections registered by plugins):

- **Every value is copied.** Collections (`List<T>`, `IList<T>`, arrays, `Dictionary<,>`, sub-key dictionaries, sets)
  are copied too, so `draft.Tags.Add("x")` never touches the live list. Runtime-only and `[IgnoreDataMember]`
  properties are copied as well. The copy is generated code: no reflection.
- **The metadata is the same.** Defaults, descriptions, value origins (`GetValueOrigin`, `IsUserValue`) and the
  protections: `IsConstant(key)` answers the same for constants files and policies (value sources in the `Constant`
  layer), and writing a locked key throws `AccessViolationException` exactly as it does on the live section. A
  converter like Greenshot's `FixedToEnabledConverter`, which calls `section.IsConstant(propertyName)` on the bound
  section, keeps working.
- **It has its own events and validation.** `PropertyChanging` / `PropertyChanged` and `INotifyDataErrorInfo`
  (`[Range]`, `[Required]`, `IDataValidation<TSelf>`, …) are raised on the draft. All validations run when the draft is
  created, so errors of the current values show up right away.
- **It is invisible to the configuration.** A draft is not registered, never saved (not even by auto-save), not
  reloaded when the file changes, and no lifecycle hook (`OnAfterLoad`, `OnBeforeSave`, migrations) runs for it.
  Registering a draft throws.
- **It knows where it came from.** `draft.GetDraftSource()` (or `IniSectionBase.DraftSource`) returns the live section,
  `draft.IsDraft()` tells drafts apart. The draft records which keys were assigned on it
  (`GetDraftAssignedKeys()`) and the live value of every key when it was created, the *base value*
  (`GetDraftBaseValue(key)`).

---

## Applying a draft

```csharp
IReadOnlyList<string> written = draft.ApplyDraft();          // to the section it was created from
IReadOnlyList<string> written = draft.ApplyTo(liveSection);  // to another section of the same type

// Several sections at once, saved once:
IReadOnlyDictionary<IIniSection, IReadOnlyList<string>> written = iniConfig.Apply(core, editor, plugin);
await iniConfig.ApplyAsync(new IIniSection[] { core, editor, plugin });
```

The rules:

1. **Only changed keys are written:** keys that were assigned on the draft (or collections changed in place) whose
   value differs from the base value. `draft.GetDraftChanges()` returns these keys, e.g. to enable an OK button.
2. **The user's edit wins.** When the live value of such a key changed while the dialog was open, the draft's value is
   still written: the user edited that key.
3. **Keys the user didn't touch are never written.** Runtime state that changes while the dialog is open (a file
   counter a capture increments, a window position) stays as it is.
4. **The normal setters are used.** `PropertyChanging` / `PropertyChanged`, the dirty flag, user values, validation and
   locks behave as for any other write. The locks of all keys (of all drafts, for `IniConfig.Apply`) are checked
   *before* anything is written: when an administrator locked a key while the dialog was open, `Apply` throws
   `AccessViolationException` and nothing changes.
5. **Atomic for readers:** an `ITransactional` section that is not already in a transaction gets `Begin()` /
   `Commit()` around the writes.
6. **The draft stays usable.** The written values become the new base values, so an **Apply** button can apply the
   draft and keep the dialog open; applying again writes nothing until the draft changes again.

`IniConfig.Apply` / `ApplyAsync` additionally check that every draft was created from a section of that configuration,
pause auto-save while they apply, and save once when anything was written (and the configuration has a file path).
`ApplyDraft` / `ApplyTo` don't save; the configuration saves the section as usual (auto-save, `Save()`).

## Discarding a draft

Just drop it. The live section holds no reference to the draft (no event subscriptions), so nothing is left behind,
whether the draft was applied or not.

## Refreshing a draft

When a dialog stays open for a long time, `draft.RefreshDraft()` takes over the current live values of all keys that
were **not** assigned (nor changed in place) on the draft, and raises `PropertyChanged` on the draft for each key that
changes. It also takes over the current locks (e.g. after a reload brought a new constants file or policy). It returns
the keys whose value changed.

---

## Threads

Create, edit and apply a draft on one thread, normally the UI thread. A draft is not meant to be shared between
threads. The live section can still be changed by other threads while the dialog is open; `ApplyDraft` writes only
the keys the user changed.

---

## WPF example

The view model holds one draft per section the dialog shows. The window binds to the view model's drafts, OK applies
them, Cancel (or closing the window) drops them.

```csharp
using Dapplo.Ini;
using Dapplo.Ini.Interfaces;

public sealed class SettingsViewModel
{
    private readonly IniConfig _iniConfig;

    public SettingsViewModel(IniConfig iniConfig, ICoreConfiguration core, IEditorConfiguration editor, IEnumerable<IIniSection> pluginSections)
    {
        _iniConfig = iniConfig;
        Core = core.CreateDraft();
        Editor = editor.CreateDraft();
        PluginSections = pluginSections.Select(s => s.CreateDraft()).ToList();   // the host only knows IIniSection
    }

    // Bind the controls to these: {Binding Core.OutputFilePath}, {Binding Editor.FreehandSensitivity}, ...
    public ICoreConfiguration Core { get; }
    public IEditorConfiguration Editor { get; }
    public IReadOnlyList<IIniSection> PluginSections { get; }

    public bool HasChanges => Drafts.Any(d => d.GetDraftChanges().Count > 0);

    private IEnumerable<IIniSection> Drafts => new IIniSection[] { Core, Editor }.Concat(PluginSections);

    /// <summary>OK: applies the changed keys of every section and saves once.</summary>
    public void Ok() => _iniConfig.Apply(Drafts.ToArray());

    /// <summary>Apply (dialog stays open).</summary>
    public void Apply() => _iniConfig.Apply(Drafts.ToArray());
}
```

```xml
<!-- Disabled when an administrator fixed the value: works on the draft, which knows the same locks -->
<TextBox Text="{Binding Core.OutputFilePath, UpdateSourceTrigger=PropertyChanged, ValidatesOnNotifyDataErrors=True}"
         IsEnabled="{Binding Core, Converter={StaticResource FixedToEnabledConverter}, ConverterParameter=OutputFilePath}" />
```

```csharp
// SettingsWindow code-behind
private void OnOk(object sender, RoutedEventArgs e)
{
    // Generated sections with validation implement INotifyDataErrorInfo
    if (new object[] { ViewModel.Core, ViewModel.Editor }.OfType<INotifyDataErrorInfo>().Any(d => d.HasErrors))
        return;
    ViewModel.Ok();
    DialogResult = true;
}

// Cancel / closing with X: nothing to do, the drafts are dropped with the view model.
```

---

## Limitations

- Drafts are available for sections generated by `Dapplo.Ini.Generator` (the generated class needs its parameterless
  constructor). Language sections (internationalization) are not supported.
- Elements of collections are copied by value for value types and strings. For other element types of properties stored
  in the INI file the value is copied by serializing it with its converter and parsing it again; reference types of
  runtime-only / ignored properties that are not collections are shared, not copied.
- A draft of a section that is in a transaction copies the committed values (what readers see).
