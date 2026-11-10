# Windows Policies

The package **Dapplo.Ini.WindowsPolicies** lets administrators configure any Dapplo.Ini application through
Windows policies: Group Policy (gpedit.msc, GPMC), Intune or a `.reg` file. It adds

- value sources that read the policy registry keys and reload the configuration when they change,
- a `.reg` export of section values,
- a generator for an administrative template (ADMX + ADML), as an API and as the dotnet tool `dapplo-ini-admx`.

Targets: `net48`, `net10.0-windows`, and `net10.0` (there the template and `.reg` generation work on any OS; the
sources find no policies without a registry). Nothing of this is in the core package.

---

## Reading policies

```csharp
var config = IniConfigRegistry.ForFile("greenshot.ini")
    .AddSearchPath(appData)
    .RegisterSection<ICoreSettings>(new CoreSettingsImpl())
    .AddWindowsPolicies("Dapplo", "Greenshot")
    .Build();
```

### Registry layout

| Kind | Key | Layer |
| --- | --- | --- |
| Mandatory | `HKLM\|HKCU\SOFTWARE\Policies\<Vendor>\<Product>\<Section>\<Key>` | `Constant`: the key is constant (`IsConstant` is `true`, setters throw), never saved |
| Recommended | `HKLM\|HKCU\SOFTWARE\Policies\<Vendor>\<Product>\Recommended\<Section>\<Key>` | `Default`: a default the user can change, not saved |

`AddWindowsPolicies` registers four sources in this order (a later one wins inside its layer):

1. recommended HKCU, 2. recommended HKLM (both `ValueSourceLayer.Default`),
3. mandatory HKCU, 4. mandatory HKLM (both `ValueSourceLayer.Constant`).

So a computer policy wins over a user policy, as in Group Policy, and with the other layers the order of a load is:
compiled default → defaults files → recommended policies → user file → constants files → mandatory policies.
`IgnoreDefaults` / `IgnoreConstants` on a section or key are respected, so a key with `IgnoreConstants` can't be
forced by a mandatory policy.

```csharp
section.GetValueOrigin("OutputFileFormat").ToString();
// ValueSource "Windows policy (HKLM, mandatory)" (Constant)
```

### Options

```csharp
builder.AddWindowsPolicies("Dapplo", "Greenshot", new WindowsPolicyOptions
{
    IncludeCurrentUser = true,              // HKCU policies (default true)
    IncludeLocalMachine = true,             // HKLM policies (default true)
    IncludeRecommended = true,              // the Recommended sub key (default true)
    RegistryView = RegistryView.Registry64, // default: a 32-bit process reads the 64-bit view, like Group Policy writes it
    RootPath = null,                        // override SOFTWARE\Policies\<Vendor>\<Product>
    WatchForChanges = true,                 // reload on a policy change (default true)
    ChangeDebounce = TimeSpan.FromMilliseconds(250)
});
```

`WindowsPolicySource` can also be registered by hand, e.g. only the mandatory HKLM policies:
`builder.AddValueSource(new WindowsPolicySource(RegistryHive.LocalMachine, PolicyKind.Mandatory, root), ValueSourceLayer.Constant)`.

### Mapping by property type

The registry value is turned into the INI text of the property, which then goes through the normal value converters
and validation.

| Property type | Registry |
| --- | --- |
| `bool` | `REG_DWORD` 0 / 1 |
| `int`, `uint`, `short`, `byte`, … | `REG_DWORD` (`REG_QWORD` for `long` / `ulong`); a numeric `REG_SZ` is accepted |
| enum | `REG_SZ` with the name of the value (a defined number as `REG_DWORD` is accepted) |
| `string` | `REG_SZ` or `REG_EXPAND_SZ`, **never expanded**: the application gets `%TEMP%\x` as an INI file would hold it |
| list / array | a sub key named like the key with the values `1`..`n` (numeric order), or a `REG_MULTI_SZ` value |
| dictionary | a sub key named like the key: value name = dictionary key, value = dictionary value |
| anything else | `REG_SZ` with the INI text (e.g. `1.5`, `00:05:00`) |

A value of the wrong type or with an invalid value (`2` for a bool, an unknown enum name, a number out of range) is
reported through `IIniConfigListener.OnValueConversionFailed` and the key is skipped; the load never fails because
of a policy. Keys with `[NotAdministrable]` or `RuntimeOnly` are not read.

A value source of your own can do the same: implement `ISectionValueSource` to get the section (and so the property
type, `section.GetPropertyType(key)`), and throw `InvalidSourceValueException` for a value it can't use.

### Change notification

While the configuration listens, the policy root (with its sub tree) and its nearest existing parent are watched
with `RegNotifyChangeKeyValue`, on one background thread per hive and root. A burst of changes (a Group Policy refresh
writes many values) is debounced into one `ValueChanged` event, after which the configuration reloads (debounced once
more by `ValueSourceChangeDebounce`), raising `PropertyChanged` for the values that changed. A policy that is removed
gives the key back to the user (or default). Disposing the `IniConfig` stops the watching.

