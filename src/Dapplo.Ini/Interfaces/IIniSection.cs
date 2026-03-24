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
}
