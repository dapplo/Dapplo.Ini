# Value Converters

The framework converts between raw INI strings and strongly-typed .NET values using
pluggable `IValueConverter<T>` implementations.

---

## Built-in converters

| .NET type | Converter class |
|-----------|----------------|
| `string` | `StringConverter` |
| `bool` | `BoolConverter` (writes `True`/`False`; also reads `1`/`0`, `yes`/`no`, `on`/`off`) |
| `byte` | `ByteConverter` |
| `sbyte` | `SByteConverter` |
| `short` | `Int16Converter` |
| `ushort` | `UInt16Converter` |
| `int` | `Int32Converter` |
| `long` | `Int64Converter` |
| `uint` | `UInt32Converter` |
| `ulong` | `UInt64Converter` |
| `char` | `CharConverter` (a single character; not trimmed, so a space is a valid value) |
| `double` | `DoubleConverter` (invariant culture, no thousands separators) |
| `float` | `FloatConverter` (invariant culture, no thousands separators) |
| `decimal` | `DecimalConverter` (invariant culture, no thousands separators) |
| `DateTime` | `DateTimeConverter` (ISO 8601 round-trip) |
| `DateTimeOffset` | `DateTimeOffsetConverter` (ISO 8601 round-trip) |
| `TimeSpan` | `TimeSpanConverter` (constant "c" format) |
| `Guid` | `GuidConverter` |
| `Uri` | `UriConverter` (absolute and relative URIs; written as `OriginalString`) |
| Any `enum` | `EnumConverter` (auto-registered on first use) |
| `Nullable<T>` | `NullableConverter` wraps the inner converter: an empty or missing value is `null`, `null` is written as an empty value |

> **Note:** `double`, `float` and `decimal` no longer accept thousands separators — before,
> `1,5` was silently read as `15`; now it is a conversion error (reported via
> `OnValueConversionFailed`, see [[Listeners]]).

---

## Lists, arrays and dictionaries

`List<T>` (and its interfaces), `T[]` and `Dictionary<TKey,TValue>` are written as one
delimiter-separated value.  An element that contains the separator, or starts with a quote
or starts/ends with whitespace, is written in **double quotes**, with inner quotes doubled,
so every element round-trips:

```ini
Items = plain,"has,comma","""quoted"""," padded "
```

Unquoted input is read exactly as before.  An empty element (`a,,b`) is passed to the
element converter as `""` on every target framework, so `List<string>` gets `"a", "", "b"`.  Dictionaries read from the inline
`key=value,…` form use a case-insensitive comparer when the key type is `string`.

---

## Per-property list delimiter

For list-like property types (`List<T>`, `IList<T>`, `ICollection<T>`, `IEnumerable<T>`, `IReadOnlyList<T>`, `IReadOnlyCollection<T>`, `T[]`), you can override the default comma delimiter per property:

```csharp
[IniValue(DefaultValue = "one|two|three", ListDelimiter = '|')]
List<string>? Items { get; set; }
```

---

## Adding a custom converter

```csharp
// 1. Implement IValueConverter<T>
public sealed class VersionConverter : ValueConverterBase<Version>
{
    public override Version? ConvertFromString(string? raw, Version? defaultValue = default)
        => raw is null ? defaultValue : Version.Parse(raw.Trim());
}

// 2. Register before calling Build()
ValueConverterRegistry.Register(new VersionConverter());
// Registering (or replacing) a converter also updates the List<T>, T[] and
// Dictionary converters that were created for that type earlier.

// 3. Use the type in your section interface
[IniSection]
public interface IAppInfo : IIniSection
{
    [IniValue(DefaultValue = "1.0.0.0")]
    Version? AppVersion { get; set; }
}
```

---

## Encrypting sensitive values

### Option A — Encrypt/decrypt in a custom converter (recommended)

The converter is responsible for the raw string stored on disk. Everything else in the
framework (defaults, reload, transactional) continues to work normally.

```csharp
/// <summary>
/// Stores a string value AES-encrypted (Base64) in the INI file.
/// Replace the key derivation with your own secure mechanism (e.g. DPAPI, Azure KeyVault).
/// </summary>
public sealed class EncryptedStringConverter : IValueConverter
{
    // ⚠️  Hard-coded key for illustration only — use a proper key-management solution!
    private static readonly byte[] Key = Convert.FromBase64String("your-32-byte-key-base64==");
    private static readonly byte[] IV  = Convert.FromBase64String("your-16-byte-iv-base64=");

    public Type TargetType => typeof(string);

    public object? ConvertFromString(string? raw)
    {
        if (raw is null) return null;
        using var aes = Aes.Create();
        aes.Key = Key; aes.IV = IV;
        var cipher = Convert.FromBase64String(raw);
        using var decryptor = aes.CreateDecryptor();
        var plain = decryptor.TransformFinalBlock(cipher, 0, cipher.Length);
        return System.Text.Encoding.UTF8.GetString(plain);
    }

    public string? ConvertToString(object? value)
    {
        if (value is not string s) return null;
        using var aes = Aes.Create();
        aes.Key = Key; aes.IV = IV;
        var plain = System.Text.Encoding.UTF8.GetBytes(s);
        using var encryptor = aes.CreateEncryptor();
        var cipher = encryptor.TransformFinalBlock(plain, 0, plain.Length);
        return Convert.ToBase64String(cipher);
    }
}
```

```csharp
// Register before Build()
ValueConverterRegistry.Register(new EncryptedStringConverter());

[IniSection("Credentials")]
public interface ICredentials : IIniSection
{
    [IniValue(DefaultValue = "")]
    string? ApiKey { get; set; }   // stored as encrypted Base64 in the file
}
```

### Option B — Decrypt in IAfterLoad, re-encrypt in IBeforeSave

This approach stores the encrypted value in the INI file and keeps the plaintext only
in memory.  It is useful when the property type must remain `string` without a custom converter.

```csharp
[IniSection("Credentials")]
public interface ICredentials
    : IIniSection,
      IAfterLoad<ICredentials>,
      IBeforeSave<ICredentials>
{
    // Raw (encrypted) value as stored in the file — treated as opaque by the framework
    [IniValue(DefaultValue = "")]
    string? ApiKeyEncrypted { get; set; }

    // Plaintext — marked ReadOnly so it is never written back to the file
    [IniValue(ReadOnly = true)]
    string? ApiKeyPlain { get; set; }

    static new void OnAfterLoad(ICredentials self)
    {
        // Decrypt once after loading
        self.ApiKeyPlain = Decrypt(self.ApiKeyEncrypted);
    }

    static new bool OnBeforeSave(ICredentials self)
    {
        // Re-encrypt before writing; keep plaintext in memory only
        self.ApiKeyEncrypted = Encrypt(self.ApiKeyPlain);
        return true;
    }

    private static string? Decrypt(string? cipher) => /* … your crypto … */ cipher;
    private static string? Encrypt(string? plain)  => /* … your crypto … */ plain;
}
```

---

## See also

- [[Defining-Sections]] — `[IniValue]` attribute reference
- [[Lifecycle-Hooks]] — `IAfterLoad`, `IBeforeSave` hooks used in Option B
