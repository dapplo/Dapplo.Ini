// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace Dapplo.Ini.Parsing;

/// <summary>
/// Represents the full contents of a parsed INI file, preserving sections and their entries.
/// </summary>
public sealed class IniFile
{
    private readonly Dictionary<string, IniSection> _sections =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly List<IniSection> _sectionsOrdered = new();

    /// <summary>All sections, in file order.</summary>
    public IReadOnlyList<IniSection> Sections => _sectionsOrdered;

    /// <summary>Returns the section with the given <paramref name="name"/>, or <c>null</c>.</summary>
    public IniSection? GetSection(string name)
        => _sections.TryGetValue(name, out var s) ? s : null;

    /// <summary>Gets or creates a section with the given <paramref name="name"/>.</summary>
    public IniSection GetOrAddSection(string name)
    {
        if (!_sections.TryGetValue(name, out var section))
        {
            section = new IniSection(name, Array.Empty<string>());
            _sections[name] = section;
            _sectionsOrdered.Add(section);
        }
        return section;
    }

    /// <summary>Adds a <see cref="IniSection"/> (replaces any existing section with the same name).</summary>
    public void AddSection(IniSection section)
    {
        if (_sections.ContainsKey(section.Name))
        {
            // Replace in ordered list
            var idx = _sectionsOrdered.FindIndex(s =>
                string.Equals(s.Name, section.Name, StringComparison.OrdinalIgnoreCase));
            if (idx >= 0) _sectionsOrdered[idx] = section;
        }
        else
        {
            _sectionsOrdered.Add(section);
        }
        _sections[section.Name] = section;
    }

    /// <summary>
    /// Inserts a section at position 0, making it the first section in the file.
    /// If a section with the same name already exists it is removed from its current position
    /// and re-inserted at index 0.
    /// </summary>
    public void PrependSection(IniSection section)
    {
        if (_sections.ContainsKey(section.Name))
        {
            var idx = _sectionsOrdered.FindIndex(s =>
                string.Equals(s.Name, section.Name, StringComparison.OrdinalIgnoreCase));
            if (idx >= 0) _sectionsOrdered.RemoveAt(idx);
        }
        _sectionsOrdered.Insert(0, section);
        _sections[section.Name] = section;
    }
}
