// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace Dapplo.Ini.Attributes;

/// <summary>
/// Marks an interface as an INI section and allows customising the section name and
/// description.  The source generator will create a concrete implementation.
/// </summary>
/// <remarks>
/// <para>
/// This attribute is <b>optional</b>.  The source generator automatically
/// processes any interface that extends <see cref="Interfaces.IIniSection"/>, even
/// without <c>[IniSection]</c>.
/// </para>
/// <para>
/// When <c>[IniSection]</c> is omitted:
/// </para>
/// <list type="bullet">
///   <item>The section name is derived from the interface name by stripping a
///   leading <c>I</c> (e.g. <c>IAppSettings</c> → <c>[AppSettings]</c>).</item>
///   <item>Use <see cref="System.ComponentModel.DescriptionAttribute"/> on the
///   interface to set the section comment without adding <c>[IniSection]</c>.</item>
/// </list>
/// <para>
/// Use <c>[IniSection]</c> only when you need to override the default section name
/// or when <c>[Description]</c> is not available.
/// </para>
/// <example>
/// <code>
/// // Minimal — no [IniSection] needed when the default name is acceptable.
/// [Description("App settings")]
/// public interface IAppSettings : IIniSection
/// {
///     [DefaultValue("MyApp")]
///     string? AppName { get; set; }
/// }
///
/// // Explicit section name — use [IniSection] only when you need a custom name.
/// [IniSection("app")]
/// public interface IAppSettings : IIniSection { /* … */ }
/// </code>
/// </example>
/// </remarks>
[AttributeUsage(AttributeTargets.Interface, AllowMultiple = false, Inherited = false)]
public sealed class IniSectionAttribute : Attribute
{
    /// <summary>
    /// The name of the section in the INI file. If not specified, the interface name
    /// (without leading 'I') is used.
    /// </summary>
    public string? SectionName { get; }

    /// <summary>
    /// Optional description / comment written above the section header in the INI file.
    /// </summary>
    /// <remarks>
    /// Prefer <see cref="System.ComponentModel.DescriptionAttribute"/> as a standard
    /// alternative so that <c>[IniSection]</c> can be omitted entirely.
    /// </remarks>
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
