// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace Dapplo.Ini.Attributes;

/// <summary>
/// Provides extra INI-specific metadata for a property on an INI section interface.
/// </summary>
/// <remarks>
/// <para>
/// For most properties the standard .NET attributes are the preferred choice, and the source
/// generator honours them as first-class alternatives:
/// </para>
/// <list type="table">
///   <listheader><term>Prefer this standard attribute…</term><description>…over this <c>[IniValue]</c> property</description></listheader>
///   <item><term><see cref="System.ComponentModel.DefaultValueAttribute"/></term><description><see cref="DefaultValue"/></description></item>
///   <item><term><see cref="System.ComponentModel.DescriptionAttribute"/></term><description><see cref="Description"/></description></item>
///   <item><term><see cref="System.Runtime.Serialization.DataMemberAttribute"/> <c>Name</c></term><description><see cref="KeyName"/></description></item>
///   <item><term><see cref="System.Runtime.Serialization.IgnoreDataMemberAttribute"/></term><description>Full exclusion from all INI operations</description></item>
///   <item><term>Getter-only property <c>{ get; }</c></term><description><see cref="ReadOnly"/></description></item>
/// </list>
/// <para>
/// When <c>[IniValue]</c> and a standard attribute both supply the same piece of
/// information, <c>[IniValue]</c> takes precedence.  Standard attributes are used
/// only when the corresponding <c>[IniValue]</c> property is left at its default value.
/// </para>
/// <para>
/// Use <c>[IniValue]</c> only for the properties that have no standard equivalent:
/// <see cref="Transactional"/>, <see cref="NotifyPropertyChanged"/>, and
/// <see cref="RuntimeOnly"/>.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class IniValueAttribute : Attribute
{
    /// <summary>
    /// The key name used in the INI file. Defaults to the property name.
    /// </summary>
    /// <remarks>
    /// Prefer <c>[DataMember(Name = "key")]</c> from
    /// <see cref="System.Runtime.Serialization"/> as a standard alternative.
    /// </remarks>
    public string? KeyName { get; set; }

    /// <summary>
    /// The default value expressed as a string (will be converted via the registered converter).
    /// </summary>
    /// <remarks>
    /// Prefer <see cref="System.ComponentModel.DefaultValueAttribute"/> as a standard alternative.
    /// It accepts any value type and the source generator converts it to a string internally.
    /// <example>
    /// <code>
    /// [DefaultValue(8080)]
    /// int Port { get; set; }
    /// </code>
    /// </example>
    /// </remarks>
    public string? DefaultValue { get; set; }

    /// <summary>
    /// An optional description / comment written above the key in the INI file.
    /// </summary>
    /// <remarks>
    /// Prefer <see cref="System.ComponentModel.DescriptionAttribute"/> as a standard alternative.
    /// </remarks>
    public string? Description { get; set; }

    /// <summary>
    /// When <c>true</c> the value is never written to the INI file.
    /// </summary>
    /// <remarks>
    /// Prefer a getter-only interface property (<c>{ get; }</c> without a setter) as the
    /// idiomatic C# approach to read-only properties.  The generated implementation class
    /// still exposes a public setter so the framework and concrete-class callers can assign
    /// values programmatically.
    /// <para>
    /// Use <c>[IniValue(ReadOnly = true)]</c> only when you need the setter to remain
    /// accessible through the interface type itself.
    /// </para>
    /// </remarks>
    public bool ReadOnly { get; set; }

    /// <summary>
    /// When <c>true</c> the property participates in transactions (old value returned until <c>Commit</c>).
    /// Requires the containing section interface to also implement <see cref="Interfaces.ITransactional"/>.
    /// </summary>
    /// <remarks>There is no standard .NET equivalent for this property.</remarks>
    public bool Transactional { get; set; }

    /// <summary>
    /// When <c>true</c> setting this property raises <c>INotifyPropertyChanging</c> and
    /// <c>INotifyPropertyChanged</c> events from the generated class.
    /// </summary>
    /// <remarks>There is no standard .NET equivalent for this property.</remarks>
    public bool NotifyPropertyChanged { get; set; }

    /// <summary>
    /// When <c>true</c> the property is never loaded from or saved to the INI file.
    /// The property participates in <see cref="IIniSection.ResetToDefaults"/> (default value
    /// is applied) but its value lives only in memory for the lifetime of the application.
    /// Use this for settings that must be configurable at runtime but must not be persisted.
    /// </summary>
    /// <remarks>
    /// <para>
    /// There is no exact standard .NET equivalent for this property.
    /// </para>
    /// <para>
    /// <see cref="System.Runtime.Serialization.IgnoreDataMemberAttribute"/> is the closest
    /// standard attribute, but it performs a <em>full exclusion</em>: the property is excluded
    /// from all INI operations <em>including</em> <see cref="IIniSection.ResetToDefaults"/>.
    /// Use <c>[IniValue(RuntimeOnly = true)]</c> when the default value should still be
    /// applied on every load / reload cycle.
    /// </para>
    /// </remarks>
    public bool RuntimeOnly { get; set; }

    /// <summary>
    /// When <c>true</c>, an absent or <c>null</c> INI value is represented as an empty value
    /// rather than <c>null</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The exact "empty" representation depends on the property type:
    /// </para>
    /// <list type="table">
    ///   <listheader><term>Property type</term><description>Empty representation</description></listheader>
    ///   <item><term><c>string</c></term><description><see cref="string.Empty"/> (<c>""</c>)</description></item>
    ///   <item><term><c>List&lt;T&gt;</c> / <c>IList&lt;T&gt;</c> / <c>ICollection&lt;T&gt;</c> / <c>IEnumerable&lt;T&gt;</c></term><description>An empty <see cref="System.Collections.Generic.List{T}"/></description></item>
    ///   <item><term><c>T[]</c></term><description>An empty array (<c>new T[0]</c>)</description></item>
    ///   <item><term><c>Dictionary&lt;TKey,TValue&gt;</c> / <c>IDictionary&lt;TKey,TValue&gt;</c></term><description>An empty <see cref="System.Collections.Generic.Dictionary{TKey,TValue}"/></description></item>
    /// </list>
    /// <para>
    /// When <see cref="DefaultValue"/> is also specified, that default takes precedence over
    /// <see cref="EmptyWhenNull"/> for the initial value; <see cref="EmptyWhenNull"/> still
    /// applies when the property key is present in the INI file but has an empty value.
    /// </para>
    /// <para>
    /// This is particularly useful for collection properties where you prefer to receive an
    /// empty collection (and avoid null checks) rather than <c>null</c> when no data is stored.
    /// </para>
    /// </remarks>
    public bool EmptyWhenNull { get; set; }
}
