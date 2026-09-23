// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace Dapplo.Ini.Ui.Attributes;

/// <summary>
/// Associates a configuration property (or section interface) with an
/// internationalization (i18n) resource key for its label text.
/// </summary>
/// <remarks>
/// <para>
/// The UI framework uses this key to look up the display label via the project's i18n
/// mechanism (e.g. <c>LanguageConfig</c> from <c>Dapplo.Ini.Internationalization</c>).
/// When this attribute is absent the property name (or section name) is used as the
/// label directly.
/// </para>
/// <para>
/// A <see cref="DescriptionKey"/> can optionally be supplied to look up a tooltip or
/// longer description string for the control.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// [UiLabelKey("settings_proxy_host_label", DescriptionKey = "settings_proxy_host_tooltip")]
/// string ProxyHost { get; set; }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Interface, AllowMultiple = false, Inherited = false)]
public sealed class UiLabelKeyAttribute : Attribute
{
    /// <summary>
    /// Gets the i18n resource key used to look up the control's label text.
    /// </summary>
    public string LabelKey { get; }

    /// <summary>
    /// Initialises a new instance of <see cref="UiLabelKeyAttribute"/>.
    /// </summary>
    /// <param name="labelKey">The i18n resource key for the label.</param>
    public UiLabelKeyAttribute(string labelKey)
    {
        LabelKey = labelKey;
    }

    /// <summary>
    /// Gets or sets an optional i18n resource key for the control's tooltip or longer
    /// description text.  When absent no tooltip is shown.
    /// </summary>
    public string? DescriptionKey { get; set; }
}
