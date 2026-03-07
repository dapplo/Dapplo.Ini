// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace Dapplo.Ini.Config.Attributes;

/// <summary>
/// Marks an interface as an INI section. The source generator will create a concrete implementation.
/// </summary>
[AttributeUsage(AttributeTargets.Interface, AllowMultiple = false, Inherited = false)]
public sealed class IniSectionAttribute : Attribute
{
    /// <summary>
    /// The name of the section in the INI file. If not specified, the interface name (without leading 'I') is used.
    /// </summary>
    public string? SectionName { get; }

    /// <summary>
    /// Optional description / comment written above the section header in the INI file.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Initialises a new instance of <see cref="IniSectionAttribute"/>.
    /// </summary>
    /// <param name="sectionName">The INI section name. Defaults to the interface name without the leading 'I'.</param>
    public IniSectionAttribute(string? sectionName = null)
    {
        SectionName = sectionName;
    }
}
