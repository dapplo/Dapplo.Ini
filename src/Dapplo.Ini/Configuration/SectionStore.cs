// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Dapplo.Ini.Interfaces;
#if NET
using System.Diagnostics.CodeAnalysis;
#endif

namespace Dapplo.Ini.Configuration;

/// <summary>
/// Thread-safe, ordered store of the sections registered with an <see cref="IniConfig"/>.
/// </summary>
/// <remarks>
/// Writes replace an immutable array (registration order) and a lookup dictionary as one unit
/// (copy-on-write), so readers such as <see cref="IniConfig.GetSection{T}"/>, auto-save or
/// <see cref="IniConfig.Save"/> never observe a half-updated collection and never need a lock.
/// </remarks>
internal sealed class SectionStore
{
    private sealed class State
    {
        public static readonly State Empty = new(Array.Empty<KeyValuePair<Type, IIniSection>>());

        public State(KeyValuePair<Type, IIniSection>[] entries)
        {
            Entries = entries;
            Values = entries.Select(e => e.Value).ToArray();
            ByType = new Dictionary<Type, IIniSection>(entries.Length);
            foreach (var entry in entries)
                ByType[entry.Key] = entry.Value;
        }

        public readonly KeyValuePair<Type, IIniSection>[] Entries;
        public readonly IIniSection[] Values;
        public readonly Dictionary<Type, IIniSection> ByType;
    }

    private volatile State _state = State.Empty;
    private readonly object _writeLock = new();

    /// <summary>The registered sections, in registration order. The returned list never changes.</summary>
    public IReadOnlyList<IIniSection> Values => _state.Values;

    /// <summary>The number of registered sections.</summary>
    public int Count => _state.Values.Length;

    /// <summary>Looks up the section registered under <paramref name="type"/>.</summary>
    public bool TryGetValue(Type type, out IIniSection section)
        => _state.ByType.TryGetValue(type, out section!);

    /// <summary>Returns <c>true</c> when a section is registered under <paramref name="type"/>.</summary>
    public bool ContainsKey(Type type) => _state.ByType.ContainsKey(type);

    /// <summary>
    /// Returns the registered section whose <see cref="IIniSection.SectionName"/> equals
    /// <paramref name="sectionName"/> (case-insensitive), or <c>null</c>.
    /// </summary>
    public IIniSection? FindByName(string sectionName)
    {
        foreach (var section in _state.Values)
        {
            if (string.Equals(section.SectionName, sectionName, StringComparison.OrdinalIgnoreCase))
                return section;
        }
        return null;
    }

    /// <summary>
    /// Registers <paramref name="section"/> under <paramref name="type"/>. An existing registration for
    /// the same type is replaced in place (its position in the registration order is kept).
    /// </summary>
    public void Set(Type type, IIniSection section)
    {
        lock (_writeLock)
        {
            var entries = _state.Entries;
            var index = Array.FindIndex(entries, e => e.Key == type);
            KeyValuePair<Type, IIniSection>[] updated;
            if (index >= 0)
            {
                updated = (KeyValuePair<Type, IIniSection>[])entries.Clone();
                updated[index] = new KeyValuePair<Type, IIniSection>(type, section);
            }
            else
            {
                updated = new KeyValuePair<Type, IIniSection>[entries.Length + 1];
                Array.Copy(entries, updated, entries.Length);
                updated[entries.Length] = new KeyValuePair<Type, IIniSection>(type, section);
            }
            _state = new State(updated);
        }
    }

    /// <summary>
    /// Determines the type a section is registered under.
    /// </summary>
    /// <remarks>
    /// When <paramref name="declaredType"/> is an interface it is used as-is. Otherwise the most
    /// derived <see cref="IIniSection"/>-derived interface implemented by <paramref name="runtimeType"/>
    /// is used, so that <c>ISettings : IBaseSettings : IIniSection</c> registers under <c>ISettings</c>
    /// regardless of the order in which reflection returns the interfaces. When several unrelated
    /// section interfaces are implemented, the choice is made deterministically by full name; pass the
    /// interface type explicitly to avoid the ambiguity.
    /// </remarks>
#if NET
    [RequiresUnreferencedCode("Inspects implemented interfaces at runtime to infer the section type.")]
#endif
    public static Type ResolveKeyType(Type declaredType, Type runtimeType)
    {
        if (declaredType.IsInterface)
            return declaredType;

        var candidates = runtimeType.GetInterfaces()
            .Where(i => typeof(IIniSection).IsAssignableFrom(i) && i != typeof(IIniSection))
            .ToList();
        if (candidates.Count == 0)
            return runtimeType;

        var mostDerived = candidates
            .Where(c => !candidates.Any(other => other != c && c.IsAssignableFrom(other)))
            .OrderBy(c => c.FullName, StringComparer.Ordinal)
            .ToList();
        return mostDerived.Count > 0 ? mostDerived[0] : candidates[0];
    }
}
