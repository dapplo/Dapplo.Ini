// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace Dapplo.Ini.Ui.Enums;

/// <summary>
/// Specifies the type of UI control rendered for a configuration property.
/// Used with <see cref="Attributes.UiControlAttribute"/>.
/// </summary>
public enum UiControlType
{
    /// <summary>
    /// A single-line text input field.
    /// Suitable for <c>string</c> properties.
    /// </summary>
    TextBox,

    /// <summary>
    /// A boolean toggle rendered as a checkbox.
    /// Suitable for <c>bool</c> properties.
    /// </summary>
    CheckBox,

    /// <summary>
    /// A ranged numeric value rendered as a horizontal slider.
    /// Use <see cref="Attributes.UiControlAttribute.Minimum"/> and
    /// <see cref="Attributes.UiControlAttribute.Maximum"/> to configure the range.
    /// Suitable for numeric properties (<c>int</c>, <c>double</c>, etc.).
    /// </summary>
    Slider,

    /// <summary>
    /// A fixed set of options rendered as a drop-down (combo-box) list.
    /// When the property type is an <c>enum</c>, the options are derived automatically.
    /// Use <see cref="Attributes.UiControlAttribute.ItemsSourceProperty"/> to specify a
    /// dynamic list from a method or property on the section.
    /// </summary>
    DropDown,

    /// <summary>
    /// A numeric spin-box (up/down arrows with a text field).
    /// Use <see cref="Attributes.UiControlAttribute.Minimum"/> and
    /// <see cref="Attributes.UiControlAttribute.Maximum"/> to configure the allowed range.
    /// Use <see cref="Attributes.UiControlAttribute.Increment"/> for the step size.
    /// Suitable for numeric properties (<c>int</c>, <c>double</c>, etc.).
    /// </summary>
    UpDown,

    /// <summary>
    /// One option from a mutually-exclusive group rendered as a radio button.
    /// All properties sharing the same <see cref="Attributes.UiGroupAttribute.GroupName"/>
    /// and this control type are rendered together as a radio-button group.
    /// </summary>
    RadioButton,

    /// <summary>
    /// A multi-line text input field.
    /// Suitable for <c>string</c> properties that may span multiple lines.
    /// </summary>
    MultilineTextBox,

    /// <summary>
    /// A colour-picker control.
    /// Suitable for properties that represent a colour (e.g. <c>System.Drawing.Color</c>
    /// or a hex string).
    /// </summary>
    ColorPicker,

    /// <summary>
    /// A file-path selection control (text field + browse button).
    /// Suitable for <c>string</c> properties holding file system paths.
    /// </summary>
    FilePicker,

    /// <summary>
    /// A folder-path selection control (text field + browse button).
    /// Suitable for <c>string</c> properties holding directory paths.
    /// </summary>
    FolderPicker,
}
