// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Dapplo.Ini.Ui.Enums;

namespace Dapplo.Ini.Ui.Attributes;

/// <summary>
/// Specifies the UI control type used to render a configuration property in the settings UI.
/// </summary>
/// <remarks>
/// <para>
/// When this attribute is omitted the UI framework infers the best control type from the
/// property's .NET type:
/// </para>
/// <list type="table">
///   <listheader><term>Property type</term><description>Inferred control</description></listheader>
///   <item><term><c>bool</c></term><description><see cref="UiControlType.CheckBox"/></description></item>
///   <item><term><c>enum</c></term><description><see cref="UiControlType.DropDown"/></description></item>
///   <item><term><c>int</c>, <c>long</c>, <c>double</c>, …</term><description><see cref="UiControlType.UpDown"/></description></item>
///   <item><term><c>string</c></term><description><see cref="UiControlType.TextBox"/></description></item>
/// </list>
/// <para>
/// Use this attribute only when the inferred control is not appropriate, for example to
/// render an integer as a <see cref="UiControlType.Slider"/> instead of an
/// <see cref="UiControlType.UpDown"/>.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// [UiControl(UiControlType.Slider, Minimum = 0, Maximum = 100)]
/// int Volume { get; set; }
///
/// [UiControl(UiControlType.DropDown, ItemsSourceProperty = nameof(GetAvailableThemes))]
/// string Theme { get; set; }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class UiControlAttribute : Attribute
{
    /// <summary>
    /// Gets the UI control type for this property.
    /// </summary>
    public UiControlType ControlType { get; }

    /// <summary>
    /// Initialises a new instance of <see cref="UiControlAttribute"/>.
    /// </summary>
    /// <param name="controlType">The UI control type to use for this property.</param>
    public UiControlAttribute(UiControlType controlType)
    {
        ControlType = controlType;
    }

    /// <summary>
    /// The minimum value for <see cref="UiControlType.Slider"/> and
    /// <see cref="UiControlType.UpDown"/> controls.
    /// Ignored for other control types.
    /// </summary>
    public double Minimum { get; set; } = double.MinValue;

    /// <summary>
    /// The maximum value for <see cref="UiControlType.Slider"/> and
    /// <see cref="UiControlType.UpDown"/> controls.
    /// Ignored for other control types.
    /// </summary>
    public double Maximum { get; set; } = double.MaxValue;

    /// <summary>
    /// The step increment for <see cref="UiControlType.UpDown"/> controls.
    /// Ignored for other control types.
    /// </summary>
    public double Increment { get; set; } = 1.0;

    /// <summary>
    /// The number of decimal places shown for <see cref="UiControlType.UpDown"/> and
    /// <see cref="UiControlType.Slider"/> controls.
    /// </summary>
    public int DecimalPlaces { get; set; } = 0;

    /// <summary>
    /// The name of a property or parameterless method on the same section interface that
    /// returns an <c>IEnumerable&lt;string&gt;</c> or <c>IEnumerable&lt;T&gt;</c> of items
    /// for a <see cref="UiControlType.DropDown"/> control.
    /// When specified this takes precedence over auto-generating items from an <c>enum</c> type.
    /// </summary>
    public string? ItemsSourceProperty { get; set; }

    /// <summary>
    /// When <c>true</c>, the control's label is hidden and only the control itself is shown.
    /// Useful for controls that are self-descriptive (e.g. a <see cref="UiControlType.CheckBox"/>
    /// whose text already acts as the label).
    /// </summary>
    public bool HideLabel { get; set; }

    /// <summary>
    /// An optional unit label appended next to the control (e.g. <c>"px"</c>, <c>"ms"</c>).
    /// </summary>
    public string? Unit { get; set; }

    /// <summary>
    /// An optional watermark / placeholder text shown inside a
    /// <see cref="UiControlType.TextBox"/> or <see cref="UiControlType.MultilineTextBox"/>
    /// when the field is empty.
    /// </summary>
    public string? Placeholder { get; set; }
}
