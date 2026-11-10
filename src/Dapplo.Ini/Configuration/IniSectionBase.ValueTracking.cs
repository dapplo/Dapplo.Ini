// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace Dapplo.Ini.Configuration;

/// <summary>
/// Tracks, per key, whether the current value is a <em>user value</em> (read from the user file, or set by the
/// application after the load) or an <em>inherited</em> value (compiled default, defaults file), and which value is
/// hidden below a layer above the user file (constants file, value source). Only user values are written on save.
/// </summary>
public abstract partial class IniSectionBase
{
    private sealed class KeyState
    {
        // Where the current value came from.
        public ValueOrigin Origin = ValueOrigin.CompiledDefault;

        // The user chose a value for this key; UserRaw is that value, also while a layer above hides it.
        public bool IsUser;
        public string? UserRaw;

        // The value of the highest layer below the user file (defaults file); without one the compiled default applies.
        public bool HasInherited;
        public string? InheritedRaw;
        public ValueOrigin InheritedOrigin = ValueOrigin.CompiledDefault;
    }

    // Guarded by _sync, like _rawValues.
    private readonly Dictionary<string, KeyState> _keyStates = new(StringComparer.OrdinalIgnoreCase);

    // Sub-key dictionaries whose whole value was assigned through the property setter: every entry is a user value.
    private readonly HashSet<string> _userProperties = new(StringComparer.OrdinalIgnoreCase);

    // The serialized values of mutable properties (collections) after the load, to notice in-place changes
    // (section.List.Add(...) followed by MarkAsDirty()) that never pass a setter.
    private Dictionary<string, string?>? _mutableSnapshot;

    /// <summary>
    /// Set by <see cref="IniConfig"/> while it resets the section before applying the layers: the reset is then
    /// part of the load and neither marks the section dirty nor records anything.
    /// </summary>
    internal bool IsResetByConfig;

    // ── Hooks implemented by the source generator ────────────────────────────

    /// <summary>
    /// Resets the backing field of the property with the key <paramref name="key"/> to its compiled default,
    /// exactly like <see cref="ResetToDefaults"/> does for all properties. Returns <c>false</c> when the key is
    /// not a property of this section. Overridden by generated sections; the base implementation returns
    /// <c>false</c>.
    /// </summary>
    /// <param name="key">The property key (for a sub-key dictionary the key of the property itself, not of an entry).</param>
    protected virtual bool ResetPropertyToDefault(string key) => false;

    /// <summary>
    /// Returns the key of the property that stores <paramref name="rawKey"/>: the key itself for an ordinary
    /// property, <c>Property</c> for an entry <c>Property.subkey</c> of a sub-key dictionary, or <c>null</c> when
    /// the key belongs to no property. Overridden by generated sections; the base implementation returns
    /// <paramref name="rawKey"/>.
    /// </summary>
    protected virtual string? GetPropertyKey(string rawKey) => rawKey;

    /// <summary>
    /// Returns the serialized values of the properties that can be changed without their setter (collections and
    /// other reference types except <see cref="string"/>), in the form <see cref="GetAllRawValues"/> returns them.
    /// Generated sections override this with exactly those properties; the base implementation filters
    /// <see cref="GetAllRawValues"/> by <see cref="IIniSection.GetPropertyType"/>.
    /// </summary>
    protected virtual IEnumerable<KeyValuePair<string, string?>> GetMutableRawValues()
    {
        foreach (var kvp in GetAllRawValues())
        {
            var type = GetPropertyType(kvp.Key);
            if (type == null || !(type.IsValueType || type == typeof(string)))
                yield return kvp;
        }
    }

    /// <summary>
    /// Called by the generated <see cref="ResetToDefaults"/> after all properties have their compiled default:
    /// no key holds a user value anymore, so the next save removes them from the user file.
    /// </summary>
    protected void OnResetToDefaults()
    {
        if (IsResetByConfig)
            return;
        lock (_sync)
        {
            _keyStates.Clear();
            _userProperties.Clear();
        }
        CaptureMutableValues();
        MarkAsDirty();
    }

