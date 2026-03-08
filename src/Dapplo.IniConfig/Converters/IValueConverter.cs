// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace Dapplo.IniConfig.Converters;

/// <summary>
/// Converts between a .NET value and its string representation in an INI file.
/// </summary>
public interface IValueConverter
{
    /// <summary>Gets the type this converter handles.</summary>
    Type TargetType { get; }

    /// <summary>Converts the string from the INI file to the target type.</summary>
    object? ConvertFromString(string? raw);

    /// <summary>Converts the typed value to the string representation stored in the INI file.</summary>
    string? ConvertToString(object? value);
}

/// <summary>
/// Strongly-typed variant of <see cref="IValueConverter"/>.
/// </summary>
/// <typeparam name="T">The .NET type this converter handles.</typeparam>
public interface IValueConverter<T> : IValueConverter
{
    /// <summary>Converts the string from the INI file to <typeparamref name="T"/>.</summary>
    T? ConvertFromString(string? raw, T? defaultValue = default);

    /// <summary>Converts <typeparamref name="T"/> to its INI string representation.</summary>
    string? ConvertToString(T? value);
}
