# Transactional Updates

Implement `ITransactional` on your section interface to enable atomic, rollback-capable
updates.  Mark individual properties with `[IniValue(Transactional = true)]` to opt them in.

---

## Defining a transactional section

```csharp
[IniSection]
public interface ICredentials : IIniSection, ITransactional
{
    [IniValue(DefaultValue = "guest", Transactional = true)]
    string? Username { get; set; }

    [IniValue(DefaultValue = "", Transactional = true)]
    string? Password { get; set; }

    // Non-transactional properties are updated immediately
    [IniValue(DefaultValue = "0")]
    int LoginCount { get; set; }
}
```

---

## Using Begin / Commit / Rollback

```csharp
var creds = config.GetSection<ICredentials>();

creds.Begin();          // Start transaction — old values remain visible to readers

creds.Username = "alice";
creds.Password = "secret";

if (valid)
    creds.Commit();     // Make new values visible
else
    creds.Rollback();   // Discard changes — old values restored
```

---

## What Commit does

- Only properties that were **assigned during the transaction** are committed.  A reload
  that happened between `Begin()` and `Commit()` is therefore not overwritten with the values
  captured by `Begin()`.
- A committed change updates the raw value and marks the section **dirty**, so it is saved
  by the next `Save()` or auto-save — exactly like a normal setter.
- `PropertyChanging` / `PropertyChanged` for transactional properties are raised on
  `Commit()`, when the new value becomes visible — not by the setter during the transaction.
- `Rollback()` discards the pending values; no events are raised.
- A write to a key protected by a constants file throws `AccessViolationException` in the
  setter, before anything changes — also inside a transaction.

---

## Behaviour of non-transactional properties

Properties **without** `[IniValue(Transactional = true)]` are updated immediately
and are not affected by `Begin()`, `Commit()`, or `Rollback()`.

---

## See also

- [[Defining-Sections]] — `[IniValue]` attribute reference
- [[Saving]] — saving transactional sections
- [[Reloading]] — effect of reload on transactional state
