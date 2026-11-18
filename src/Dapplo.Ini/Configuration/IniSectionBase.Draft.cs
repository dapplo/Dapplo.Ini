// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections;
using Dapplo.Ini.Interfaces;

namespace Dapplo.Ini.Configuration;

/// <summary>
/// Drafts: detached copies of a section that a settings dialog can edit without the rest of the application seeing
/// the changes, until they are applied. See the wiki page "Drafts (edit dialogs)" and <see cref="IniSectionDraftExtensions"/>.
/// </summary>
public abstract partial class IniSectionBase
{
    /// <summary>What a draft knows about where it came from.</summary>
    private sealed class DraftState
    {
        public DraftState(IniSectionBase source, IniSectionBase baseValues)
        {
            Source = source;
            BaseValues = baseValues;
        }

        /// <summary>The section the draft was created from.</summary>
        public readonly IniSectionBase Source;

        /// <summary>
        /// A private copy holding the base values: the live values when the draft was created (or last applied or
        /// refreshed). Never handed out, never changed through its setters.
        /// </summary>
        public readonly IniSectionBase BaseValues;

        /// <summary>The keys of the properties that were assigned on the draft.</summary>
        public readonly HashSet<string> Assigned = new(StringComparer.OrdinalIgnoreCase);
    }

    // Set only on drafts, right after they are created; null for every section an IniConfig owns.
    private DraftState? _draft;

    /// <summary>
    /// <c>true</c> when this instance is a draft created with <see cref="IniSectionDraftExtensions.CreateDraft{T}"/>.
    /// A draft is not registered with any <see cref="IniConfig"/>: it is never saved, reloaded or passed to lifecycle hooks.
    /// </summary>
    public bool IsDraft => _draft != null;

    /// <summary>The section this draft was created from, or <c>null</c> when this instance is not a draft.</summary>
    public IIniSection? DraftSource => _draft?.Source;

    // ── Hooks implemented by the source generator ────────────────────────────

    /// <summary>
    /// Creates a new, empty instance of the same generated class. Generated sections override this;
    /// the base implementation returns <c>null</c>, which means the section does not support drafts.
    /// </summary>
    protected virtual IniSectionBase? CreateDraftInstance() => null;

    /// <summary>
    /// The keys of all properties a draft copies, in a fixed order: the index of a key is the index passed to
    /// <see cref="CopyDraftValue"/>, <see cref="DraftValueEquals"/> and <see cref="IsDraftValueMutable"/>.
    /// </summary>
    protected virtual string[] GetDraftKeys() => Array.Empty<string>();

    /// <summary>
    /// <c>true</c> when the value of the property with the index <paramref name="index"/> can change without its
    /// setter (a collection), so an in-place change on the draft counts as a change.
    /// </summary>
    protected virtual bool IsDraftValueMutable(int index) => false;

    /// <summary>
    /// Copies the value of the property with the index <paramref name="index"/> from this instance to
    /// <paramref name="target"/>, an instance of the same class. Collections are copied, so the two never share one.
    /// With <paramref name="useSetter"/> the value is assigned through the property setter of the target (events,
    /// dirty flag, validation and constant protection as for any other write); otherwise the backing field is set.
    /// </summary>
    protected virtual void CopyDraftValue(int index, IniSectionBase target, bool useSetter)
    {
    }

    /// <summary>
    /// <c>true</c> when the property with the index <paramref name="index"/> has the same value in this instance
    /// and in <paramref name="other"/>, an instance of the same class. Collections are compared by content.
    /// </summary>
    protected virtual bool DraftValueEquals(int index, IniSectionBase other) => true;

    /// <summary>
    /// Called on a draft after it was created or refreshed: generated sections with validation run all their
    /// validations, so <c>INotifyDataErrorInfo</c> reports the errors of the draft's own values.
    /// </summary>
    protected virtual void OnDraftValuesCopied()
    {
    }

