// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Dapplo.Ini.Attributes;
using Dapplo.Ini.Interfaces;
using Dapplo.Ini.Ui.Attributes;

namespace Dapplo.Ini.Ui.DemoApp.Configuration;

/// <summary>
/// General application settings — startup behaviour and locale.
/// </summary>
/// <remarks>
/// <para>
/// This section uses the default <c>Immediate</c> change mode: every edit takes effect
/// as soon as the user changes the control value, with no buffering.
/// </para>
/// <para>
/// The <c>INotifyPropertyChanged</c> interface (supported by the Dapplo.Ini source
/// generator) lets the WPF renderer subscribe to property changes and re-evaluate
/// conditional visibility/enable rules in real time.
/// </para>
/// </remarks>
[IniSection("General")]
[UiPage(Title = "General", Order = 0)]
public interface IGeneralSettings : IIniSection, INotifyPropertyChanged
{
    /// <summary>The display name shown in title bars.</summary>
    [DefaultValue("My Application")]
    [UiLabelKey("general_app_name")]
    [MaxLength(100)]
    string ApplicationName { get; set; }

    /// <summary>Launch the application automatically when Windows starts.</summary>
    [DefaultValue(false)]
    [UiLabelKey("general_start_windows")]
    bool StartWithWindows { get; set; }

    /// <summary>UI language.  Enum type → auto-inferred DropDown control.</summary>
    [DefaultValue(LanguageOption.English)]
    [UiLabelKey("general_language")]
    LanguageOption Language { get; set; }
}
