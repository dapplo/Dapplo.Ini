// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System;

namespace Dapplo.Ini.Interfaces;

/// <summary>
/// Represents an external source of configuration values (e.g. Windows Registry,
/// environment variables, a web-service endpoint).
/// </summary>
/// <remarks>
/// Implementations are registered with <see cref="Configuration.IniConfigBuilder.AddValueSource"/>
/// and are consulted at load/reload time.  Sources are applied in registration order and each source
/// can override the values produced by earlier sources (including defaults and the user INI file).
/// A source may also raise <see cref="ValueChanged"/> when an external value changes at runtime,
/// which can trigger an automatic reload of the affected sections.
/// </remarks>
public interface IValueSource
{
    /// <summary>
    /// Attempts to retrieve the raw string value for the key <paramref name="key"/> inside
    /// INI section <paramref name="sectionName"/>.
    /// </summary>
    /// <param name="sectionName">The INI section name (e.g. <c>"General"</c>).</param>
    /// <param name="key">The key name within that section (e.g. <c>"AppName"</c>).</param>
    /// <param name="value">
    /// When this method returns <c>true</c>, contains the raw string value; otherwise <c>null</c>.
    /// </param>
    /// <returns><c>true</c> when the source provides a value for the given section/key pair.</returns>
    bool TryGetValue(string sectionName, string key, out string? value);

    /// <summary>
    /// Raised when one or more values provided by this source have changed.
    /// The library can use this signal to trigger a reload of affected sections.
    /// The <see cref="ValueChangedEventArgs"/> may identify the specific section/key that changed,
    /// or report a wildcard change (both fields <c>null</c>) to indicate that all values should
    /// be re-read.
    /// </summary>
    event EventHandler<ValueChangedEventArgs>? ValueChanged;
}

/// <summary>
/// Event arguments for <see cref="IValueSource.ValueChanged"/>.
/// </summary>
public sealed class ValueChangedEventArgs : EventArgs
{
    /// <summary>
    /// The INI section name of the changed value, or <c>null</c> when all sections may be affected.
    /// </summary>
    public string? SectionName { get; }

    /// <summary>
    /// The key of the changed value, or <c>null</c> when all keys in <see cref="SectionName"/>
    /// (or the entire source) may be affected.
    /// </summary>
    public string? Key { get; }

    /// <inheritdoc cref="ValueChangedEventArgs"/>
    public ValueChangedEventArgs(string? sectionName = null, string? key = null)
    {
        SectionName = sectionName;
        Key         = key;
    }
}