    /// <summary>
    /// Records that the property <paramref name="key"/> was assigned. Does nothing unless this instance is a draft.
    /// Called by generated setters of properties that are not stored in the INI file (runtime-only, ignored).
    /// </summary>
    protected void NoteDraftAssignment(string key)
    {
        var draft = _draft;
        if (draft == null)
            return;
        lock (_sync)
            draft.Assigned.Add(key);
    }

    /// <summary>Like <see cref="NoteDraftAssignment"/>, for a raw key (an entry <c>Property.subkey</c> counts for its property).</summary>
    private void NoteDraftRawAssignment(string rawKey)
    {
        if (_draft == null)
            return;
        NoteDraftAssignment(GetPropertyKey(rawKey) ?? rawKey);
    }

    /// <summary>A draft whose properties were all reset (<see cref="ResetToDefaults"/>): every property counts as assigned.</summary>
    private void NoteDraftAllAssigned()
    {
        if (_draft == null)
            return;
        foreach (var key in GetDraftKeys())
            NoteDraftAssignment(key);
    }

    // ── Draft operations (public entry points in IniSectionDraftExtensions) ────

    /// <summary>Creates a draft of this section, see <see cref="IniSectionDraftExtensions.CreateDraft{T}"/>.</summary>
    internal IniSectionBase CreateDraftCore()
    {
        var draft = CreateDraftInstance()
                    ?? throw new NotSupportedException($"The section '{SectionName}' ({GetType().Name}) does not support drafts: only sections generated by Dapplo.Ini.Generator do.");
        var baseValues = CreateDraftInstance()!;

        CopyStateTo(draft);
        CopyStateTo(baseValues);
        var keys = GetDraftKeys();
        for (var i = 0; i < keys.Length; i++)
        {
            CopyDraftValue(i, draft, false);
            CopyDraftValue(i, baseValues, false);
        }
        draft._draft = new DraftState(this, baseValues);
        draft.OnDraftValuesCopied();
        return draft;
    }

    /// <summary>The keys that <see cref="ApplyDraftTo"/> would write now.</summary>
    internal IReadOnlyList<string> GetDraftChangesCore()
    {
        var indexes = GetDraftChangedIndexes(RequireDraft());
        var keys = GetDraftKeys();
        return indexes.Select(i => keys[i]).ToArray();
    }

    /// <summary>The keys assigned on the draft since it was created, applied or refreshed.</summary>
    internal IReadOnlyCollection<string> GetDraftAssignedKeysCore()
    {
        var draft = RequireDraft();
        lock (_sync)
            return draft.Assigned.ToArray();
    }

    /// <summary>The value the property <paramref name="key"/> had in the source section when the draft was created (or last applied or refreshed).</summary>
    internal object? GetDraftBaseValueCore(string key) => RequireDraft().BaseValues.GetValue(key);

    /// <summary>Throws before anything is written when one of the keys the draft would write is protected in <paramref name="target"/>.</summary>
    internal void ThrowIfDraftTargetLocked(IIniSection target)
    {
        var draft = RequireDraft();
        var targetBase = CheckDraftTarget(target);
        var keys = GetDraftKeys();
        foreach (var index in GetDraftChangedIndexes(draft))
            targetBase.ThrowIfConstantPrefix(keys[index]);
    }

