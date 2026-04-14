// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace Dapplo.Ini.Ui.Enums;

/// <summary>
/// Specifies how pages (tabs) are arranged in the settings window.
/// Used with <see cref="Attributes.UiLayoutAttribute"/>.
/// </summary>
public enum UiTabLayout
{
    /// <summary>
    /// Tabs are rendered horizontally across the top of the settings window (default).
    /// </summary>
    HorizontalTabs,

    /// <summary>
    /// Tabs are rendered as a vertical list on the left side of the settings window,
    /// similar to the Visual Studio Options dialog.
    /// </summary>
    VerticalTabs,

    /// <summary>
    /// All sections are shown on a single scrollable page without tabs.
    /// </summary>
    SinglePage,
}
