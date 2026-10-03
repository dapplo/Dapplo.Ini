// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Generic;
using System.Globalization;
using System.Text;
#if NET
using System.Diagnostics.CodeAnalysis;
#endif

namespace Dapplo.Ini.Converters;

/// <summary>Base class that simplifies building typed converters.</summary>
/// <typeparam name="T">The .NET type this converter handles.</typeparam>
public abstract class ValueConverterBase<T> : IValueConverter<T>
{
    /// <inheritdoc/>
    public Type TargetType => typeof(T);

    /// <inheritdoc/>
    public abstract T? ConvertFromString(string? raw, T? defaultValue = default);

    /// <inheritdoc/>
    public virtual string? ConvertToString(T? value) => value?.ToString();

    // Non-generic IValueConverter interface — delegates to a virtual helper so subclasses
    // can broaden the accepted type (e.g. ListConverter<T> accepts IEnumerable<T>).
    object? IValueConverter.ConvertFromString(string? raw) => ConvertFromString(raw);
    string? IValueConverter.ConvertToString(object? value) => ConvertToStringFromObject(value);

    /// <summary>
    /// Converts an untyped value to its INI string representation.
    /// Override in a subclass to accept types broader than <typeparamref name="T"/>
    /// (for example, accepting <c>IEnumerable&lt;T&gt;</c> for a list converter).
    /// The default implementation casts to <typeparamref name="T"/> and falls back to <c>null</c>.
    /// </summary>
    protected virtual string? ConvertToStringFromObject(object? value)
        => ConvertToString(value is T typed ? typed : default);
}

// ─── Built-in converters ────────────────────────────────────────────────────

/// <summary>Passes strings through unchanged.</summary>
public sealed class StringConverter : ValueConverterBase<string>
{
    public override string? ConvertFromString(string? raw, string? defaultValue = default)
        => raw ?? defaultValue;
}

/// <summary>Converts <see cref="bool"/> using "True"/"False".</summary>
public sealed class BoolConverter : ValueConverterBase<bool>
{
    public override bool ConvertFromString(string? raw, bool defaultValue = default)
    {
        if (string.IsNullOrWhiteSpace(raw)) return defaultValue;
        var value = raw!.Trim();
        if (bool.TryParse(value, out var result)) return result;
        // Common hand-written alternatives (compared without allocating a lower-case copy)
        if (value == "1" || IsWord(value, "yes") || IsWord(value, "on")) return true;
        if (value == "0" || IsWord(value, "no") || IsWord(value, "off")) return false;
        return bool.Parse(value); // throws a FormatException with the usual message
    }

    private static bool IsWord(string value, string word) => string.Equals(value, word, StringComparison.OrdinalIgnoreCase);
}

/// <summary>Converts <see cref="short"/>.</summary>
public sealed class Int16Converter : ValueConverterBase<short>
{
    public override short ConvertFromString(string? raw, short defaultValue = default)
    {
        if (string.IsNullOrWhiteSpace(raw)) return defaultValue;
        return short.Parse(raw!.Trim(), CultureInfo.InvariantCulture);
    }

    public override string? ConvertToString(short value) => value.ToString(CultureInfo.InvariantCulture);
}

/// <summary>Converts <see cref="ushort"/>.</summary>
public sealed class UInt16Converter : ValueConverterBase<ushort>
{
    public override ushort ConvertFromString(string? raw, ushort defaultValue = default)
    {
        if (string.IsNullOrWhiteSpace(raw)) return defaultValue;
        return ushort.Parse(raw!.Trim(), CultureInfo.InvariantCulture);
    }

    public override string? ConvertToString(ushort value) => value.ToString(CultureInfo.InvariantCulture);
}

/// <summary>Converts <see cref="sbyte"/>.</summary>
public sealed class SByteConverter : ValueConverterBase<sbyte>
{
    public override sbyte ConvertFromString(string? raw, sbyte defaultValue = default)
    {
        if (string.IsNullOrWhiteSpace(raw)) return defaultValue;
        return sbyte.Parse(raw!.Trim(), CultureInfo.InvariantCulture);
    }

    public override string? ConvertToString(sbyte value) => value.ToString(CultureInfo.InvariantCulture);
}

