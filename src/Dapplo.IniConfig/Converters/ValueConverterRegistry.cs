// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
#if NET
using System.Diagnostics.CodeAnalysis;
#endif

namespace Dapplo.IniConfig.Converters;

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
    /// <remarks>
    /// Enum support requires dynamic code and unreferenced-code access. For full trim/AOT
    /// compatibility register a typed <see cref="ValueConverterBase{T}"/> for each enum type
    /// instead of relying on the automatic <see cref="EnumConverter"/>.
    /// </remarks>
#if NET
    [RequiresDynamicCode("Auto-registering an EnumConverter for unknown enum types requires dynamic code. Register a typed converter for full AOT compatibility.")]
    [RequiresUnreferencedCode("Auto-registering an EnumConverter for unknown enum types accesses type metadata at runtime. Register a typed converter for full trim compatibility.")]
#endif
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

#if NET
    [RequiresDynamicCode("Creates an EnumConverter with a runtime type argument.")]
    [RequiresUnreferencedCode("Creates an EnumConverter that accesses enum members by name at runtime.")]
#endif
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
