// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Globalization;
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

    // Non-generic IValueConverter interface
    object? IValueConverter.ConvertFromString(string? raw) => ConvertFromString(raw);
    string? IValueConverter.ConvertToString(object? value) => ConvertToString(value is T typed ? typed : default);
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
