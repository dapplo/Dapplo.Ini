// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace Dapplo.Ini.Ui.Enums;

/// <summary>
/// Defines when a setting change is applied to the underlying configuration.
/// Used with <see cref="Attributes.UiChangeModeAttribute"/>.
/// </summary>
public enum UiChangeMode
{
    /// <summary>
    /// The setting is applied to the configuration immediately when the user changes the
    /// control value (default behaviour).
    /// </summary>
    Immediate,

    /// <summary>
    /// The setting is held in a pending state and applied only when the user explicitly
    /// confirms (e.g. clicks <em>OK</em> or <em>Apply</em>).
    /// Cancelling or closing the window without confirming will roll back any pending changes
    /// via <see cref="Interfaces.IUiSettingsManager.Rollback"/>.
    /// </summary>
    OnConfirm,
}
