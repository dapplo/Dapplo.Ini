// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.ComponentModel;
using Dapplo.Ini.Attributes;
using Dapplo.Ini.Interfaces;
using Dapplo.Ini.Ui.Attributes;
using Dapplo.Ini.Ui.Enums;

namespace Dapplo.Ini.Ui.DemoApp.Configuration;

/// <summary>
/// Visual appearance settings — theme, colour accent, and typography.
/// </summary>
/// <remarks>
/// This section demonstrates conditional enable: the <c>FontSize</c> slider becomes
/// read-only when <c>DarkMode</c> is enabled (inverted condition,
/// <c>Invert = true</c>).
/// </remarks>
[IniSection("Appearance")]
[UiPage(Title = "Appearance", Order = 5)]
public interface IAppearanceSettings : IIniSection, INotifyPropertyChanged
{
    /// <summary>Enable the dark colour theme.</summary>
    [DefaultValue(false)]
    [UiLabelKey("appearance_dark_mode")]
    bool DarkMode { get; set; }

    /// <summary>Accent colour applied to buttons and highlights.  Auto-inferred DropDown (enum).</summary>
    [DefaultValue(AccentColorOption.Blue)]
    [UiGroup("Theme", Order = 10)]
    [UiLabelKey("appearance_accent")]
    AccentColorOption AccentColor { get; set; }

    /// <summary>
    /// Base font size in points.  Disabled when <c>DarkMode</c> is active (inverted enable condition).
    /// </summary>
    [DefaultValue(12)]
    [UiControl(UiControlType.Slider, Minimum = 8, Maximum = 32, Unit = "pt")]
    [UiGroup("Theme")]
    [UiConditionalEnable(nameof(DarkMode), Invert = true)]
    [UiLabelKey("appearance_font_size")]
    int FontSize { get; set; }
}
