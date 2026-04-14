// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace Dapplo.Ini.Ui.Attributes;

/// <summary>
/// Controls the visibility of a configuration property's UI control based on the value of
/// another <c>bool</c> property on the same section interface.
/// </summary>
/// <remarks>
/// <para>
/// The referenced property (identified by <see cref="ConditionProperty"/>) must be a
/// <c>bool</c> property on the same section interface.  When its value is <c>true</c>
/// the decorated property is visible; when <c>false</c> it is hidden.
/// Set <see cref="Invert"/> to <c>true</c> to reverse this logic.
/// </para>
/// <para>
/// For group-level visibility control, use
/// <see cref="UiGroupAttribute.VisibilityConditionProperty"/> instead.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// bool UseProxy { get; set; }
///
/// // Shown only when UseProxy is true:
/// [UiConditionalVisibility(nameof(UseProxy))]
/// string ProxyHost { get; set; }
///
/// // Shown only when UseProxy is false (inverted condition):
/// [UiConditionalVisibility(nameof(UseProxy), Invert = true)]
/// string DirectConnectionHint { get; set; }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class UiConditionalVisibilityAttribute : Attribute
{
    /// <summary>
    /// Gets the name of the <c>bool</c> property on the same interface that governs visibility.
    /// </summary>
    public string ConditionProperty { get; }

    /// <summary>
    /// Initialises a new instance of <see cref="UiConditionalVisibilityAttribute"/>.
    /// </summary>
    /// <param name="conditionProperty">
    /// The name of the <c>bool</c> property whose value controls visibility.
    /// </param>
    public UiConditionalVisibilityAttribute(string conditionProperty)
    {
        ConditionProperty = conditionProperty;
    }

    /// <summary>
    /// When <c>true</c>, the visibility logic is inverted: the decorated property is hidden
    /// when <paramref name="conditionProperty"/> is <c>true</c> and visible when it is
    /// <c>false</c>.
    /// </summary>
    public bool Invert { get; set; }
}
