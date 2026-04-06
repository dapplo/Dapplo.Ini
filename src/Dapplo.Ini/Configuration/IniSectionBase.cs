// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Dapplo.Ini.Converters;
using Dapplo.Ini.Interfaces;
#if NET
using System.Diagnostics.CodeAnalysis;
#endif

namespace Dapplo.Ini.Configuration;

/// <summary>
/// Base class for all source-generated INI section classes.
/// Provides the raw key/value dictionary and converter-aware get/set helpers.
/// </summary>
public abstract class IniSectionBase : IIniSection
{
    // Raw backing store (key → string as it appears in the file)
    private readonly Dictionary<string, string?> _rawValues =
        new(StringComparer.OrdinalIgnoreCase);

    // Tracks keys that were loaded from a constants file and are protected against change.
    private readonly HashSet<string> _constantKeys = new(StringComparer.OrdinalIgnoreCase);

    // Dirty flag: set when a value is written via SetRawValue; cleared by IniConfig after Save/Reload.
    private bool _isDirty;

    // Tracks the key currently being applied from an INI file (set/cleared by SetRawValue).
    // When non-null, ConvertFromRaw knows a file-load is in progress and can report errors.
    private string? _currentKey;

    // Callback invoked by ConvertFromRaw when a value cannot be converted to the target type.
    // Set by IniConfig.ApplyIniFile before loading file entries; null at all other times.
    internal Action<string, string, string?, Exception>? ConversionFailedCallback;

    /// <summary>
    /// When <c>true</c>, reference-type properties return an empty value (e.g.
    /// <see cref="string.Empty"/>, an empty list, or an empty array) instead of <c>null</c>
    /// when no INI key is present and the property has no explicit default value.
    /// <para>
    /// Set by <see cref="IniConfig"/> on every load/reload cycle based on the
    /// <see cref="IniConfigBuilder.EmptyWhenNull"/> builder setting.
    /// </para>
    /// </summary>
    protected internal bool GlobalEmptyWhenNull;

    // ── IIniSection ───────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public abstract string SectionName { get; }

    /// <inheritdoc/>
    public string? GetRawValue(string key)
        => _rawValues.TryGetValue(key, out var v) ? v : null;

    /// <inheritdoc/>
    public void SetRawValue(string key, string? value)
    {
        if (_constantKeys.Contains(key))
            throw new AccessViolationException(
                $"The configuration key '{key}' in section '{SectionName}' is protected by an administrator constants file and cannot be modified.");

        _currentKey = key;
        try
        {
            _rawValues[key] = value;
            _isDirty = true;
            OnRawValueSet(key, value);
        }
        finally
        {
            _currentKey = null;
        }
    }

    /// <inheritdoc/>
    public abstract void ResetToDefaults();

    /// <inheritdoc/>
    public bool HasChanges => _isDirty;

    /// <inheritdoc/>
    public void MarkAsDirty() => _isDirty = true;

    /// <inheritdoc/>
    public bool IsConstant(string key) => _constantKeys.Contains(key);

    /// <inheritdoc/>
    /// <remarks>
    /// The base implementation returns the keys currently present in the raw backing store.
    /// Source-generated subclasses override this with the compile-time property list.
    /// </remarks>
    public virtual IEnumerable<string> GetKeys() => _rawValues.Keys;

    /// <inheritdoc/>
    /// <remarks>
    /// The base implementation always returns <c>null</c>.
    /// Source-generated subclasses override this with the exact property type for each key.
    /// </remarks>
    public virtual Type? GetPropertyType(string key) => null;

    // ── Unknown key detection ─────────────────────────────────────────────────

    /// <summary>
    /// Returns <c>true</c> when <paramref name="key"/> corresponds to a property declared on
    /// the section interface; <c>false</c> when the key is not known to this section.
    /// <para>
    /// The source generator overrides this with the exact set of declared property key names so
    /// that the framework can detect and notify unknown keys without incurring a full scan.
    /// Non-generated subclasses keep the default (returns <c>true</c> for every key, meaning
    /// no unknown-key notifications are raised for them).
    /// </para>
    /// </summary>
    /// <param name="key">The key name to check (comparison is case-insensitive).</param>
    public virtual bool IsKnownKey(string key) => true;

