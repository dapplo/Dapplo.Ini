// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace Dapplo.Ini.Ui.Attributes;

/// <summary>
/// Specifies the display order of a configuration property within its containing group or
/// page in the settings UI.
/// </summary>
/// <remarks>
/// <para>
/// Properties are ordered by ascending <see cref="Order"/> value within their group (or
/// ungrouped page area).  Properties without this attribute default to an order of
/// <c>0</c>; their relative order among each other follows declaration order in the
/// source interface.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// [UiOrder(10)]
/// string Host { get; set; }
///
/// [UiOrder(20)]
/// int Port { get; set; }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class UiOrderAttribute : Attribute
{
    /// <summary>
    /// Gets the display order value. Lower values appear first.
    /// </summary>
    public int Order { get; }

    /// <summary>
    /// Initialises a new instance of <see cref="UiOrderAttribute"/>.
    /// </summary>
    /// <param name="order">The display order value.</param>
    public UiOrderAttribute(int order)
    {
        Order = order;
    }
}
