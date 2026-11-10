# Dapplo.Ini.WindowsPolicies

Makes every setting of a [Dapplo.Ini](https://www.nuget.org/packages/Dapplo.Ini) application administrable through
Windows policies: Group Policy (gpedit.msc, GPMC), Intune or a `.reg` file.

```csharp
builder.AddWindowsPolicies("Dapplo", "Greenshot");
```

| Kind | Registry key | Effect |
| --- | --- | --- |
| Mandatory | HKLM or HKCU `SOFTWARE\Policies\<Vendor>\<Product>\<Section>\<Key>` | the key is constant, users can't change it |
| Recommended | HKLM or HKCU `SOFTWARE\Policies\<Vendor>\<Product>\Recommended\<Section>\<Key>` | a default users can change |

- Values are mapped by the property type (DWORD → bool / integers, SZ → string, enum by name, lists, dictionaries);
  an invalid value is reported to the listener and skipped, a load never fails because of a policy.
- Policy changes are picked up while the application runs.
- `AdmxGenerator` writes an administrative template (ADMX + ADML per language) for gpedit, GPMC and Intune;
  also available as the dotnet tool **Dapplo.Ini.WindowsPolicies.Tool** (`dapplo-ini-admx`).
- `PolicyRegFile` exports section values as a `.reg` file.

Targets net48, net10.0-windows and net10.0 (template and `.reg` generation on any OS).
See the wiki page [Windows Policies](https://github.com/dapplo/Dapplo.Ini/wiki/Windows-Policies).