/// <summary>Converts <see cref="char"/> (a single character; not trimmed, so a space is a valid value).</summary>
public sealed class CharConverter : ValueConverterBase<char>
{
    public override char ConvertFromString(string? raw, char defaultValue = default)
    {
        if (string.IsNullOrEmpty(raw)) return defaultValue;
        if (raw!.Length == 1) return raw[0];
        var trimmed = raw.Trim();
        if (trimmed.Length == 1) return trimmed[0];
        throw new FormatException($"'{raw}' is not a single character.");
    }

    public override string? ConvertToString(char value) => value.ToString();
}

/// <summary>
/// Wraps the converter of a value type <c>T</c> for <c>T?</c>: an empty or missing value is <c>null</c>
/// (instead of <c>default(T)</c>), and <c>null</c> is written as an empty value.
/// </summary>
public sealed class NullableConverter : IValueConverter
{
    private readonly IValueConverter _inner;

    /// <param name="nullableType">The <c>Nullable&lt;T&gt;</c> type.</param>
    /// <param name="inner">The converter for <c>T</c>.</param>
    public NullableConverter(Type nullableType, IValueConverter inner)
    {
        TargetType = nullableType ?? throw new ArgumentNullException(nameof(nullableType));
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    }

    /// <inheritdoc/>
    public Type TargetType { get; }

    /// <inheritdoc/>
    public object? ConvertFromString(string? raw)
        => string.IsNullOrWhiteSpace(raw) ? null : _inner.ConvertFromString(raw);

    /// <inheritdoc/>
    public string? ConvertToString(object? value)
        => value == null ? null : _inner.ConvertToString(value);
}

/// <summary>
/// Splits and joins delimiter-separated values. An element that contains the delimiter (or starts with a
/// quote or with whitespace) is written between double quotes, with inner quotes doubled, so every element
/// round-trips. Unquoted input is read exactly as before.
/// </summary>
internal static class DelimitedValues
{
    /// <summary>Splits <paramref name="raw"/> on <paramref name="separator"/> outside quotes; parts are trimmed, quotes kept.</summary>
    /// <param name="raw">The raw value.</param>
    /// <param name="separator">The element separator.</param>
    /// <param name="innerSeparator">
    /// For dictionaries: the key/value separator. A quote may also open right after it, so a quoted value
    /// that contains the element separator (<c>"k"="a,b"</c>) stays one part.
    /// </param>
    public static List<string> SplitRaw(string raw, char separator, char? innerSeparator = null)
    {
        var parts = new List<string>();
        var start = 0;
        var inQuotes = false;
        var contentSeen = false; // non-whitespace seen in the current part
        for (var i = 0; i < raw.Length; i++)
        {
            var c = raw[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < raw.Length && raw[i + 1] == '"') i++; // doubled quote
                    else inQuotes = false;
                }
                continue;
            }
            if (c == separator)
            {
                parts.Add(raw.Substring(start, i - start).Trim());
                start = i + 1;
                contentSeen = false;
                continue;
            }
            if (c == '"' && !contentSeen)
                inQuotes = true; // a quote only opens at the start of a part (or of a value)
            if (innerSeparator.HasValue && c == innerSeparator.Value)
                contentSeen = false;
            else if (!char.IsWhiteSpace(c))
                contentSeen = true;
        }
        parts.Add(raw.Substring(start).Trim());
        return parts;
    }

    /// <summary>Finds the first <paramref name="separator"/> outside quotes, or -1.</summary>
    public static int IndexOfUnquoted(string token, char separator)
    {
        var inQuotes = false;
        var contentSeen = false;
        for (var i = 0; i < token.Length; i++)
        {
            var c = token[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < token.Length && token[i + 1] == '"') i++;
                    else inQuotes = false;
                }
                continue;
            }
            if (c == separator) return i;
            if (c == '"' && !contentSeen) inQuotes = true;
            if (!char.IsWhiteSpace(c)) contentSeen = true;
        }
        return -1;
    }

    /// <summary>Removes surrounding quotes written by <see cref="Quote"/>.</summary>
    public static string Unquote(string token)
    {
        token = token.Trim();
        if (token.Length >= 2 && token[0] == '"' && token[token.Length - 1] == '"')
            return token.Substring(1, token.Length - 2).Replace("\"\"", "\"");
        return token;
    }

    /// <summary>Quotes <paramref name="value"/> when it could not be read back unquoted.</summary>
    public static string Quote(string value, char separator, char? otherSeparator = null)
    {
        var needsQuotes = value.IndexOf(separator) >= 0
            || (otherSeparator.HasValue && value.IndexOf(otherSeparator.Value) >= 0)
            || (value.Length > 0 && (value[0] == '"' || char.IsWhiteSpace(value[0]) || char.IsWhiteSpace(value[value.Length - 1])));
        return needsQuotes ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
    }
}