    // ── Internal helpers for IniConfig ────────────────────────────────────────

    /// <summary>
    /// Clears the dirty flag. Called by <see cref="IniConfig"/> after a successful
    /// <see cref="IniConfig.Save"/> or <see cref="IniConfig.Reload"/>.
    /// </summary>
    internal void ClearDirtyFlag() => _isDirty = false;

    /// <summary>
    /// Marks <paramref name="key"/> as a constant (loaded from an admin constants file).
    /// After this call any attempt to change the key via <see cref="SetRawValue"/> will throw
    /// <see cref="AccessViolationException"/>.
    /// </summary>
    internal void MarkKeyAsConstant(string key) => _constantKeys.Add(key);

    /// <summary>
    /// Clears all constant-key protections. Called by <see cref="IniConfig"/> at the start
    /// of every load / reload cycle so that protections are re-established from the current
    /// constants files.
    /// </summary>
    internal void ClearConstants() => _constantKeys.Clear();

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

    /// <summary>
    /// Returns the description comment for this section, or <c>null</c> when no description
    /// was specified. The source generator overrides this with the value from
    /// <c>[IniSection(Description = "...")]</c> or <c>[Description("...")]</c>.
    /// </summary>
    public virtual string? GetSectionDescription() => null;

    /// <summary>
    /// Returns the description comment for the property identified by <paramref name="key"/>,
    /// or <c>null</c> when no description was specified for that property. The source generator
    /// overrides this with the values from <c>[IniValue(Description = "...")]</c> or
    /// <c>[Description("...")]</c> on each property.
    /// </summary>
    /// <param name="key">The INI key name (case-insensitive).</param>
    public virtual string? GetPropertyDescription(string key) => null;

    // ── Converter helpers (used by generated code) ────────────────────────────

    /// <summary>
    /// Converts a raw INI string to <typeparamref name="T"/> using the registered converter.
    /// Falls back to <paramref name="defaultValue"/> when the raw value is absent or conversion
    /// fails.  When a <see cref="ConversionFailedCallback"/> is registered (i.e. the value is
    /// being applied from an INI file) the callback is invoked with the failed key and exception
    /// so that <see cref="IIniConfigListener.OnValueConversionFailed"/> can be raised.
    /// </summary>
#if NET
    [RequiresDynamicCode("Enum types require dynamic code via EnumConverter. Register a typed converter for full AOT compatibility.")]
    [RequiresUnreferencedCode("Enum types require unreferenced-code access via EnumConverter. Register a typed converter for full trim compatibility.")]
#endif
    protected T? ConvertFromRaw<T>(string? raw, T? defaultValue = default)
    {
        var converter = ValueConverterRegistry.GetConverter(typeof(T));
        if (converter == null) return defaultValue;
        try
        {
            var result = converter.ConvertFromString(raw);
            return result is T typed ? typed : defaultValue;
        }
        catch (Exception ex)
        {
            // Only report if we are currently loading from a file (_currentKey is set).
            if (_currentKey != null)
                ConversionFailedCallback?.Invoke(SectionName, _currentKey, raw, ex);
            return defaultValue;
        }
    }

    /// <summary>
    /// Converts a typed value to its raw INI string representation.
    /// </summary>
#if NET
    [RequiresDynamicCode("Enum types require dynamic code via EnumConverter. Register a typed converter for full AOT compatibility.")]
    [RequiresUnreferencedCode("Enum types require unreferenced-code access via EnumConverter. Register a typed converter for full trim compatibility.")]
#endif
    protected static string? ConvertToRaw<T>(T? value)
    {
        var converter = ValueConverterRegistry.GetConverter(typeof(T));
        return converter?.ConvertToString(value);
    }
}

