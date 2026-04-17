# Parser Options

`IniParserOptions` is a configuration class that controls how `IniFileParser`
interprets INI file content.  All options default to the most common / lenient
behaviour so that existing code continues to work unchanged.

Use the fluent `IniConfigBuilder` methods to apply options when loading via the
registry, or pass an `IniParserOptions` instance directly to `IniFileParser.Parse`
when using the low-level API.

---

## Quick-start: fluent builder

```csharp
using var config = IniConfigRegistry.ForFile("app.ini")
    .AddSearchPath(AppContext.BaseDirectory)
    .EnableEscapeSequences()          // decode \n, \t, \\, \xHH, …
    .EnableQuotedValues()             // strip surrounding "…" / '…'
    .EnableLineContinuation()         // join lines ending with \
    .AssignmentDelimiters("=:")       // accept '=' and ':'
    .CaseSensitiveKeys()              // AppName ≠ appname
    .WithDuplicateKeyHandling(DuplicateKeyHandling.FirstWins)
    .RegisterSection<IAppSettings>(new AppSettingsImpl())
    .Build();
```

Or supply a pre-built `IniParserOptions` object:

```csharp
var opts = new IniParserOptions
{
    AssignmentDelimiters = "=:",
    EscapeSequences = true,
    QuotedValues    = true,
};

using var config = IniConfigRegistry.ForFile("app.ini")
    .AddSearchPath(AppContext.BaseDirectory)
    .WithParserOptions(opts)
    .RegisterSection<IAppSettings>(new AppSettingsImpl())
    .Build();
```

---

## Options reference

### `AssignmentDelimiters`

Controls which characters are treated as key/value assignment delimiters.
Default is `=:` (both equals and colon are accepted).

```ini
[Database]
Host = server
Port: 5432
```

```csharp
var opts = new IniParserOptions { AssignmentDelimiters = "=:" };
var file = IniFileParser.Parse(content, opts);
```

Builder shorthand:

```csharp
.AssignmentDelimiters("=:")
```

---

### `DuplicateKeyHandling`

Controls what happens when the same key appears more than once inside one section.

| Value | Behaviour |
|-------|-----------|
| `LastWins` **(default)** | Each occurrence overwrites the previous one; the last value wins |
| `FirstWins` | The first occurrence is kept; later duplicates are silently ignored |
| `ThrowError` | An `InvalidOperationException` is thrown on the first duplicate |

```ini
[Database]
Host = server1
Host = server2      ; duplicate!
```

```csharp
// LastWins (default) → Host = "server2"
var file = IniFileParser.Parse(content);

// FirstWins → Host = "server1"
var file = IniFileParser.Parse(content,
    new IniParserOptions { DuplicateKeyHandling = DuplicateKeyHandling.FirstWins });

// ThrowError → throws InvalidOperationException
var file = IniFileParser.Parse(content,
    new IniParserOptions { DuplicateKeyHandling = DuplicateKeyHandling.ThrowError });
```

Builder shorthand:

```csharp
.WithDuplicateKeyHandling(DuplicateKeyHandling.FirstWins)
```

---

### `QuotedValues`

When `true`, values enclosed in matching double-quotes `"…"` or single-quotes `'…'`
have their surrounding quote characters stripped.  Interior whitespace is preserved.

| `QuotedValues` | INI line | Parsed value |
|----------------|----------|--------------|
| `false` **(default)** | `key = "hello world"` | `"hello world"` |
| `true` | `key = "hello world"` | `hello world` |
| `true` | `key = '  spaces  '` | `  spaces  ` |
| `true` | `key = plain value` | `plain value` *(unchanged — no quotes)* |

```csharp
var opts = new IniParserOptions { QuotedValues = true };
var file = IniFileParser.Parse(content, opts);
```

Builder shorthand:

```csharp
.EnableQuotedValues()
```

---

### `LineContinuation`

