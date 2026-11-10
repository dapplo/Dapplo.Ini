# User Values

Since Dapplo.Ini 2.0 the user file keeps **only what the user set**. Values that come from the
compiled defaults, the defaults files, the constants files or value sources are *inherited* and
are not stored in the user file, so a later change there reaches every user:

- a changed defaults file (`AddDefaultsFile`) reaches users who saved before,
- a changed `[DefaultValue]` in a new application version reaches existing users,
- a key removed from a constants file brings back the user's own value (or the default).

Dapplo.Ini 1.x wrote every value on the first save, which froze all of them in the user file.

---

## What is a user value?

A key holds a *user value* when its value

- was read from the user file, or
- was set by the application after the load: a property setter, `SetRawValue`, an in-place change of
  a collection followed by `MarkAsDirty()`, or a migration in `OnAfterLoad`.

```csharp
settings.IsUserValue("OutputFileFormat");   // true when the user chose a value
settings.ResetKey("OutputFileFormat");      // back to the defaults file / compiled default
settings.ResetToDefaults();                 // all keys back to their compiled defaults
```

A user value stays a user value when it equals the inherited value: the user chose it, so it survives
a later change of the default. Only `ResetKey` / `ResetToDefaults` turn it back into an inherited value.
Assigning the value a property already has is a no-op (no event, not dirty) and does not change this.

`ResetKey` gives the property the value of the defaults file (or else its compiled default), raises
`PropertyChanged` and marks the section dirty. For a sub-key dictionary it resets the whole dictionary.
A key protected by a constants file can't be reset (`AccessViolationException`).

---

## How the file looks

Inherited keys are written as commented-out lines with their description, so the file stays readable
and every setting can be found and edited by hand:

```ini
; General application settings
[General]
; Application name
; AppName = MyApp
MaxRetries = 5
; EnableLogging = True
```

A commented-out line is never read back as a value. With `PreserveFormatting()` a user who removes the
`; ` in front of such a line gets a real (user) value; the next save keeps the line where it is.
The commented-out lines are regenerated on every save (they show the current inherited value), so they
don't pile up. A comment of your own of the form `Key = text`, where `Key` is a key of that section, is
treated as such a line.

Leave inherited keys out completely:

```csharp
builder.WriteInheritedValuesAsComments(false);
// or: new IniWriterOptions { WriteInheritedValuesAsComments = false }
```

---

## Constants

A constant (a key in a constants file) is **never** written to the user file. When the user file holds
a value of its own for that key, it stays in the file untouched, so it comes back when the administrator
removes the constant.

The same holds for a value source above the user file: its value is not written, the user's own value
below it stays in the file.

---

## Writing every value

An application that relies on the user file holding every setting can opt out:

```csharp
builder.SaveAllValues();
```

Constants are still never written.

---

## Migrating existing files

A file written by Dapplo.Ini 1.x (or with `SaveAllValues()`) holds every key, so the library can't tell
which values the user chose: after the update they are all user values, and nothing changes for that user.
The application decides whether to migrate. The helper turns every user value that equals the value of
the layer below (defaults file, else compiled default) into an inherited value — once:

```csharp
// Once, on the first load of a file that wasn't written with user values only.
// Needs EnableMetadata(): the next save writes UserValuesOnly = True into [__metadata__].
builder.EnableMetadata()
       .ConvertUnchangedValuesToInherited();

// Or with a condition of your own, decided per load from the file's metadata
builder.ConvertUnchangedValuesToInherited(metadata => metadata?.Version is null or "1.0");

// Or by hand, at any time
int converted = config.ConvertUnchangedUserValuesToInherited();
config.Save();
```

The builder option runs during the load, after the files and value sources were applied and before the
`IAfterLoad` hooks, so values that migrations set stay user values. The converted sections are marked dirty,
so the next save cleans up the file. A user who chose the default on purpose loses that choice (the key
follows the default from then on); that's why it should run once, not on every load.

`IniConfig.Metadata.UserValuesOnly` tells whether the file that was read says it holds only user values.

---

## See also

- [[Saving]]
- [[Loading-Life-Cycle]]
- [[Runtime-Only-and-Constants]]
- [[Migration]]
