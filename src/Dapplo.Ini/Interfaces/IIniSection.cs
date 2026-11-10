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
    /// Returns the current value for <paramref name="key"/> as an untyped <see cref="object"/>,
    /// exactly as the property getter would return it (including any default configured via
    /// <c>[IniValue(DefaultValue = "...")]</c>).
    /// <para>
    /// Use this overload when iterating over properties generically and the .NET type of
    /// each property is not known at compile time. When the type <em>is</em> known, prefer
    /// <see cref="GetValue{T}(string)"/> which avoids the boxing overhead for value types.
    /// </para>
    /// <para>
    /// For dictionary properties, the entire dictionary object is returned; use
    /// <see cref="GetRawValue"/> with the sub-key notation (<c>"PropertyName.subkey"</c>)
    /// to retrieve individual entries.
    /// </para>
    /// </summary>
    /// <param name="key">The INI key name (case-insensitive).</param>
    /// <returns>
    /// The value as <see cref="object"/>, or <c>null</c> when the key is not found.
    /// </returns>
    object? GetValue(string key);

    /// <summary>
    /// Returns the current typed value for <paramref name="key"/>, exactly as the property
    /// getter would return it (including any default configured via
    /// <c>[IniValue(DefaultValue = "...")]</c>).
    /// <para>
    /// For dictionary properties, the entire dictionary object is returned; use
    /// <see cref="GetRawValue"/> with the sub-key notation (<c>"PropertyName.subkey"</c>)
    /// to retrieve individual entries.
    /// </para>
    /// </summary>
    /// <typeparam name="T">The expected .NET type of the property.</typeparam>
    /// <param name="key">The INI key name (case-insensitive).</param>
    /// <returns>
    /// The typed value, or <c>default(<typeparamref name="T"/>)</c> when the key is not
    /// found or cannot be cast to <typeparamref name="T"/>.
    /// </returns>
    T? GetValue<T>(string key);

    /// <summary>
    /// Stores a raw string value for <paramref name="key"/> in this section.
    /// The property whose key matches will be updated via its converter.
    /// </summary>
    void SetRawValue(string key, string? value);

    /// <summary>
    /// Resets all properties to their compiled default values. No key holds a user value afterwards, so the
    /// next save removes the values from the user file and the next load applies the defaults files again.
    /// </summary>
    void ResetToDefaults();

    /// <summary>
    /// Returns <c>true</c> when <paramref name="key"/> holds a <em>user value</em>: a value read from the user
    /// file or set by the application (property setter, <see cref="SetRawValue"/>) after the load.
    /// Only user values are written to the user file. Keys whose value comes from the compiled default,
    /// a defaults file, a constants file or a value source are inherited and not written, so a later change
    /// of that layer reaches the user.
    /// </summary>
    /// <param name="key">The key of a property, or of an entry <c>Property.subkey</c> of a sub-key dictionary.</param>
    bool IsUserValue(string key);

    /// <summary>
    /// Turns the value of <paramref name="key"/> back into an inherited value: the property gets the value of
    /// the defaults file, or else its compiled default, and the key is no longer written to the user file.
    /// </summary>
    /// <param name="key">The key of a property; for a sub-key dictionary the whole dictionary is reset.</param>
    /// <exception cref="AccessViolationException">The key is protected by a constants file.</exception>
    void ResetKey(string key);

    /// <summary>
    /// Returns where the current value of <paramref name="key"/> came from: the compiled default, a defaults file,
    /// the user file (or the application), a constants file or a value source, with the file path or the source
    /// and its layer.
    /// </summary>
    /// <param name="key">The key of a property, or of an entry <c>Property.subkey</c> of a sub-key dictionary.</param>
    Configuration.ValueOrigin GetValueOrigin(string key);

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
