// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace Dapplo.Ini.Config.Parsing;

/// <summary>
/// Represents a single [Section] inside an INI file, containing its key/value entries.
/// </summary>
public sealed class IniSection
{
    private readonly Dictionary<string, IniEntry> _entries =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The section name (without brackets).</summary>
    public string Name { get; }

    /// <summary>Comment lines that appeared above the section header.</summary>
    public IReadOnlyList<string> Comments { get; }

    /// <summary>The entries in declaration order (preserves file order).</summary>
    public IReadOnlyList<IniEntry> Entries => _entriesOrdered;

    private readonly List<IniEntry> _entriesOrdered = new();

    public IniSection(string name, IReadOnlyList<string> comments)
    {
        Name = name;
        Comments = comments;
    }

    /// <summary>Adds or replaces an entry.</summary>
    public void SetEntry(IniEntry entry)
    {
        if (_entries.TryGetValue(entry.Key, out var existing))
        {
            existing.Value = entry.Value;
        }
        else
        {
            _entries[entry.Key] = entry;
            _entriesOrdered.Add(entry);
        }
    }

    /// <summary>Returns the entry for <paramref name="key"/>, or <c>null</c>.</summary>
    public IniEntry? GetEntry(string key)
        => _entries.TryGetValue(key, out var e) ? e : null;

    /// <summary>Returns the raw value for <paramref name="key"/>, or <c>null</c>.</summary>
    public string? GetValue(string key)
        => _entries.TryGetValue(key, out var e) ? e.Value : null;

    /// <summary>Sets (or adds) a raw value for <paramref name="key"/>.</summary>
    public void SetValue(string key, string? value)
    {
        if (_entries.TryGetValue(key, out var e))
        {
            e.Value = value;
        }
        else
        {
            var entry = new IniEntry(key, value, Array.Empty<string>());
            _entries[key] = entry;
            _entriesOrdered.Add(entry);
        }
    }

    /// <summary>Returns <c>true</c> when a key with this name exists.</summary>
    public bool ContainsKey(string key) => _entries.ContainsKey(key);
}
