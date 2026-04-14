// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Dapplo.Ini.Ui.Enums;

namespace Dapplo.Ini.Ui.Attributes;

/// <summary>
/// Specifies when a configuration change made via the settings UI is applied to the
/// underlying INI section.
/// </summary>
/// <remarks>
/// <para>
/// When applied to a <em>property</em> it controls the change behaviour for that specific
/// control.  When applied to a <em>section interface</em> it sets the default for all
/// properties in that section (individual properties can still override it).
/// </para>
/// <para>
/// The default mode is <see cref="UiChangeMode.Immediate"/> when this attribute is absent.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// // All properties in this section require explicit confirmation:
/// [UiChangeMode(UiChangeMode.OnConfirm)]
/// public interface INetworkSettings : IIniSection { … }
///
/// // This specific property is applied immediately even in an OnConfirm section:
/// [UiChangeMode(UiChangeMode.Immediate)]
/// bool EnableLogging { get; set; }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Interface, AllowMultiple = false, Inherited = false)]
public sealed class UiChangeModeAttribute : Attribute
{
    /// <summary>
    /// Gets the change mode for the decorated property or section.
    /// </summary>
    public UiChangeMode ChangeMode { get; }

    /// <summary>
    /// Initialises a new instance of <see cref="UiChangeModeAttribute"/>.
    /// </summary>
    /// <param name="changeMode">The change mode to apply.</param>
    public UiChangeModeAttribute(UiChangeMode changeMode)
    {
        ChangeMode = changeMode;
    }
}
