// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace Dapplo.Ini.Config.Converters;

/// <summary>
/// Provides a registry of <see cref="IValueConverter"/> instances and looks one up for any given <see cref="Type"/>.
/// </summary>
public static class ValueConverterRegistry
{
    private static readonly Dictionary<Type, IValueConverter> _converters = new();

    static ValueConverterRegistry()
    {
        // Register all built-in converters
        Register(new StringConverter());
        Register(new BoolConverter());
        Register(new Int32Converter());
        Register(new Int64Converter());
        Register(new UInt32Converter());
        Register(new UInt64Converter());
        Register(new DoubleConverter());
        Register(new FloatConverter());
        Register(new DecimalConverter());
        Register(new DateTimeConverter());
        Register(new TimeSpanConverter());
        Register(new GuidConverter());
        Register(new UriConverter());
    }

    /// <summary>Registers (or replaces) a converter for its <see cref="IValueConverter.TargetType"/>.</summary>
    public static void Register(IValueConverter converter)
    {
        if (converter is null) throw new ArgumentNullException(nameof(converter));
        _converters[converter.TargetType] = converter;
    }

    /// <summary>
    /// Returns the converter for <paramref name="type"/>, supporting nullable value types and enums.
    /// Returns <c>null</c> when no converter is found.
    /// </summary>
    public static IValueConverter? GetConverter(Type type)
    {
        if (type is null) throw new ArgumentNullException(nameof(type));

        if (_converters.TryGetValue(type, out var converter))
            return converter;

        // Nullable<T> → unwrap and find converter for T
        var underlying = Nullable.GetUnderlyingType(type);
        if (underlying != null)
        {
            if (_converters.TryGetValue(underlying, out converter))
                return converter;

            // Try enum under nullable
            if (underlying.IsEnum)
                return GetOrCreateEnumConverter(underlying);
        }

        // Plain enum
        if (type.IsEnum)
            return GetOrCreateEnumConverter(type);

        return null;
    }

    private static IValueConverter GetOrCreateEnumConverter(Type enumType)
    {
        if (!_converters.TryGetValue(enumType, out var conv))
        {
            conv = new EnumConverter(enumType);
            _converters[enumType] = conv;
        }
        return conv;
    }
}
