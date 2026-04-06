// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace Dapplo.Ini.Interfaces;

/// <summary>
/// Base interface that all generated INI-section classes implement.
/// Provides access to raw key/value data and meta-information.
/// </summary>
public interface IIniSection
{
    /// <summary>Gets the name of the section as it appears in the INI file.</summary>
    string SectionName { get; }

    /// <summary>
    /// Returns the raw string value stored for <paramref name="key"/> in this section,
    /// or <c>null</c> when the key is not present.
    /// </summary>
    string? GetRawValue(string key);

    /// <summary>
    /// Stores a raw string value for <paramref name="key"/> in this section.
    /// The property whose key matches will be updated via its converter.
    /// </summary>
    void SetRawValue(string key, string? value);

    /// <summary>
    /// Resets all properties to their default values.
    /// </summary>
    void ResetToDefaults();

    /// <summary>
    /// Gets a value indicating whether this section has unsaved changes since the last
    /// <see cref="IniConfig.Save"/> or <see cref="IniConfig.Reload"/>.
    /// </summary>
    bool HasChanges { get; }

    /// <summary>
    /// Marks this section as having unsaved changes.
    /// <para>
    /// Call this method after mutating a collection property in-place (for example
    /// <c>section.Tags.Add("new-tag")</c>) so that the auto-save timer and
    /// <see cref="IniConfig.HasPendingChanges"/> correctly detect the modification.
    /// Property setters call this automatically; only direct collection mutations
    /// require an explicit call.
    /// </para>
    /// </summary>
    void MarkAsDirty();

    /// <summary>
    /// Returns <c>true</c> when the value for <paramref name="key"/> was loaded from a
    /// constants file (registered via <c>IniConfigBuilder.AddConstantsFile</c>) and is
    /// therefore protected against modification.
    /// Attempting to change a constant key via its property setter or
    /// <see cref="SetRawValue"/> throws <see cref="AccessViolationException"/>.
    /// Use this method in UI code to disable the corresponding input control.
    /// </summary>
    /// <param name="key">
    /// The key name as it appears in the INI file (case-insensitive).
    /// Typically the property name, unless overridden via <c>[IniValue(KeyName="...")]</c>.
    /// </param>
    bool IsConstant(string key);

    /// <summary>
    /// Returns the INI key names of all properties declared on this section.
    /// For source-generated sections this reflects the compile-time property list;
    /// for non-generated sections it reflects the keys currently held in the raw store.
    /// </summary>
    IEnumerable<string> GetKeys();

    /// <summary>
    /// Returns the .NET <see cref="Type"/> of the property identified by <paramref name="key"/>,
    /// or <c>null</c> when the key is not a known declared property.
    /// Source-generated sections return the exact property type; non-generated sections
    /// always return <c>null</c>.
    /// </summary>
    /// <param name="key">The INI key name (case-insensitive).</param>
    Type? GetPropertyType(string key);
}