/// <summary>Converts <see cref="byte"/>.</summary>
public sealed class ByteConverter : ValueConverterBase<byte>
{
    public override byte ConvertFromString(string? raw, byte defaultValue = default)
    {
        if (string.IsNullOrWhiteSpace(raw)) return defaultValue;
        return byte.Parse(raw!.Trim(), CultureInfo.InvariantCulture);
    }

    public override string? ConvertToString(byte value)
        => value.ToString(CultureInfo.InvariantCulture);
}

/// <summary>Converts <see cref="int"/>.</summary>
public sealed class Int32Converter : ValueConverterBase<int>
{
    public override int ConvertFromString(string? raw, int defaultValue = default)
    {
        if (string.IsNullOrWhiteSpace(raw)) return defaultValue;
        return int.Parse(raw!.Trim(), CultureInfo.InvariantCulture);
    }

    public override string? ConvertToString(int value)
        => value.ToString(CultureInfo.InvariantCulture);
}

/// <summary>Converts <see cref="long"/>.</summary>
public sealed class Int64Converter : ValueConverterBase<long>
{
    public override long ConvertFromString(string? raw, long defaultValue = default)
    {
        if (string.IsNullOrWhiteSpace(raw)) return defaultValue;
        return long.Parse(raw!.Trim(), CultureInfo.InvariantCulture);
    }

    public override string? ConvertToString(long value)
        => value.ToString(CultureInfo.InvariantCulture);
}

/// <summary>Converts <see cref="uint"/>.</summary>
public sealed class UInt32Converter : ValueConverterBase<uint>
{
    public override uint ConvertFromString(string? raw, uint defaultValue = default)
    {
        if (string.IsNullOrWhiteSpace(raw)) return defaultValue;
        return uint.Parse(raw!.Trim(), CultureInfo.InvariantCulture);
    }

    public override string? ConvertToString(uint value)
        => value.ToString(CultureInfo.InvariantCulture);
}

/// <summary>Converts <see cref="ulong"/>.</summary>
public sealed class UInt64Converter : ValueConverterBase<ulong>
{
    public override ulong ConvertFromString(string? raw, ulong defaultValue = default)
    {
        if (string.IsNullOrWhiteSpace(raw)) return defaultValue;
        return ulong.Parse(raw!.Trim(), CultureInfo.InvariantCulture);
    }

    public override string? ConvertToString(ulong value)
        => value.ToString(CultureInfo.InvariantCulture);
}

/// <summary>Converts <see cref="double"/> using invariant culture.</summary>
public sealed class DoubleConverter : ValueConverterBase<double>
{
    public override double ConvertFromString(string? raw, double defaultValue = default)
    {
        if (string.IsNullOrWhiteSpace(raw)) return defaultValue;
        return double.Parse(raw!.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture);
    }

    public override string? ConvertToString(double value)
        => value.ToString("R", CultureInfo.InvariantCulture);
}

/// <summary>Converts <see cref="float"/> using invariant culture.</summary>
public sealed class FloatConverter : ValueConverterBase<float>
{
    public override float ConvertFromString(string? raw, float defaultValue = default)
    {
        if (string.IsNullOrWhiteSpace(raw)) return defaultValue;
        return float.Parse(raw!.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture);
    }

    public override string? ConvertToString(float value)
        => value.ToString("R", CultureInfo.InvariantCulture);
}

/// <summary>Converts <see cref="decimal"/> using invariant culture.</summary>
public sealed class DecimalConverter : ValueConverterBase<decimal>
{
    public override decimal ConvertFromString(string? raw, decimal defaultValue = default)
    {
        if (string.IsNullOrWhiteSpace(raw)) return defaultValue;
        return decimal.Parse(raw!.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture);
    }

    public override string? ConvertToString(decimal value)
        => value.ToString(CultureInfo.InvariantCulture);
}