    /// <summary>
    /// Records that the whole value of the property <paramref name="key"/> was assigned by the application.
    /// Called by the generated setter of a sub-key dictionary, which can also assign an empty dictionary.
    /// </summary>
    protected void MarkPropertyAsUserValue(string key)
    {
        lock (_sync)
            _userProperties.Add(key);
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns <c>true</c> when <paramref name="key"/> holds a <em>user value</em>: a value read from the user
    /// file or set by the application after the load. Only user values are written to the user file; other
    /// keys inherit their value from the compiled default or a defaults file, so a later change there reaches
    /// the user. A user value stays one when it equals the inherited value; <see cref="ResetKey"/> and
    /// <see cref="ResetToDefaults"/> turn it back into an inherited value.
    /// </summary>
    /// <param name="key">The key of a property, or of an entry <c>Property.subkey</c> of a sub-key dictionary.</param>
    public bool IsUserValue(string key)
    {
        var propertyKey = GetPropertyKey(key);
        lock (_sync)
        {
            if (_keyStates.TryGetValue(key, out var state) && state.IsUser)
                return true;
            if (propertyKey == null)
                return false;
            if (_userProperties.Contains(propertyKey))
                return true;
            if (!string.Equals(propertyKey, key, StringComparison.OrdinalIgnoreCase))
                return false;
            // The key of a sub-key dictionary itself: a user value when any of its entries is one
            var prefix = key + ".";
            foreach (var kvp in _keyStates)
            {
                if (kvp.Value.IsUser && kvp.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }
    }

    /// <summary>
    /// Turns the value of <paramref name="key"/> back into an inherited value: the property gets the value of the
    /// defaults file, or else its compiled default, and the key is no longer written to the user file.
    /// When a value source above the user file supplies the key, that value stays in effect.
    /// Raises <c>PropertyChanged</c> when the value changes and marks the section dirty.
    /// </summary>
    /// <param name="key">The key of a property; for a sub-key dictionary the whole dictionary is reset.</param>
    /// <exception cref="AccessViolationException">The key is protected by a constants file.</exception>
    public void ResetKey(string key)
    {
        if (key is null) throw new ArgumentNullException(nameof(key));
        var propertyKey = GetPropertyKey(key) ?? key;
        ThrowIfConstant(propertyKey);
        ThrowIfConstantPrefix(propertyKey);

        var before = CaptureValuesForChangeNotification();
        lock (_sync)
        {
            _userProperties.Remove(propertyKey);
            foreach (var state in StatesOf(propertyKey))
            {
                state.Value.IsUser = false;
                state.Value.UserRaw = null;
            }
        }
        RestoreLayers(propertyKey);
        CaptureMutableValues();
        MarkAsDirty();
        if (before != null)
            RaisePropertyChangedForChanges(before);
    }

    // ── Internal API for IniConfig ────────────────────────────────────────────

    /// <inheritdoc/>
    public ValueOrigin GetValueOrigin(string key)
    {
        lock (_sync)
            return _keyStates.TryGetValue(key, out var state) ? state.Origin : ValueOrigin.CompiledDefault;
    }

    /// <summary>
    /// Applies a value of a configuration layer (defaults, user or constants file, value source) without marking
    /// the section dirty, and records where it came from. Constant protection is not checked: the caller decides
    /// which layer wins.
    /// </summary>
    internal void ApplyLayerValue(string key, string? value, ValueOrigin origin)
    {
        _currentKey = key;
        try
        {
            lock (_sync)
            {
                _rawValues[key] = value;
                var state = GetOrAddState(key);
                state.Origin = origin;
                if (origin.IsInherited)
                {
                    state.HasInherited = true;
                    state.InheritedRaw = value;
                    state.InheritedOrigin = origin;
                }
                else if (origin.Kind == ValueOriginKind.UserFile)
                {
                    state.IsUser = true;
                    state.UserRaw = value;
                }
            }
            OnRawValueSet(key, value);
        }
        finally
        {
            _currentKey = null;
        }
    }

    /// <summary>The values the application set since the load (user values that are not in the user file yet).</summary>
    internal List<KeyValuePair<string, string?>> GetApplicationValues()
    {
        lock (_sync)
            return _keyStates.Where(kvp => kvp.Value.IsUser && ReferenceEquals(kvp.Value.Origin, ValueOrigin.Application))
                .Select(kvp => new KeyValuePair<string, string?>(kvp.Key, kvp.Value.UserRaw))
                .ToList();
    }

    /// <summary>
    /// Puts back values the application set before a reload of the layers (see <see cref="GetApplicationValues"/>).
    /// A key that a constant or a value source above the user file now supplies keeps that value; the application's
    /// value is kept below it as the user value. Marks the section dirty.
    /// </summary>
    internal void RestoreApplicationValues(List<KeyValuePair<string, string?>> values)
    {
        foreach (var kvp in values)
        {
            bool hidden;
            lock (_sync)
            {
                var state = GetOrAddState(kvp.Key);
                hidden = _constantKeys.Contains(kvp.Key) || state.Origin.IsAboveUser;
                if (hidden)
                {
                    state.IsUser = true;
                    state.UserRaw = kvp.Value;
                }
            }
            if (!hidden)
                ApplyLayerValue(kvp.Key, kvp.Value, ValueOrigin.Application);
        }
        MarkAsDirty();
    }

    /// <summary>Forgets everything recorded about the values. Called before the layers are applied again.</summary>
    internal void ClearValueTracking()
    {
        lock (_sync)
        {
            _keyStates.Clear();
            _userProperties.Clear();
            _mutableSnapshot = null;
        }
    }

    /// <summary>Remembers the serialized values of the mutable properties, after the layers were applied.</summary>
    internal void CaptureMutableValues()
    {
        var snapshot = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var kvp in GetMutableRawValues())
            snapshot[kvp.Key] = kvp.Value;
        lock (_sync)
            _mutableSnapshot = snapshot;
    }

    /// <summary>
    /// Returns the keys of mutable properties whose serialized value differs from the one after the load, or
    /// <c>null</c> when none does. Such a value was changed in place, so it is a user value.
    /// </summary>
    internal HashSet<string>? GetKeysChangedInPlace()
    {
        Dictionary<string, string?>? snapshot;
        lock (_sync)
            snapshot = _mutableSnapshot;
        HashSet<string>? changed = null;
        foreach (var kvp in GetMutableRawValues())
        {
            if (snapshot != null && snapshot.TryGetValue(kvp.Key, out var loaded) && string.Equals(loaded, kvp.Value, StringComparison.Ordinal))
                continue;
            (changed ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase)).Add(kvp.Key);
        }
        return changed;
    }

    /// <summary>How a key is written on save.</summary>
    internal enum SaveKind
    {
        /// <summary>Written as a value.</summary>
        Value,
        /// <summary>An inherited value: not written, or only as a comment.</summary>
        Inherited,
        /// <summary>Not written at all (a constant without a user value below it).</summary>
        Skip
    }

    /// <summary>
    /// Decides how <paramref name="key"/> with its current serialized value <paramref name="current"/> is written.
    /// </summary>
    /// <param name="key">The raw key, as <see cref="GetAllRawValues"/> returns it.</param>
    /// <param name="current">The current serialized value.</param>
    /// <param name="changedInPlace">The value was changed in place since the load: it becomes a user value.</param>
    /// <param name="saveAllValues">Write inherited values as values too (<see cref="IniConfigBuilder.SaveAllValues"/>).</param>
    /// <param name="value">The value to write.</param>
    internal SaveKind GetSaveKind(string key, string? current, bool changedInPlace, bool saveAllValues, out string? value)
    {
        value = current;
        var propertyKey = GetPropertyKey(key);
        lock (_sync)
        {
            _keyStates.TryGetValue(key, out var state);
            if (_constantKeys.Contains(key) || (propertyKey != null && _constantKeys.Contains(propertyKey)))
            {
                // A constant is never written; the user's own value below it stays, so it comes back when the
                // administrator removes the constant.
                if (state is { IsUser: true })
                {
                    value = state.UserRaw;
                    return SaveKind.Value;
                }
                return SaveKind.Skip;
            }

            var wholePropertyIsUser = propertyKey != null
                                      && !string.Equals(propertyKey, key, StringComparison.OrdinalIgnoreCase)
                                      && _userProperties.Contains(propertyKey);
            if (changedInPlace && state is not { IsUser: true } && !wholePropertyIsUser)
            {
                state ??= GetOrAddState(key);
                state.IsUser = true;
                state.UserRaw = current;
                state.Origin = ValueOrigin.Application;
            }

            if (state is { IsUser: true } && state.Origin.IsAboveUser)
            {
                // The current value comes from a layer above the user file: keep the user's own value.
                value = state.UserRaw;
                return SaveKind.Value;
            }
            if (state is { IsUser: true } || wholePropertyIsUser || saveAllValues)
                return SaveKind.Value;
            return SaveKind.Inherited;
        }
    }

    /// <summary>
    /// A quick check, without allocations, for <see cref="GetHiddenUserValues"/>: <c>false</c> when no user value
    /// is hidden by a layer above the user file or by a constant, so the saving code can skip building the key set.
    /// </summary>
    internal bool MayHaveHiddenUserValues()
    {
        lock (_sync)
        {
            if (_constantKeys.Count > 0)
                return true;
            foreach (var kvp in _keyStates)
            {
                if (kvp.Value.IsUser && kvp.Value.Origin.IsAboveUser)
                    return true;
            }
            return false;
        }
    }

    /// <summary>
    /// The user values hidden below a constant or a value source that are not among <paramref name="writtenKeys"/>:
    /// e.g. entries of a dictionary that a value source replaced as a whole. They are written too, so they come
    /// back when the source stops supplying the key.
    /// </summary>
    internal List<KeyValuePair<string, string?>> GetHiddenUserValues(ICollection<string> writtenKeys)
    {
        lock (_sync)
            return _keyStates
                .Where(kvp => kvp.Value.IsUser && !writtenKeys.Contains(kvp.Key)
                              && (kvp.Value.Origin.IsAboveUser || _constantKeys.Contains(kvp.Key)
                                  || (GetPropertyKey(kvp.Key) is { } p && _constantKeys.Contains(p))))
                .Where(kvp => GetDeclaredPropertyKey(kvp.Key) != null)
                .Select(kvp => new KeyValuePair<string, string?>(kvp.Key, kvp.Value.UserRaw))
                .ToList();
    }

    /// <summary>
    /// Turns every user value that equals the value it would inherit (defaults file, else compiled default) into
    /// an inherited value. Returns the number of keys that were turned. The current values do not change.
    /// </summary>
    internal int ConvertUnchangedUserValuesToInherited()
    {
        var current = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var kvp in GetAllRawValues())
            current[kvp.Key] = kvp.Value;

        // The properties that hold at least one user value
        var properties = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        lock (_sync)
        {
            properties.UnionWith(_userProperties);
            foreach (var kvp in _keyStates)
            {
                if (kvp.Value.IsUser && GetPropertyKey(kvp.Key) is { } propertyKey)
                    properties.Add(propertyKey);
            }
        }

        var converted = 0;
        foreach (var propertyKey in properties)
        {
            var currentEntries = EntriesOf(current, propertyKey);
            // An empty dictionary can't be restored entry by entry, and differs from any inherited value anyway
            if (currentEntries.Count == 0 || !ResetPropertyToDefault(propertyKey))
                continue;

            // Let the property hold its inherited value, serialize it, then restore the current value
            List<KeyValuePair<string, string?>> inheritedLayers;
            lock (_sync)
            {
                inheritedLayers = StatesOf(propertyKey)
                    .Where(s => s.Value.HasInherited)
                    .Select(s => new KeyValuePair<string, string?>(s.Key, s.Value.InheritedRaw))
                    .ToList();
            }
            foreach (var kvp in inheritedLayers)
                OnRawValueSet(kvp.Key, kvp.Value);
            var inherited = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            foreach (var kvp in GetAllRawValues())
            {
                if (BelongsTo(kvp.Key, propertyKey))
                    inherited[kvp.Key] = kvp.Value;
            }
            ResetPropertyToDefault(propertyKey);
            foreach (var kvp in currentEntries)
                OnRawValueSet(kvp.Key, kvp.Value);

            lock (_sync)
            {
                var wholeProperty = _userProperties.Remove(propertyKey);
                foreach (var kvp in currentEntries)
                {
                    var state = _keyStates.TryGetValue(kvp.Key, out var s) ? s : null;
                    if (state is not { IsUser: true } && !wholeProperty)
                        continue;
                    var userValue = state is { IsUser: true } && state.Origin.IsAboveUser ? state.UserRaw : kvp.Value;
                    if (inherited.TryGetValue(kvp.Key, out var inheritedValue) && string.Equals(inheritedValue, userValue, StringComparison.Ordinal))
                    {
                        if (state != null)
                        {
                            state.IsUser = false;
                            state.UserRaw = null;
                            if (state.Origin.Kind == ValueOriginKind.UserFile)
                                state.Origin = state.InheritedOrigin;
                        }
                        converted++;
                    }
                    else if (state is not { IsUser: true })
                    {
                        // Part of a dictionary the application assigned as a whole: this entry stays a user value
                        state ??= GetOrAddState(kvp.Key);
                        state.IsUser = true;
                        state.UserRaw = kvp.Value;
                        state.Origin = ValueOrigin.Application;
                    }
                }
            }
        }

        if (converted > 0)
        {
            CaptureMutableValues();
            MarkAsDirty();
        }
        return converted;
    }

