// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace Dapplo.Ini.Ui.Interfaces;

/// <summary>
/// Optional interface that a settings section partial class can implement to receive
/// callbacks when UI control events occur during an active settings session.
/// </summary>
/// <remarks>
/// <para>
/// The UI framework calls these methods on the <em>concrete implementation</em> of the
/// section, so they should be implemented in a <c>partial class</c> next to the generated
/// class.
/// </para>
/// <para>
/// All methods have no-op default implementations so that sections only need to override
/// the events they care about.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// // Partial class alongside the generated IGeneralSettings implementation:
/// public partial class GeneralSettingsBase : IUiControlEvents
/// {
///     public void OnPropertyChanged(string propertyName, object? oldValue, object? newValue)
///     {
///         if (propertyName == nameof(IGeneralSettings.EnableProxy))
///             // React to the checkbox change …
///     }
/// }
/// </code>
/// </example>
public interface IUiControlEvents
{
    /// <summary>
    /// Called when the user changes the value of a control in the settings UI.
    /// </summary>
    /// <param name="propertyName">The name of the property that was changed.</param>
    /// <param name="oldValue">The value before the change.</param>
    /// <param name="newValue">The new value after the change.</param>
    void OnPropertyChanged(string propertyName, object? oldValue, object? newValue)
    {
        // Default: no-op
    }

    /// <summary>
    /// Called when a control in the settings UI receives focus (e.g. the user clicks into
    /// a text box or selects a control via keyboard navigation).
    /// </summary>
    /// <param name="propertyName">The name of the property whose control received focus.</param>
    void OnControlFocused(string propertyName)
    {
        // Default: no-op
    }

    /// <summary>
    /// Called when a control in the settings UI loses focus.
    /// </summary>
    /// <param name="propertyName">The name of the property whose control lost focus.</param>
    void OnControlBlurred(string propertyName)
    {
        // Default: no-op
    }

    /// <summary>
    /// Called when the user clicks a <see cref="Enums.UiControlType.CheckBox"/> or
    /// activates a <see cref="Enums.UiControlType.RadioButton"/> control.
    /// </summary>
    /// <param name="propertyName">The name of the property whose control was clicked.</param>
    /// <param name="newValue">The new <c>bool</c> or enum value after the click.</param>
    void OnControlClicked(string propertyName, object? newValue)
    {
        // Default: no-op
    }
}