    /// <summary>
    /// Writes the keys that were assigned on the draft (or changed in place) and differ from the base value to
    /// <paramref name="target"/>, through its property setters. Keys that were not assigned are never written, so a
    /// value the application changed in the live section while the draft was open stays. Returns the written keys.
    /// </summary>
    internal IReadOnlyList<string> ApplyDraftTo(IIniSection target)
    {
        var draft = RequireDraft();
        var targetBase = CheckDraftTarget(target);
        var indexes = GetDraftChangedIndexes(draft);
        if (indexes.Count == 0)
            return Array.Empty<string>();

        var keys = GetDraftKeys();
        // Check all locks first, so a locked key doesn't leave a half-applied draft behind
        foreach (var index in indexes)
            targetBase.ThrowIfConstantPrefix(keys[index]);

        var transactional = target as ITransactional;
        var began = false;
        if (transactional is { IsInTransaction: false })
        {
            transactional.Begin();
            began = true;
        }
        try
        {
            foreach (var index in indexes)
                CopyDraftValue(index, targetBase, true);
            if (began)
                transactional!.Commit();
        }
        catch
        {
            if (began && transactional!.IsInTransaction)
                transactional.Rollback();
            throw;
        }

        // The written values are the new base values: applying again writes nothing until the draft changes again.
        var applied = new string[indexes.Count];
        for (var i = 0; i < indexes.Count; i++)
        {
            var index = indexes[i];
            CopyDraftValue(index, draft.BaseValues, false);
            applied[i] = keys[index];
        }
        lock (_sync)
        {
            foreach (var key in applied)
                draft.Assigned.Remove(key);
        }
        return applied;
    }

