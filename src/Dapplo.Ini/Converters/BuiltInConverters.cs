// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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
        return bool.Parse(raw!.Trim());
    }
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
        return double.Parse(raw!.Trim(), CultureInfo.InvariantCulture);
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
        return float.Parse(raw!.Trim(), CultureInfo.InvariantCulture);
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
        return decimal.Parse(raw!.Trim(), CultureInfo.InvariantCulture);
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
        return new Uri(raw!.Trim());
    }

    public override string? ConvertToString(Uri? value)
        => value?.ToString();
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

    /// <param name="elementConverter">Converter for the individual list elements.</param>
    /// <param name="separator">Delimiter used between elements. Defaults to <c>,</c>.</param>
    public ListConverter(IValueConverter elementConverter, char separator = ',')
    {
        _elementConverter = elementConverter ?? throw new ArgumentNullException(nameof(elementConverter));
        _separator = separator;
    }

    /// <inheritdoc/>
    public override List<T>? ConvertFromString(string? raw, List<T>? defaultValue = default)
    {
        if (raw == null) return defaultValue;
        if (raw.Length == 0) return new List<T>();
        var result = new List<T>();
        foreach (var part in raw.Split(_separator))
        {
            var item = _elementConverter.ConvertFromString(part.Trim());
            result.Add(item is T typed ? typed : default!);
        }
        return result;
    }

    /// <inheritdoc/>
    public override string? ConvertToString(List<T>? value)
    {
        if (value == null) return null;
        return string.Join(_separator.ToString(),
            value.Select(item => _elementConverter.ConvertToString(item) ?? string.Empty));
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
    }

    /// <inheritdoc/>
    public override Dictionary<TKey, TValue>? ConvertFromString(
        string? raw, Dictionary<TKey, TValue>? defaultValue = default)
    {
        if (raw == null) return defaultValue;
        if (raw.Length == 0) return new Dictionary<TKey, TValue>();
        var result = new Dictionary<TKey, TValue>();
        foreach (var pair in raw.Split(_pairSeparator))
        {
            var kv = pair.Trim();
            var sepIdx = kv.IndexOf(_keyValueSeparator);
            if (sepIdx < 0) continue;
            var keyStr = kv.Substring(0, sepIdx).Trim();
            var valStr = kv.Substring(sepIdx + 1).Trim();
            var keyObj = _keyConverter.ConvertFromString(keyStr);
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
        return string.Join(_pairSeparator.ToString(), value.Select(kvp =>
            $"{_keyConverter.ConvertToString(kvp.Key) ?? string.Empty}{_keyValueSeparator}{_valueConverter.ConvertToString(kvp.Value) ?? string.Empty}"));
    }

    /// <inheritdoc/>
    protected override string? ConvertToStringFromObject(object? value)
    {
        if (value is Dictionary<TKey, TValue> dict) return ConvertToString(dict);
        if (value is IEnumerable<KeyValuePair<TKey, TValue>> pairs)
        {
            return string.Join(_pairSeparator.ToString(), pairs.Select(kvp =>
                $"{_keyConverter.ConvertToString(kvp.Key) ?? string.Empty}{_keyValueSeparator}{_valueConverter.ConvertToString(kvp.Value) ?? string.Empty}"));
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
        if (string.IsNullOrWhiteSpace(raw)) return Enum.ToObject(_enumType, 0);
        return Enum.Parse(_enumType, raw!.Trim(), ignoreCase: true);
    }

    public string? ConvertToString(object? value)
        => value?.ToString();
}