    /// <summary>
    /// Returns the declared property key that stores <paramref name="rawKey"/> (see <see cref="GetPropertyKey"/>),
    /// spelled as declared, or <c>null</c> when no declared property stores it.
    /// </summary>
    internal string? GetDeclaredPropertyKey(string rawKey)
    {
        var propertyKey = GetPropertyKey(rawKey);
        if (propertyKey == null)
            return null;
        foreach (var key in GetKeys())
        {
            if (string.Equals(key, propertyKey, StringComparison.OrdinalIgnoreCase))
                return key;
        }
        return null;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Marks <paramref name="key"/> as set by the application. Caller holds <see cref="_sync"/>.</summary>
    private void MarkUserValueLocked(string key, string? value)
    {
        var state = GetOrAddState(key);
        state.IsUser = true;
        state.UserRaw = value;
        state.Origin = ValueOrigin.Application;
    }

    private KeyState GetOrAddState(string key)
    {
        if (!_keyStates.TryGetValue(key, out var state))
        {
            state = new KeyState();
            _keyStates[key] = state;
        }
        return state;
    }

    private static bool BelongsTo(string rawKey, string propertyKey)
        => rawKey.Equals(propertyKey, StringComparison.OrdinalIgnoreCase)
           || (rawKey.Length > propertyKey.Length && rawKey[propertyKey.Length] == '.'
               && rawKey.StartsWith(propertyKey, StringComparison.OrdinalIgnoreCase));

    /// <summary>The states of the property and of its sub-keys. Caller holds <see cref="_sync"/>.</summary>
    private List<KeyValuePair<string, KeyState>> StatesOf(string propertyKey)
        => _keyStates.Where(kvp => BelongsTo(kvp.Key, propertyKey)).ToList();

    private static List<KeyValuePair<string, string?>> EntriesOf(Dictionary<string, string?> values, string propertyKey)
        => values.Where(kvp => BelongsTo(kvp.Key, propertyKey)).ToList();

    /// <summary>
    /// Gives the property the value of the layers other than the user: its compiled default, then the inherited
    /// values (defaults file) and the values of layers above the user file, as the load applied them.
    /// </summary>
    private void RestoreLayers(string propertyKey)
    {
        if (!ResetPropertyToDefault(propertyKey))
            return;
        var apply = new List<KeyValuePair<string, string?>>();
        lock (_sync)
        {
            var above = new List<KeyValuePair<string, string?>>();
            foreach (var kvp in StatesOf(propertyKey))
            {
                var state = kvp.Value;
                _rawValues.TryGetValue(kvp.Key, out var currentRaw);
                if (state.Origin.IsAboveUser)
                {
                    above.Add(new KeyValuePair<string, string?>(kvp.Key, currentRaw));
                    if (state.HasInherited)
                        apply.Add(new KeyValuePair<string, string?>(kvp.Key, state.InheritedRaw));
                    continue;
                }
                _rawValues.Remove(kvp.Key);
                if (state.HasInherited)
                {
                    state.Origin = state.InheritedOrigin;
                    _rawValues[kvp.Key] = state.InheritedRaw;
                    apply.Add(new KeyValuePair<string, string?>(kvp.Key, state.InheritedRaw));
                }
                else
                {
                    _keyStates.Remove(kvp.Key);
                }
            }
            apply.AddRange(above);
        }
        foreach (var kvp in apply)
            OnRawValueSet(kvp.Key, kvp.Value);
    }
}
