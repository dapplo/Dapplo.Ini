// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
using System.Collections.Concurrent;
using System.Collections.Generic;
#if NET
using System.Diagnostics.CodeAnalysis;
#endif

namespace Dapplo.Ini.Converters;

/// <summary>
/// Provides a registry of <see cref="IValueConverter"/> instances and looks one up for any given <see cref="Type"/>.
/// </summary>
public static class ValueConverterRegistry
{
    private static readonly ConcurrentDictionary<Type, IValueConverter> _converters = new();

    // Converters this registry composed itself (nullable, list, array, dictionary). They capture their
    // element converters, so they are dropped whenever a converter is registered and rebuilt on demand.
    private static readonly ConcurrentDictionary<Type, byte> _composed = new();

    // Serialises Register with the bookkeeping of composed converters.
    private static readonly object _registrationLock = new();

    // Generic type definitions used for collection/dictionary detection
    private static readonly HashSet<Type> _listLikeGenericDefinitions = new()
    {
        typeof(List<>),
        typeof(IList<>),
        typeof(ICollection<>),
        typeof(IEnumerable<>),
        typeof(IReadOnlyList<>),
        typeof(IReadOnlyCollection<>),
    };

    private static readonly HashSet<Type> _dictionaryLikeGenericDefinitions = new()
    {
        typeof(Dictionary<,>),
        typeof(IDictionary<,>),
        typeof(IReadOnlyDictionary<,>),
    };

    static ValueConverterRegistry()
    {
        // Register all built-in converters
        Register(new StringConverter());
        Register(new BoolConverter());
        Register(new ByteConverter());
        Register(new Int32Converter());
        Register(new Int64Converter());
        Register(new UInt32Converter());
        Register(new UInt64Converter());
        Register(new DoubleConverter());
        Register(new FloatConverter());
        Register(new DecimalConverter());
        Register(new DateTimeConverter());
        Register(new DateTimeOffsetConverter());
        Register(new TimeSpanConverter());
        Register(new GuidConverter());
        Register(new UriConverter());
        Register(new Int16Converter());
        Register(new UInt16Converter());
        Register(new SByteConverter());
        Register(new CharConverter());
    }

    /// <summary>Registers (or replaces) a converter for its <see cref="IValueConverter.TargetType"/>.</summary>
    public static void Register(IValueConverter converter)
    {
        if (converter is null) throw new ArgumentNullException(nameof(converter));
        lock (_registrationLock)
        {
            _converters[converter.TargetType] = converter;
            _composed.TryRemove(converter.TargetType, out _);
            // Composed converters captured the previous element converters: rebuild them on the next lookup.
            foreach (var composedType in _composed.Keys)
            {
                if (_composed.TryRemove(composedType, out _))
                    _converters.TryRemove(composedType, out _);
            }
        }
    }

    /// <summary>
    /// Returns the converter for <paramref name="type"/>, supporting nullable value types, enums,
    /// collection types (<see cref="List{T}"/>, <c>IList&lt;T&gt;</c>, <c>T[]</c>, etc.),
    /// and dictionary types (<see cref="Dictionary{TKey,TValue}"/>, <c>IDictionary&lt;TKey,TValue&gt;</c>, etc.).
    /// Returns <c>null</c> when no converter is found.
    /// </summary>
    /// <remarks>
    /// Enum support requires dynamic code and unreferenced-code access. For full trim/AOT
    /// compatibility register a typed <see cref="ValueConverterBase{T}"/> for each enum type
    /// instead of relying on the automatic <see cref="EnumConverter"/>.
    /// Collection and dictionary converters are created on demand using reflection and are cached
    /// for subsequent lookups. This method is safe to call concurrently from multiple threads.
    /// </remarks>
#if NET
    [RequiresDynamicCode("Auto-registering converters for enum, collection, and dictionary types requires dynamic code. Register typed converters for full AOT compatibility.")]
    [RequiresUnreferencedCode("Auto-registering converters for enum, collection, and dictionary types accesses type metadata at runtime. Register typed converters for full trim compatibility.")]
#endif
    public static IValueConverter? GetConverter(Type type)
    {
        if (type is null) throw new ArgumentNullException(nameof(type));

        if (_converters.TryGetValue(type, out var converter))
            return converter;

        // Nullable<T> → wrap the converter for T so that an empty value is null
        var underlying = Nullable.GetUnderlyingType(type);
        if (underlying != null)
        {
            var inner = _converters.TryGetValue(underlying, out var innerConverter)
                ? innerConverter
                : underlying.IsEnum ? GetOrCreateEnumConverter(underlying) : null;
            if (inner == null)
                return null;
            return AddComposed(type, new NullableConverter(type, inner));
        }

        // Plain enum
        if (type.IsEnum)
            return GetOrCreateEnumConverter(type);

        // T[] array
        if (type.IsArray)
        {
            var elementType = type.GetElementType()!;
            var elementConverter = GetConverter(elementType);
            if (elementConverter != null)
            {
                var arrayConverterType = typeof(ArrayConverter<>).MakeGenericType(elementType);
                converter = (IValueConverter)Activator.CreateInstance(arrayConverterType, elementConverter, ',')!;
                return AddComposed(type, converter);
            }
        }

        // Generic collection and dictionary types
        if (type.IsGenericType)
        {
            var genericDef = type.GetGenericTypeDefinition();
            var typeArgs = type.GetGenericArguments();

            // List-like: List<T>, IList<T>, ICollection<T>, IEnumerable<T>, IReadOnlyList<T>, IReadOnlyCollection<T>
            if (typeArgs.Length == 1 && _listLikeGenericDefinitions.Contains(genericDef))
            {
                var elementType = typeArgs[0];
                var elementConverter = GetConverter(elementType);
                if (elementConverter != null)
                {
                    var listConverterType = typeof(ListConverter<>).MakeGenericType(elementType);
                    converter = (IValueConverter)Activator.CreateInstance(listConverterType, elementConverter, ',')!;
                    return AddComposed(type, converter);
                }
            }

            // Dictionary-like: Dictionary<TK,TV>, IDictionary<TK,TV>, IReadOnlyDictionary<TK,TV>
            if (typeArgs.Length == 2 && _dictionaryLikeGenericDefinitions.Contains(genericDef))
            {
                var keyConverter = GetConverter(typeArgs[0]);
                var valueConverter = GetConverter(typeArgs[1]);
                if (keyConverter != null && valueConverter != null)
                {
                    var dictConverterType = typeof(DictionaryConverter<,>).MakeGenericType(typeArgs[0], typeArgs[1]);
                    converter = (IValueConverter)Activator.CreateInstance(dictConverterType, keyConverter, valueConverter, ',', '=')!;
                    return AddComposed(type, converter);
                }
            }
        }

        return null;
    }

    private static IValueConverter AddComposed(Type type, IValueConverter converter)
    {
        lock (_registrationLock)
        {
            // Only the first writer wins; all callers then get the winner. Only a converter this registry
            // composed is marked as composed — never one the application registered in the meantime.
            var winner = _converters.GetOrAdd(type, converter);
            if (ReferenceEquals(winner, converter))
                _composed.TryAdd(type, 0);
            return winner;
        }
    }

#if NET
    [RequiresDynamicCode("Creates an EnumConverter with a runtime type argument.")]
    [RequiresUnreferencedCode("Creates an EnumConverter that accesses enum members by name at runtime.")]
#endif
    private static IValueConverter GetOrCreateEnumConverter(Type enumType)
    {
        return _converters.GetOrAdd(enumType, static t => new EnumConverter(t));
    }
}
