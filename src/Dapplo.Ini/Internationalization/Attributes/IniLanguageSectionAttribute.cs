// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace Dapplo.Ini.Internationalization.Attributes;

/// <summary>
/// Marks an interface as a language section. The source generator creates a concrete
/// implementation where each <c>string</c> property returns the translated value
/// (or <c>###PropertyName###</c> when the key is missing from the loaded language file).
/// </summary>
/// <remarks>
/// <para>
/// File naming and section routing convention:
/// </para>
/// <list type="bullet">
///   <item>No <paramref name="sectionName"/>: reads from <c>{basename}.{ietf}.ini</c> (all keys).</item>
///   <item>
///     With <paramref name="sectionName"/> = <c>"core"</c>:
///     first tries <c>{basename}.core.{ietf}.ini</c> (all keys);
///     if not found, reads the <c>[core]</c> section from <c>{basename}.{ietf}.ini</c>.
///   </item>
/// </list>
/// <para>
/// This allows module sections to be placed either in their own dedicated file
/// or inside the main application file under a matching <c>[sectionName]</c> header.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Interface, AllowMultiple = false, Inherited = false)]
public sealed class IniLanguageSectionAttribute : Attribute
{
    /// <summary>
    /// Optional section name used for file naming and section filtering:
    /// <list type="bullet">
    ///   <item>Dedicated file: <c>{basename}.{sectionName}.{ietf}.ini</c></item>
    ///   <item>Section in main file: <c>[sectionName]</c> within <c>{basename}.{ietf}.ini</c></item>
    /// </list>
    /// When <c>null</c> the file pattern <c>{basename}.{ietf}.ini</c> is used and all keys are read.
    /// </summary>
    public string? SectionName { get; }

    /// <summary>
    /// Obsolete: use <see cref="SectionName"/> instead.
    /// </summary>
    [Obsolete("Use SectionName instead.")]
    public string? ModuleName => SectionName;

    /// <summary>
    /// Initialises a new instance of <see cref="IniLanguageSectionAttribute"/>.
    /// </summary>
    /// <param name="sectionName">
    /// Optional section name for file naming and section routing.
    /// </param>
    public IniLanguageSectionAttribute(string? sectionName = null)
    {
        SectionName = sectionName;
    }
}