---

## Attributes

| Attribute | Effect |
| --- | --- |
| `[NotAdministrable]` (property or section interface) | No policy is read, not in the template or `.reg` export |
| `[Secret]` | Not in the template or `.reg` export (policies are readable by every user); a policy is still read |
| `[PolicyDisplayName("…")]` (property, section interface or enum value) | The name in the policy editor; default: the key split at camel case (`OutputFileFormat` → `Output file format`) |
| `[PolicyCategory("…")]` (property or section interface) | Puts the policy into this category below the product instead of the section category |
| `[PolicyExpandable]` | A string written as `REG_EXPAND_SZ` (`expandable="true"`), e.g. for paths |

`[Description]` / `[IniValue(Description = …)]` becomes the explain text, `[Range]` the minimum / maximum of a number.

---

## Administrative template (ADMX / ADML)

```csharp
var template = AdmxGenerator.Generate(config, new AdmxOptions("Dapplo", "Greenshot")
{
    SupportedOn = "Greenshot 2.0 or later",
    Revision = "1.0",                          // raise with every change
    Languages = { "de-DE" },                   // en-US is in the list by default
    Localize = (language, id, text) => Translate(language, id, text),   // null keeps the English text
    // Prefix = "greenshot", Namespace = "Dapplo.Policies.Greenshot", IncludeRecommended = true,
    // Include = info => …, CategorySelector = info => …
});
template.WriteTo(@"artifacts\policies");   // greenshot.admx, en-US\greenshot.adml, de-DE\greenshot.adml
```

| Property type | ADMX |
| --- | --- |
| `bool` | a policy with `enabledValue` 1 / `disabledValue` 0 |
| integers | `decimal` (`longDecimal` for 64-bit) with `minValue` / `maxValue` from `[Range]` or the type; a range below 0 uses `text` |
| enum | `enum` with one item per name (`REG_SZ`) |
| `string` | `text` (`expandable` with `[PolicyExpandable]`) |
| list | `list` (values `1`..`n`) |
| dictionary | `list` with `explicitValue` (name and value) |
| others | `text` with the INI text |

- Categories: `<Vendor>` → `<Product>` → one per section, and a second tree `<Product> - Default Settings (users can
  override)` with the recommended policies. `[PolicyCategory]` or `CategorySelector` replaces the section category.
- Every policy is `class="Both"` (Computer and User Configuration), its explain text is the description, the
  default value and the registry location.
- Left out: runtime-only and `[NotAdministrable]` keys, `[Secret]` keys, mandatory policies of keys that ignore
  constants, recommended policies of keys that ignore defaults, and what `Include` rejects.
- `supportedOn` is defined in the ADMX itself and there are no references to `windows.admx`, so the template
  imports on its own into the central store, gpedit and Intune ("Import ADMX").

### dotnet tool

```
dotnet tool install -g Dapplo.Ini.WindowsPolicies.Tool
dapplo-ini-admx --assembly bin\Release\net10.0-windows\Greenshot.dll --vendor Dapplo --product Greenshot ^
                --output artifacts\policies --supported-on "Greenshot 2.0 or later" --language en-US --language de-DE
```

The tool loads the assembly (with its dependencies) and creates every generated section with its defaults. Point it
to a .NET (not .NET Framework) build. Options: `--prefix`, `--namespace`, `--revision`, `--root-path`, `--section`
(repeatable), `--no-recommended`, `--reg <file>` (also a `.reg` with the compiled defaults).

In a build, an `Exec` target after the build does the same:

```xml
<Target Name="GeneratePolicyTemplates" AfterTargets="Build" Condition="'$(Configuration)' == 'Release'">
  <Exec Command="dapplo-ini-admx --assembly &quot;$(TargetPath)&quot; --vendor Dapplo --product Greenshot --output &quot;$(OutDir)policies&quot;" />
</Target>
```

---

## `.reg` export

```csharp
// The current values of all sections as mandatory HKLM policies
PolicyRegFile.Write("greenshot-policies.reg", config.GetSections(), "Dapplo", "Greenshot");

// Only what the user changed, as recommended HKCU policies
PolicyRegFile.Write("recommended.reg", config.GetSections(), "Dapplo", "Greenshot", new PolicyRegFileOptions
{
    Kind = PolicyKind.Recommended,
    Hive = RegistryHive.CurrentUser,
    OnlyUserValues = true
});
```

The file is UTF-16 like regedit writes it. Lists and dictionaries become sub keys, each preceded by a delete
(`[-…]`) so no stale entries remain. Secrets are left out unless `IncludeSecrets` is set.

---

## See also

- [[External-Value-Sources]]
- [[Loading-Life-Cycle]]
- [[Runtime-Only-and-Constants]]
- [[User-Values]]