    /// <summary>
    /// Takes over the current values of the source section for every key that was not assigned (nor changed in place)
    /// on the draft, raises <c>PropertyChanged</c> on the draft for each value that changes, and refreshes the
    /// protection (constants, policies) of all keys. Returns the keys whose value changed.
    /// </summary>
    internal IReadOnlyList<string> RefreshDraftCore()
    {
        var draft = RequireDraft();
        var source = draft.Source;
        var keys = GetDraftKeys();
        var before = CaptureValuesForChangeNotification();
        var refreshed = new List<string>();

        for (var i = 0; i < keys.Length; i++)
        {
            bool assigned;
            lock (_sync)
                assigned = draft.Assigned.Contains(keys[i]);
            if (assigned || !DraftValueEquals(i, draft.BaseValues))
                continue;   // the user edited this key: keep the draft's value
            if (!source.DraftValueEquals(i, this))
            {
                source.CopyDraftValue(i, this, false);
                CopyKeyStateFrom(source, keys[i]);
                refreshed.Add(keys[i]);
            }
            source.CopyDraftValue(i, draft.BaseValues, false);
        }

        // Locks follow the live section (e.g. a reload brought a new constants file or policy)
        string[] constants;
        lock (source._sync)
            constants = source._constantKeys.ToArray();
        lock (_sync)
        {
            _constantKeys.Clear();
            _constantKeys.UnionWith(constants);
        }

        if (before != null)
            RaisePropertyChangedForChanges(before);
        if (refreshed.Count > 0)
            OnDraftValuesCopied();
        return refreshed;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private DraftState RequireDraft()
        => _draft ?? throw new InvalidOperationException($"The section '{SectionName}' is not a draft. Create one with CreateDraft().");

    private IniSectionBase CheckDraftTarget(IIniSection target)
    {
        if (target is null) throw new ArgumentNullException(nameof(target));
        if (target is not IniSectionBase targetBase || targetBase.GetType() != GetType())
            throw new ArgumentException($"A draft of '{SectionName}' ({GetType().Name}) can only be applied to a section of the same type, not to {target.GetType().Name}.", nameof(target));
        if (ReferenceEquals(target, this))
            throw new ArgumentException("A draft cannot be applied to itself.", nameof(target));
        return targetBase;
    }

    /// <summary>The indexes of the properties to write: assigned (or mutable) and different from the base value.</summary>
    private List<int> GetDraftChangedIndexes(DraftState draft)
    {
        var keys = GetDraftKeys();
        var result = new List<int>();
        for (var i = 0; i < keys.Length; i++)
        {
            bool assigned;
            lock (_sync)
                assigned = draft.Assigned.Contains(keys[i]);
            if ((assigned || IsDraftValueMutable(i)) && !DraftValueEquals(i, draft.BaseValues))
                result.Add(i);
        }
        return result;
    }

    /// <summary>
    /// Copies the bookkeeping of this section (raw values, value origins, user values, constants and policy locks,
    /// the empty-when-null setting) to <paramref name="target"/>, a new instance that nothing else references yet.
    /// The dirty flag, the owner's change callback and the conversion callback are not copied.
    /// </summary>
    private void CopyStateTo(IniSectionBase target)
    {
        lock (_sync)
        {
            foreach (var kvp in _rawValues)
                target._rawValues[kvp.Key] = kvp.Value;
            target._constantKeys.UnionWith(_constantKeys);
            foreach (var kvp in _keyStates)
                target._keyStates[kvp.Key] = CopyKeyState(kvp.Value);
            target._userProperties.UnionWith(_userProperties);
            if (_mutableSnapshot != null)
                target._mutableSnapshot = new Dictionary<string, string?>(_mutableSnapshot, StringComparer.OrdinalIgnoreCase);
        }
        target.GlobalEmptyWhenNull = GlobalEmptyWhenNull;
    }

    /// <summary>Copies the raw values and value origins of the property <paramref name="propertyKey"/> (and its sub-keys) from <paramref name="source"/>.</summary>
    private void CopyKeyStateFrom(IniSectionBase source, string propertyKey)
    {
        List<KeyValuePair<string, string?>> raw;
        List<KeyValuePair<string, KeyState>> states;
        bool userProperty;
        lock (source._sync)
        {
            raw = source._rawValues.Where(kvp => BelongsTo(kvp.Key, propertyKey)).ToList();
            states = source._keyStates.Where(kvp => BelongsTo(kvp.Key, propertyKey))
                .Select(kvp => new KeyValuePair<string, KeyState>(kvp.Key, CopyKeyState(kvp.Value)))
                .ToList();
            userProperty = source._userProperties.Contains(propertyKey);
        }
        lock (_sync)
        {
            foreach (var key in _rawValues.Keys.Where(k => BelongsTo(k, propertyKey)).ToList())
                _rawValues.Remove(key);
            foreach (var key in _keyStates.Keys.Where(k => BelongsTo(k, propertyKey)).ToList())
                _keyStates.Remove(key);
            foreach (var kvp in raw)
                _rawValues[kvp.Key] = kvp.Value;
            foreach (var kvp in states)
                _keyStates[kvp.Key] = kvp.Value;
            if (userProperty)
                _userProperties.Add(propertyKey);
            else
                _userProperties.Remove(propertyKey);
        }
    }

    private static KeyState CopyKeyState(KeyState state) => new()
    {
        Origin = state.Origin,
        IsUser = state.IsUser,
        UserRaw = state.UserRaw,
        HasInherited = state.HasInherited,
        InheritedRaw = state.InheritedRaw,
        InheritedOrigin = state.InheritedOrigin
    };

    /// <summary>Throws when <paramref name="section"/> is a draft: drafts are never registered with a configuration.</summary>
    internal static void ThrowIfDraft(IIniSection section, string paramName)
    {
        if (section is IniSectionBase { IsDraft: true })
            throw new ArgumentException($"The section '{section.SectionName}' is a draft. Drafts cannot be registered; apply them to the live section instead.", paramName);
    }

    // ── Copy and compare helpers used by generated code ──────────────────────

    /// <summary>Copies an array (the elements themselves are not copied).</summary>
    protected static T[]? DraftCopyArray<T>(T[]? source) => source == null ? null : (T[])source.Clone();

    /// <summary>Copies a list-like collection into a new <see cref="List{T}"/>.</summary>
    protected static List<T>? DraftCopyList<T>(IEnumerable<T>? source) => source == null ? null : new List<T>(source);

    /// <summary>Copies a set into a new <see cref="HashSet{T}"/> with the same comparer.</summary>
    protected static HashSet<T>? DraftCopySet<T>(IEnumerable<T>? source)
        => source switch
        {
            null => null,
            HashSet<T> set => new HashSet<T>(set, set.Comparer),
            _ => new HashSet<T>(source)
        };

    /// <summary>Copies a dictionary into a new <see cref="Dictionary{TKey,TValue}"/> with the same comparer.</summary>
    protected static Dictionary<TKey, TValue>? DraftCopyDictionary<TKey, TValue>(IEnumerable<KeyValuePair<TKey, TValue>>? source)
        where TKey : notnull
    {
        if (source == null)
            return null;
        var copy = source is Dictionary<TKey, TValue> dictionary
            ? new Dictionary<TKey, TValue>(dictionary.Count, dictionary.Comparer)
            : new Dictionary<TKey, TValue>();
        foreach (var kvp in source)
            copy[kvp.Key] = kvp.Value;
        return copy;
    }

    /// <summary>Compares two dictionaries by content (same keys, equal values), ignoring the order of the entries.</summary>
    protected static bool DraftDictionaryEquals<TKey, TValue>(IEnumerable<KeyValuePair<TKey, TValue>>? a, IEnumerable<KeyValuePair<TKey, TValue>>? b)
        where TKey : notnull
    {
        if (ReferenceEquals(a, b)) return true;
        if (a == null || b == null) return false;
        var lookup = b as IDictionary<TKey, TValue> ?? DraftCopyDictionary(b)!;
        var count = 0;
        var comparer = EqualityComparer<TValue>.Default;
        foreach (var kvp in a)
        {
            count++;
            if (!lookup.TryGetValue(kvp.Key, out var other) || !comparer.Equals(kvp.Value, other))
                return false;
        }
        return count == lookup.Count;
    }

    /// <summary>
    /// Copies a value of a type stored in the INI file that has no copy of its own: serialized and parsed again with
    /// its value converter. Returns <paramref name="value"/> itself when there is no converter for the type.
    /// </summary>
#if NET
    [System.Diagnostics.CodeAnalysis.RequiresDynamicCode("Enum, list, and array types may require dynamic converter creation. Register typed converters for full AOT compatibility.")]
    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("Enum, list, and array types may require unreferenced-code access via runtime converter creation. Register typed converters for full trim compatibility.")]
#endif
    protected T? DraftCopyViaConverter<T>(T? value, char? listDelimiter)
    {
        if (value == null)
            return value;
        var raw = listDelimiter is { } delimiter ? ConvertToRaw(value, delimiter) : ConvertToRaw(value);
        if (raw == null)
            return value;
        var copy = listDelimiter is { } d ? ConvertFromRaw<T>(raw, d) : ConvertFromRaw<T>(raw);
        return copy ?? value;
    }

