// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace Dapplo.Ini.WindowsPolicies;

/// <summary>
/// Excludes a property (or, on the section interface, a whole section) from Windows policies: no policy is read for it
/// and the administrative template generator leaves it out.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Interface, Inherited = true)]
public sealed class NotAdministrableAttribute : Attribute
{
}

/// <summary>
/// Marks a secret (password, token): the administrative template generator leaves it out, because policy values are
/// readable by every user. A policy is still read for it.
/// </summary>
[AttributeUsage(AttributeTargets.Property, Inherited = true)]
public sealed class SecretAttribute : Attribute
{
}

/// <summary>
/// The name in the administrative template of a policy (property), a section category (section interface) or an
/// enum item (enum value); by default the name split at camel case.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Interface | AttributeTargets.Field, Inherited = true)]
public sealed class PolicyDisplayNameAttribute : Attribute
{
    /// <param name="displayName">The display name.</param>
    public PolicyDisplayNameAttribute(string displayName) => DisplayName = displayName;

    /// <summary>The display name.</summary>
    public string DisplayName { get; }
}

/// <summary>
/// Puts the policy of a property (or of every property of the section) also into an extra category of the
/// administrative template, e.g. <c>"Common"</c>, below the product category.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Interface, Inherited = true, AllowMultiple = false)]
public sealed class PolicyCategoryAttribute : Attribute
{
    /// <param name="category">The name of the extra category.</param>
    public PolicyCategoryAttribute(string category) => Category = category;

    /// <summary>The name of the extra category.</summary>
    public string Category { get; }
}

/// <summary>
/// Marks a string property that holds a path: the administrative template writes it as <c>REG_EXPAND_SZ</c>
/// (<c>expandable="true"</c>), so administrators can use environment variables. The value is passed to the
/// application unexpanded, as the INI file would hold it.
/// </summary>
[AttributeUsage(AttributeTargets.Property, Inherited = true)]
public sealed class PolicyExpandableAttribute : Attribute
{
}