/// <summary>Converts <see cref="DateTime"/> using ISO-8601 round-trip format.</summary>
public sealed class DateTimeConverter : ValueConverterBase<DateTime>
{
    private const string Format = "O"; // round-trip ISO 8601

    public override DateTime ConvertFromString(string? raw, DateTime defaultValue = default)
    {
        if (string.IsNullOrWhiteSpace(raw)) return defaultValue;
        return DateTime.Parse(raw!.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
    }

    public override string? ConvertToString(DateTime value)
        => value.ToString(Format, CultureInfo.InvariantCulture);
}

/// <summary>Converts <see cref="DateTimeOffset"/> using ISO-8601 round-trip format.</summary>
public sealed class DateTimeOffsetConverter : ValueConverterBase<DateTimeOffset>
{
    private const string Format = "O"; // round-trip ISO 8601

    public override DateTimeOffset ConvertFromString(string? raw, DateTimeOffset defaultValue = default)
    {
        if (string.IsNullOrWhiteSpace(raw)) return defaultValue;
        return DateTimeOffset.Parse(raw!.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
    }

    public override string? ConvertToString(DateTimeOffset value)
        => value.ToString(Format, CultureInfo.InvariantCulture);
}

/// <summary>Converts <see cref="TimeSpan"/> using the constant ("c") format.</summary>
public sealed class TimeSpanConverter : ValueConverterBase<TimeSpan>
{
    public override TimeSpan ConvertFromString(string? raw, TimeSpan defaultValue = default)
    {
        if (string.IsNullOrWhiteSpace(raw)) return defaultValue;
        return TimeSpan.Parse(raw!.Trim(), CultureInfo.InvariantCulture);
    }

    public override string? ConvertToString(TimeSpan value)
        => value.ToString("c", CultureInfo.InvariantCulture);
}

/// <summary>Converts <see cref="Guid"/>.</summary>
public sealed class GuidConverter : ValueConverterBase<Guid>
{
    public override Guid ConvertFromString(string? raw, Guid defaultValue = default)
    {
        if (string.IsNullOrWhiteSpace(raw)) return defaultValue;
        return Guid.Parse(raw!.Trim());
    }
}

/// <summary>Converts <see cref="Uri"/>.</summary>
public sealed class UriConverter : ValueConverterBase<Uri>
{
    public override Uri? ConvertFromString(string? raw, Uri? defaultValue = default)
    {
        if (string.IsNullOrWhiteSpace(raw)) return defaultValue;
        return new Uri(raw!.Trim(), UriKind.RelativeOrAbsolute);
    }

    // OriginalString keeps the value exactly as given; ToString() would unescape it.
    public override string? ConvertToString(Uri? value)
        => value?.OriginalString;
}

/// <summary>
/// Converts <see cref="List{T}"/> to/from a delimiter-separated string (default separator: <c>,</c>).
/// This converter is also returned by <see cref="ValueConverterRegistry"/> for <c>IList&lt;T&gt;</c>,
/// <c>ICollection&lt;T&gt;</c>, <c>IEnumerable&lt;T&gt;</c>, <c>IReadOnlyList&lt;T&gt;</c>, and
/// <c>IReadOnlyCollection&lt;T&gt;</c> — all of which are satisfied by the <see cref="List{T}"/>
/// instance returned from <see cref="ConvertFromString(string?, List{T}?)"/>.
/// </summary>
/// <typeparam name="T">The element type. Must have a registered <see cref="IValueConverter"/>.</typeparam>
public sealed class ListConverter<T> : ValueConverterBase<List<T>>
{
    private readonly IValueConverter _elementConverter;
    private readonly char _separator;
    private readonly string _separatorStr;

    /// <param name="elementConverter">Converter for the individual list elements.</param>
    /// <param name="separator">Delimiter used between elements. Defaults to <c>,</c>.</param>
    public ListConverter(IValueConverter elementConverter, char separator = ',')
    {
        _elementConverter = elementConverter ?? throw new ArgumentNullException(nameof(elementConverter));
        _separator = separator;
        _separatorStr = separator.ToString();
    }