    /// <summary>Compares two values of a type stored in the INI file by their serialized form (or by content without a converter).</summary>
#if NET
    [System.Diagnostics.CodeAnalysis.RequiresDynamicCode("Enum, list, and array types may require dynamic converter creation. Register typed converters for full AOT compatibility.")]
    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("Enum, list, and array types may require unreferenced-code access via runtime converter creation. Register typed converters for full trim compatibility.")]
#endif
    protected static bool DraftEqualsViaConverter<T>(T? a, T? b, char? listDelimiter)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a == null || b == null) return false;
        var rawA = listDelimiter is { } delimiter ? ConvertToRaw(a, delimiter) : ConvertToRaw(a);
        var rawB = listDelimiter is { } d ? ConvertToRaw(b, d) : ConvertToRaw(b);
        if (rawA == null && rawB == null)
            return DraftValuesEqual(a, b);
        return string.Equals(rawA, rawB, StringComparison.Ordinal);
    }

    /// <summary>Compares two values; collections (except strings) are compared element by element.</summary>
    protected static bool DraftValuesEqual(object? a, object? b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a == null || b == null) return false;
        if (a is string || a is not IEnumerable enumerableA || b is not IEnumerable enumerableB)
            return Equals(a, b);
        var ea = enumerableA.GetEnumerator();
        var eb = enumerableB.GetEnumerator();
        while (true)
        {
            var hasA = ea.MoveNext();
            var hasB = eb.MoveNext();
            if (hasA != hasB) return false;
            if (!hasA) return true;
            if (!DraftValuesEqual(ea.Current, eb.Current)) return false;
        }
    }
}