When `true`, a backslash (`\`) at the very end of a value line causes the parser to
append the **trimmed** content of the following line, forming a single value.
The chain continues as long as each continuation line also ends with `\`.

```ini
[Message]
Text = Hello, \
       World!
```

| `LineContinuation` | Parsed value |
|--------------------|--------------|
| `false` **(default)** | `Hello, \` |
| `true` | `Hello, World!` |

Multi-line example:

```ini
[Script]
Command = first \
          second \
          third
```

Parsed value with `LineContinuation = true`: `first second third`

```csharp
var opts = new IniParserOptions { LineContinuation = true };
var file = IniFileParser.Parse(content, opts);
```

Builder shorthand:

```csharp
.EnableLineContinuation()
```

---

### `EscapeSequences`

When `true`, standard C-style escape sequences in values are decoded.

| Sequence | Decoded character |
|----------|------------------|
| `\\` | Literal backslash `\` |
| `\n` | Newline (LF, U+000A) |
| `\r` | Carriage return (CR, U+000D) |
| `\t` | Horizontal tab (U+0009) |
| `\0` | Null character (U+0000) |
| `\"` | Double quote `"` |
| `\'` | Single quote `'` |
| `\a` | Bell / alert (U+0007) |
| `\b` | Backspace (U+0008) |
| `\xHH` | Character with hex code `HH` (two hex digits) |

Unrecognised sequences (e.g. `\q`) are left **unchanged** — the backslash is preserved.

```ini
[Paths]
DataDir = C:\\ProgramData\\MyApp
Greeting = Hello\nWorld
Tab = col1\tcol2
Bullet = \x2022 item
```

With `EscapeSequences = true`:

| Key | Value |
|-----|-------|
| `DataDir` | `C:\ProgramData\MyApp` |
| `Greeting` | `Hello` + newline + `World` |
| `Tab` | `col1` + tab + `col2` |
| `Bullet` | `• item` |

```csharp
var opts = new IniParserOptions { EscapeSequences = true };
var file = IniFileParser.Parse(content, opts);
```

Builder shorthand:

```csharp
.EnableEscapeSequences()
```

---

### `CaseSensitiveKeys`

By default (`false`) key names within a section are compared
**case-insensitively** — `AppName`, `appname`, and `APPNAME` all refer to the
same entry.

When set to `true`, key comparisons use **ordinal case-sensitive** equality, so
`AppName` and `appname` are treated as different keys.

```ini
[S]
AppName = Pascal
appname = lower
```

| `CaseSensitiveKeys` | `GetValue("AppName")` | `GetValue("appname")` | Entry count |
|---------------------|-----------------------|-----------------------|-------------|
| `false` **(default)** | `lower` *(LastWins)* | `lower` | 1 |
| `true` | `Pascal` | `lower` | 2 |

```csharp
var opts = new IniParserOptions { CaseSensitiveKeys = true };
var file = IniFileParser.Parse(content, opts);
```

Builder shorthand:

```csharp
.CaseSensitiveKeys()
```

---

### `CaseSensitiveSections`

By default (`false`) section names are compared **case-insensitively** —
`[General]`, `[GENERAL]`, and `[general]` all refer to the same section.

When set to `true`, section-name comparisons use **ordinal case-sensitive**
equality, so `[General]` and `[GENERAL]` are independent sections.

```ini
[General]
key = v1

[GENERAL]
key = v2
```

| `CaseSensitiveSections` | `GetSection("General")` | `GetSection("GENERAL")` | Section count |
|-------------------------|-------------------------|-------------------------|---------------|
| `false` **(default)** | `v2` *(LastWins)* | `v2` | 1 |
| `true` | `v1` | `v2` | 2 |

```csharp
var opts = new IniParserOptions { CaseSensitiveSections = true };
var file = IniFileParser.Parse(content, opts);
```

Builder shorthand:

```csharp
.CaseSensitiveSections()
```

---

## Combining options

All options can be combined freely.  Use the fluent builder methods to compose the
exact set you need:

```csharp
using var config = IniConfigRegistry.ForFile("app.ini")
    .AddSearchPath(AppContext.BaseDirectory)
    .EnableEscapeSequences()
    .EnableQuotedValues()
    .EnableLineContinuation()
    .CaseSensitiveKeys()
    .CaseSensitiveSections()
    .WithDuplicateKeyHandling(DuplicateKeyHandling.ThrowError)
    .RegisterSection<IAppSettings>(new AppSettingsImpl())
    .Build();
```

Or build the options object once and reuse it across multiple configs:

```csharp
var strictOpts = new IniParserOptions
{
    EscapeSequences      = true,
    QuotedValues         = true,
    CaseSensitiveKeys    = true,
    DuplicateKeyHandling = DuplicateKeyHandling.ThrowError,
};

using var configA = IniConfigRegistry.ForFile("a.ini")
    .WithParserOptions(strictOpts)
    .RegisterSection<IA>(new AImpl())
    .Build();

using var configB = IniConfigRegistry.ForFile("b.ini")
    .WithParserOptions(strictOpts)
    .RegisterSection<IB>(new BImpl())
    .Build();
```

---

## Low-level API

If you use `IniFileParser` directly (without `IniConfig` / `IniConfigBuilder`),
pass `IniParserOptions` as the second argument:

```csharp
var opts = new IniParserOptions
{
    EscapeSequences = true,
    QuotedValues    = true,
};

// From a string
var iniFile = IniFileParser.Parse(rawText, opts);

// From disk (sync)
var iniFile = IniFileParser.ParseFile("/path/to/file.ini", Encoding.UTF8, opts);

// From disk (async)
var iniFile = await IniFileParser.ParseFileAsync("/path/to/file.ini",
    Encoding.UTF8, opts, cancellationToken);
```

---

## See also

- [[Ini-File-Format]] — INI syntax overview, value formats, comments
- [[Loading-Configuration]] — `IniConfigBuilder` fluent API reference
- [[Registry-API]] — complete `IniConfigBuilder` method table
