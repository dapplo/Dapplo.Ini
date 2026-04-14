// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace Dapplo.Ini.Ui.Attributes;

/// <summary>
/// Controls whether a configuration property's UI control is interactive (enabled) or
/// read-only (disabled) based on the value of another <c>bool</c> property on the same
/// section interface.
/// </summary>
/// <remarks>
/// <para>
/// The referenced property (identified by <see cref="ConditionProperty"/>) must be a
/// <c>bool</c> property on the same section interface.  When its value is <c>true</c>
/// the decorated property's control is enabled; when <c>false</c> it is disabled.
/// Set <see cref="Invert"/> to <c>true</c> to reverse this logic.
/// </para>
/// <para>
/// Properties whose values come from a constants file (i.e. <c>IsConstant(key)</c> returns
/// <c>true</c>) are always rendered as disabled regardless of this attribute.
/// </para>
/// <para>
/// For group-level enable/disable control, use
/// <see cref="UiGroupAttribute.EnableConditionProperty"/> instead.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// bool UseProxy { get; set; }
///
/// // Enabled only when UseProxy is true:
/// [UiConditionalEnable(nameof(UseProxy))]
/// string ProxyHost { get; set; }
///
/// // Enabled only when UseProxy is false:
/// [UiConditionalEnable(nameof(UseProxy), Invert = true)]
/// string DirectHostname { get; set; }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class UiConditionalEnableAttribute : Attribute
{
    /// <summary>
    /// Gets the name of the <c>bool</c> property on the same interface that governs
    /// whether the control is enabled.
    /// </summary>
    public string ConditionProperty { get; }

    /// <summary>
    /// Initialises a new instance of <see cref="UiConditionalEnableAttribute"/>.
    /// </summary>
    /// <param name="conditionProperty">
    /// The name of the <c>bool</c> property whose value controls whether the control is
    /// enabled.
    /// </param>
    public UiConditionalEnableAttribute(string conditionProperty)
    {
        ConditionProperty = conditionProperty;
    }

    /// <summary>
    /// When <c>true</c>, the enable logic is inverted: the control is disabled when
    /// <see cref="ConditionProperty"/> is <c>true</c> and enabled when it is
    /// <c>false</c>.
    /// </summary>
    public bool Invert { get; set; }
}
