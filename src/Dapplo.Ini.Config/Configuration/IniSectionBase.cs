// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Dapplo.Ini.Config.Converters;
using Dapplo.Ini.Config.Interfaces;

namespace Dapplo.Ini.Config.Configuration;

/// <summary>
/// Base class for all source-generated INI section classes.
/// Provides the raw key/value dictionary and converter-aware get/set helpers.
/// </summary>
public abstract class IniSectionBase : IIniSection
{
    // Raw backing store (key → string as it appears in the file)
    private readonly Dictionary<string, string?> _rawValues =
        new(StringComparer.OrdinalIgnoreCase);

    // ── IIniSection ───────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public abstract string SectionName { get; }

    /// <inheritdoc/>
    public string? GetRawValue(string key)
        => _rawValues.TryGetValue(key, out var v) ? v : null;

    /// <inheritdoc/>
    public void SetRawValue(string key, string? value)
    {
        _rawValues[key] = value;
        OnRawValueSet(key, value);
    }

    /// <inheritdoc/>
    public abstract void ResetToDefaults();

    // ── Internal helpers for generated code ──────────────────────────────────

    /// <summary>
    /// Called after <see cref="SetRawValue"/> so the generated class can update its
    /// typed backing field for the matching property.
    /// </summary>
    protected abstract void OnRawValueSet(string key, string? rawValue);

    /// <summary>
    /// Returns all key/value pairs held by this section, so the writer can serialise them.
    /// </summary>
    public abstract IEnumerable<KeyValuePair<string, string?>> GetAllRawValues();

    // ── Converter helpers (used by generated code) ────────────────────────────

    /// <summary>
    /// Converts a raw INI string to <typeparamref name="T"/> using the registered converter.
    /// Falls back to <paramref name="defaultValue"/> when the raw value is absent or conversion fails.
    /// </summary>
    protected static T? ConvertFromRaw<T>(string? raw, T? defaultValue = default)
    {
        var converter = ValueConverterRegistry.GetConverter(typeof(T));
        if (converter == null) return defaultValue;
        try
        {
            var result = converter.ConvertFromString(raw);
            return result is T typed ? typed : defaultValue;
        }
        catch
        {
            return defaultValue;
        }
    }

    /// <summary>
    /// Converts a typed value to its raw INI string representation.
    /// </summary>
    protected static string? ConvertToRaw<T>(T? value)
    {
        var converter = ValueConverterRegistry.GetConverter(typeof(T));
        return converter?.ConvertToString(value);
    }
}