    /// <inheritdoc/>
    public override List<T>? ConvertFromString(string? raw, List<T>? defaultValue = default)
    {
        if (raw == null) return defaultValue;
        if (raw.Trim().Length == 0) return new List<T>();

        // Quote-aware split; an empty element is passed to the element converter as "" on every
        // target framework (so List<string> "a,,b" gives "a", "", "b").
        var parts = DelimitedValues.SplitRaw(raw, _separator);
        var result = new List<T>(parts.Count);
        foreach (var part in parts)
        {
            var item = _elementConverter.ConvertFromString(DelimitedValues.Unquote(part));
            result.Add(item is T typed ? typed : default!);
        }
        return result;
    }

    /// <inheritdoc/>
    public override string? ConvertToString(List<T>? value)
    {
        if (value == null) return null;
        var count = value.Count;
        var parts = new string[count];
        for (int i = 0; i < count; i++)
            parts[i] = DelimitedValues.Quote(_elementConverter.ConvertToString(value[i]) ?? string.Empty, _separator);
        return string.Join(_separatorStr, parts);
    }

    /// <inheritdoc/>
    protected override string? ConvertToStringFromObject(object? value)
    {
        if (value is List<T> list) return ConvertToString(list);
        if (value is IEnumerable<T> enumerable) return ConvertToString(new List<T>(enumerable));
        return null;
    }
}

/// <summary>
/// Converts <c>T[]</c> to/from a delimiter-separated string (default separator: <c>,</c>).
/// Backed by <see cref="ListConverter{T}"/>: parses into a <see cref="List{T}"/> and converts to an array.
/// </summary>
/// <typeparam name="T">The element type. Must have a registered <see cref="IValueConverter"/>.</typeparam>
public sealed class ArrayConverter<T> : IValueConverter
{
    private readonly ListConverter<T> _listConverter;

    /// <param name="elementConverter">Converter for the individual array elements.</param>
    /// <param name="separator">Delimiter used between elements. Defaults to <c>,</c>.</param>
    public ArrayConverter(IValueConverter elementConverter, char separator = ',')
    {
        _listConverter = new ListConverter<T>(elementConverter, separator);
    }

    /// <inheritdoc/>
    public Type TargetType => typeof(T[]);

    /// <inheritdoc/>
    public object? ConvertFromString(string? raw)
        => _listConverter.ConvertFromString(raw)?.ToArray();

