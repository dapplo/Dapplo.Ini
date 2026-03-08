// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace Dapplo.IniConfig.Interfaces;

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
}
