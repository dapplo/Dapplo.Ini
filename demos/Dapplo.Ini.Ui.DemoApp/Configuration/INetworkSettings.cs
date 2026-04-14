// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Dapplo.Ini.Attributes;
using Dapplo.Ini.Interfaces;
using Dapplo.Ini.Ui.Attributes;
using Dapplo.Ini.Ui.Enums;

namespace Dapplo.Ini.Ui.DemoApp.Configuration;

/// <summary>
/// Network / proxy settings.
/// </summary>
/// <remarks>
/// <para>
/// This section demonstrates two key framework features:
/// <list type="bullet">
///   <item>
///     <description>
///       <b>Transactional changes</b> — the section implements <see cref="ITransactional"/>,
///       so <see cref="UiChangeModeAttribute"/> is set to <c>OnConfirm</c>.  Edits are
///       buffered until the user clicks <em>OK</em> or <em>Apply</em>.  Clicking
///       <em>Cancel</em> rolls all changes back to the values they had when the settings
///       window was opened.
///     </description>
///   </item>
///   <item>
///     <description>
///       <b>Conditional visibility / enable</b> — the <c>ProxyHost</c> and
///       <c>ProxyPort</c> fields are hidden and disabled while <c>UseProxy</c> is
///       <c>false</c>, and become visible/enabled as soon as the checkbox is ticked.
///     </description>
///   </item>
/// </list>
/// </para>
/// </remarks>
[IniSection("Network")]
[UiPage(Title = "Network", Category = "Advanced", Order = 10)]
[UiChangeMode(UiChangeMode.OnConfirm)]
public interface INetworkSettings : IIniSection, ITransactional, INotifyPropertyChanged
{
    /// <summary>Enable HTTP/HTTPS proxy routing.</summary>
    [DefaultValue(false)]
    [UiLabelKey("network_use_proxy")]
    bool UseProxy { get; set; }

    /// <summary>
    /// Proxy server hostname or IP.
    /// Hidden and disabled when <see cref="UseProxy"/> is <c>false</c>.
    /// </summary>
    [DefaultValue("")]
    [UiGroup("Proxy Settings", Order = 5)]
    [UiConditionalVisibility(nameof(UseProxy))]
    [UiConditionalEnable(nameof(UseProxy))]
    [UiLabelKey("network_proxy_host")]
    string ProxyHost { get; set; }

    /// <summary>
    /// Proxy server port.
    /// Hidden and disabled when <see cref="UseProxy"/> is <c>false</c>.
    /// </summary>
    [DefaultValue(8080)]
    [UiControl(UiControlType.UpDown, Minimum = 1, Maximum = 65535)]
    [UiGroup("Proxy Settings")]
    [UiConditionalVisibility(nameof(UseProxy))]
    [UiConditionalEnable(nameof(UseProxy))]
    [UiLabelKey("network_proxy_port")]
    int ProxyPort { get; set; }

    /// <summary>HTTP request timeout in seconds.</summary>
    [DefaultValue(30)]
    [UiControl(UiControlType.UpDown, Minimum = 1, Maximum = 300, Unit = "s")]
    [UiLabelKey("network_timeout")]
    int TimeoutSeconds { get; set; }
}