    /// <inheritdoc/>
    public string? ConvertToString(object? value)
    {
        if (value is T[] arr) return _listConverter.ConvertToString(new List<T>(arr));
        if (value is IEnumerable<T> enumerable) return _listConverter.ConvertToString(new List<T>(enumerable));
        return null;
    }
}

/// <summary>
/// Converts <see cref="Dictionary{TKey, TValue}"/> to/from a delimiter-separated list of
/// <c>key=value</c> pairs (pair separator: <c>,</c>, key/value separator: <c>=</c> by default).
/// This converter is also returned by <see cref="ValueConverterRegistry"/> for
/// <c>IDictionary&lt;TKey,TValue&gt;</c> and <c>IReadOnlyDictionary&lt;TKey,TValue&gt;</c>.
/// </summary>
/// <typeparam name="TKey">Key type. Must have a registered <see cref="IValueConverter"/>.</typeparam>
/// <typeparam name="TValue">Value type. Must have a registered <see cref="IValueConverter"/>.</typeparam>
public sealed class DictionaryConverter<TKey, TValue> : ValueConverterBase<Dictionary<TKey, TValue>>
    where TKey : notnull
{
    private readonly IValueConverter _keyConverter;
    private readonly IValueConverter _valueConverter;
    private readonly char _pairSeparator;
    private readonly char _keyValueSeparator;
    private readonly string _pairSeparatorStr;

    /// <param name="keyConverter">Converter for dictionary keys.</param>
    /// <param name="valueConverter">Converter for dictionary values.</param>
    /// <param name="pairSeparator">Delimiter between key=value pairs. Defaults to <c>,</c>.</param>
    /// <param name="keyValueSeparator">Delimiter between a key and its value. Defaults to <c>=</c>.</param>
    public DictionaryConverter(
        IValueConverter keyConverter,
        IValueConverter valueConverter,
        char pairSeparator = ',',
        char keyValueSeparator = '=')
    {
        _keyConverter = keyConverter ?? throw new ArgumentNullException(nameof(keyConverter));
        _valueConverter = valueConverter ?? throw new ArgumentNullException(nameof(valueConverter));
        _pairSeparator = pairSeparator;
        _keyValueSeparator = keyValueSeparator;
        _pairSeparatorStr = pairSeparator.ToString();
    }

    /// <inheritdoc/>
    public override Dictionary<TKey, TValue>? ConvertFromString(
        string? raw, Dictionary<TKey, TValue>? defaultValue = default)
    {
        if (raw == null) return defaultValue;
        if (raw.Trim().Length == 0) return new Dictionary<TKey, TValue>();

        var pairs = DelimitedValues.SplitRaw(raw, _pairSeparator, _keyValueSeparator);
        // String keys are case-insensitive, like INI keys and the sub-key dictionaries the generator creates.
        var result = typeof(TKey) == typeof(string)
            ? new Dictionary<TKey, TValue>(pairs.Count, (IEqualityComparer<TKey>)(object)StringComparer.OrdinalIgnoreCase)
            : new Dictionary<TKey, TValue>(pairs.Count);
        foreach (var kv in pairs)
        {
            var sepIdx = DelimitedValues.IndexOfUnquoted(kv, _keyValueSeparator);
            if (sepIdx < 0) continue;
            var keyStr = DelimitedValues.Unquote(kv.Substring(0, sepIdx));
            var valStr = DelimitedValues.Unquote(kv.Substring(sepIdx + 1));
            var keyObj = _keyConverter.ConvertFromString(keyStr.Length == 0 ? null : keyStr);
            var valObj = _valueConverter.ConvertFromString(valStr);
            if (keyObj is TKey typedKey)
                result[typedKey] = valObj is TValue typedVal ? typedVal : default!;
        }
        return result;
    }

    /// <inheritdoc/>
    public override string? ConvertToString(Dictionary<TKey, TValue>? value)
    {
        if (value == null) return null;
        var sb = new StringBuilder();
        bool first = true;
        foreach (var kvp in value)
        {
            if (!first) sb.Append(_pairSeparatorStr);
            first = false;
            sb.Append(DelimitedValues.Quote(_keyConverter.ConvertToString(kvp.Key) ?? string.Empty, _pairSeparator, _keyValueSeparator));
            sb.Append(_keyValueSeparator);
            sb.Append(DelimitedValues.Quote(_valueConverter.ConvertToString(kvp.Value) ?? string.Empty, _pairSeparator));
        }
        return sb.ToString();
    }

    /// <inheritdoc/>
    protected override string? ConvertToStringFromObject(object? value)
    {
        if (value is Dictionary<TKey, TValue> dict) return ConvertToString(dict);
        if (value is IEnumerable<KeyValuePair<TKey, TValue>> pairs)
        {
            var sb = new StringBuilder();
            bool first = true;
            foreach (var kvp in pairs)
            {
                if (!first) sb.Append(_pairSeparatorStr);
                first = false;
                sb.Append(DelimitedValues.Quote(_keyConverter.ConvertToString(kvp.Key) ?? string.Empty, _pairSeparator, _keyValueSeparator));
                sb.Append(_keyValueSeparator);
                sb.Append(DelimitedValues.Quote(_valueConverter.ConvertToString(kvp.Value) ?? string.Empty, _pairSeparator));
            }
            return sb.ToString();
        }
        return null;
    }
}

/// <summary>Converts any <see cref="Enum"/> type using its name.</summary>
#if NET
[RequiresDynamicCode("Uses Enum.ToObject and Enum.Parse with a runtime type argument. Register a typed converter for full AOT compatibility.")]
[RequiresUnreferencedCode("Accesses enum members by name at runtime. Register a typed converter for full trim compatibility.")]
#endif
public sealed class EnumConverter : IValueConverter
{
    private readonly Type _enumType;

    public EnumConverter(Type enumType)
    {
        if (!enumType.IsEnum) throw new ArgumentException("Type must be an enum.", nameof(enumType));
        _enumType = enumType;
    }

    public Type TargetType => _enumType;

    public object? ConvertFromString(string? raw)
    {
        // Empty means "no value": returning null lets the caller use its default value.
        if (string.IsNullOrWhiteSpace(raw)) return null;
        return Enum.Parse(_enumType, raw!.Trim(), ignoreCase: true);
    }

    public string? ConvertToString(object? value)
        => value?.ToString();
}
