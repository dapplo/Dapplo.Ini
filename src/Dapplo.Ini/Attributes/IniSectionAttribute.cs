// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Dapplo.Ini.Parsing;

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
    /// When <c>true</c>, values for all properties in this section are never loaded from
    /// default files (registered via <c>IniConfigBuilder.AddDefaultsFile</c>).
    /// The property values will only be set from the main user INI file or constant files.
    /// </summary>
    public bool IgnoreDefaults { get; set; }

    /// <summary>
    /// When <c>true</c>, values for all properties in this section are never loaded from
    /// constant files (registered via <c>IniConfigBuilder.AddConstantsFile</c>).
    /// The property values will never be locked by an administrator constants file.
    /// </summary>
    public bool IgnoreConstants { get; set; }

    /// <summary>
    /// When <c>true</c>, every reference-type property in this section (strings, lists, arrays,
    /// dictionaries) returns an empty value instead of <c>null</c> when no INI key is present
    /// and the property has no explicit <see cref="IniValueAttribute.DefaultValue"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is a convenient shorthand for applying
    /// <c>[IniValue(EmptyWhenNull = true)]</c> to every qualifying property in the section.
    /// It saves you from having to annotate each property individually when the entire section
    /// should prefer empty-over-null semantics.
    /// </para>
    /// <para>
    /// The empty representation depends on the property type:
    /// </para>
    /// <list type="table">
    ///   <listheader><term>Property type</term><description>Empty representation</description></listheader>
    ///   <item><term><c>string</c></term><description><see cref="string.Empty"/></description></item>
    ///   <item><term><c>List&lt;T&gt;</c> / <c>IList&lt;T&gt;</c> / collection interfaces</term><description>Empty <see cref="System.Collections.Generic.List{T}"/></description></item>
    ///   <item><term><c>T[]</c></term><description>Empty array</description></item>
    ///   <item><term><c>Dictionary&lt;K,V&gt;</c> / <c>IDictionary&lt;K,V&gt;</c></term><description>Empty <see cref="System.Collections.Generic.Dictionary{TKey,TValue}"/></description></item>
    /// </list>
    /// <para>
    /// Value-type properties (e.g. <c>int</c>, <c>bool</c>, <c>double</c>) are never affected —
    /// they always use <c>default(T)</c> or their <see cref="IniValueAttribute.DefaultValue"/> as usual.
    /// </para>
    /// <para>
    /// To apply empty-over-null behaviour to <em>all</em> sections, use
    /// <see cref="IniConfigBuilder.EmptyWhenNull"/> on the builder instead.
    /// </para>
    /// </remarks>
    public bool EmptyWhenNull { get; set; }

    /// <summary>
    /// Optional section-level override for value quoting when writing.
    /// </summary>
    public IniValueQuoteStyle QuoteValues { get; set; } = IniValueQuoteStyle.Default;

    /// <summary>
    /// Optional section-level override for escape-sequence output when writing.
    /// </summary>
    public IniBooleanOption EscapeSequences { get; set; } = IniBooleanOption.Default;

    /// <summary>
    /// Optional section-level override for writing comments.
    /// </summary>
    public IniBooleanOption WriteComments { get; set; } = IniBooleanOption.Default;

    /// <summary>
    /// Initialises a new instance of <see cref="IniSectionAttribute"/>.
    /// </summary>
    /// <param name="sectionName">The INI section name. Defaults to the interface name without the leading 'I'.</param>
    public IniSectionAttribute(string? sectionName = null)
    {
        SectionName = sectionName;
    }
}
