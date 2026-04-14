// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace Dapplo.Ini.Ui.Attributes;

/// <summary>
/// Groups one or more configuration properties under a named heading in the settings UI.
/// </summary>
/// <remarks>
/// <para>
/// Properties that share the same <see cref="GroupName"/> are rendered together inside a
/// labelled panel or group-box.  Groups are ordered by <see cref="Order"/> within their
/// containing page.
/// </para>
/// <para>
/// A group can be made to appear or disappear based on another property's value by
/// setting <see cref="VisibilityConditionProperty"/> or
/// <see cref="EnableConditionProperty"/>. For per-property conditions prefer
/// <see cref="UiConditionalVisibilityAttribute"/> and
/// <see cref="UiConditionalEnableAttribute"/> instead.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// [UiGroup("Proxy", Order = 10)]
/// bool UseProxy { get; set; }
///
/// [UiGroup("Proxy")]
/// string ProxyHost { get; set; }
///
/// [UiGroup("Proxy")]
/// int ProxyPort { get; set; }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class UiGroupAttribute : Attribute
{
    /// <summary>
    /// Gets the group name.  All properties annotated with the same group name on the same
    /// section interface are rendered together.
    /// </summary>
    public string GroupName { get; }

    /// <summary>
    /// Initialises a new instance of <see cref="UiGroupAttribute"/>.
    /// </summary>
    /// <param name="groupName">
    /// The display name of the group.  Serves as both the identifier and the default header
    /// label; override the label via i18n using a <see cref="UiLabelKeyAttribute"/> on the
    /// first property in the group.
    /// </param>
    public UiGroupAttribute(string groupName)
    {
        GroupName = groupName;
    }

    /// <summary>
    /// Controls the order in which groups are displayed within their containing page.
    /// Lower values appear first.  Defaults to <c>0</c>.
    /// </summary>
    public int Order { get; set; }

    /// <summary>
    /// The name of a <c>bool</c> property on the same section interface whose value
    /// controls the visibility of the entire group.
    /// When the referenced property is <c>false</c> the whole group is hidden.
    /// Set <see cref="InvertVisibilityCondition"/> to <c>true</c> to reverse the logic.
    /// </summary>
    public string? VisibilityConditionProperty { get; set; }

    /// <summary>
    /// When <c>true</c>, inverts the <see cref="VisibilityConditionProperty"/> logic
    /// so the group is visible when the referenced property is <c>false</c>.
    /// </summary>
    public bool InvertVisibilityCondition { get; set; }

    /// <summary>
    /// The name of a <c>bool</c> property on the same section interface whose value
    /// controls whether the controls in this group are interactive.
    /// When the referenced property is <c>false</c> all controls in the group are disabled.
    /// Set <see cref="InvertEnableCondition"/> to <c>true</c> to reverse the logic.
    /// </summary>
    public string? EnableConditionProperty { get; set; }

    /// <summary>
    /// When <c>true</c>, inverts the <see cref="EnableConditionProperty"/> logic
    /// so the group is enabled when the referenced property is <c>false</c>.
    /// </summary>
    public bool InvertEnableCondition { get; set; }
}
