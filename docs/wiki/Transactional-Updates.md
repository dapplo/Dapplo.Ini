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

## Behaviour of non-transactional properties

Properties **without** `[IniValue(Transactional = true)]` are updated immediately
and are not affected by `Begin()`, `Commit()`, or `Rollback()`.

---

## See also

- [[Defining-Sections]] — `[IniValue]` attribute reference
- [[Saving]] — saving transactional sections
- [[Reloading]] — effect of reload on transactional state
