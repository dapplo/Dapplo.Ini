// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;

namespace Dapplo.Ini.WindowsPolicies.Admx;

/// <summary>Options for <see cref="AdmxGenerator"/>.</summary>
public sealed class AdmxOptions
{
    /// <summary>Creates the options for the policies of <paramref name="product"/> of <paramref name="vendor"/>.</summary>
    /// <param name="vendor">The vendor, as passed to <c>AddWindowsPolicies</c>.</param>
    /// <param name="product">The product, as passed to <c>AddWindowsPolicies</c>.</param>
    public AdmxOptions(string vendor, string product)
    {
        if (string.IsNullOrWhiteSpace(vendor))
            throw new ArgumentException("A vendor is needed.", nameof(vendor));
        if (string.IsNullOrWhiteSpace(product))
            throw new ArgumentException("A product is needed.", nameof(product));
        Vendor = vendor.Trim();
        Product = product.Trim();
    }

    /// <summary>The vendor; the top category and part of the registry path.</summary>
    public string Vendor { get; }

    /// <summary>The product; the product category and part of the registry path.</summary>
    public string Product { get; }

    /// <summary>The policy root below the hive, overriding <c>SOFTWARE\Policies\&lt;Vendor&gt;\&lt;Product&gt;</c>.</summary>
    public string? RootPath { get; set; }

    /// <summary>The target namespace of the ADMX; default <c>&lt;Vendor&gt;.Policies.&lt;Product&gt;</c>. Must be unique in a policy store.</summary>
    public string? Namespace { get; set; }

    /// <summary>The namespace prefix and file name (<c>&lt;prefix&gt;.admx</c>, <c>&lt;lang&gt;\&lt;prefix&gt;.adml</c>); default the product, lower case.</summary>
    public string? Prefix { get; set; }

    /// <summary>The revision of the template, <c>major.minor</c>; default <c>1.0</c>. Raise it with every change.</summary>
    public string Revision { get; set; } = "1.0";

    /// <summary>The languages to write an ADML for; default <c>en-US</c>. The texts come from the code, see <see cref="Localize"/>.</summary>
    public IList<string> Languages { get; set; } = new List<string> { "en-US" };

    /// <summary>
    /// Translates a text of the ADML: called with the language, the string id and the (English) text from the code,
    /// returns the text to write. <c>null</c> keeps the text.
    /// </summary>
    public Func<string, string, string, string?>? Localize { get; set; }

    /// <summary>The "Supported on" text, e.g. <c>MyProduct 2.0 or later</c>; default the product name.</summary>
    public string? SupportedOn { get; set; }

    /// <summary>Also write the recommended policies (a second category tree, users can change them). Default <c>true</c>.</summary>
    public bool IncludeRecommended { get; set; } = true;

    /// <summary>The display name of the recommended tree; default <c>&lt;Product&gt; - Default Settings (users can override)</c>.</summary>
    public string? RecommendedCategoryName { get; set; }

    /// <summary>An extra filter of your own; <c>false</c> leaves the key out.</summary>
    public Func<PolicyKeyInfo, bool>? Include { get; set; }

    /// <summary>
    /// Chooses the category of a policy below the product (instead of the section category). Default: the
    /// <see cref="PolicyCategoryAttribute"/> of the key or section; <c>null</c> means the section category.
    /// </summary>
    public Func<PolicyKeyInfo, string?>? CategorySelector { get; set; }

    /// <summary>Add the default value to the explain text. Default <c>true</c>.</summary>
    public bool ExplainDefaultValue { get; set; } = true;

    /// <summary>Add the registry location to the explain text. Default <c>true</c>.</summary>
    public bool ExplainRegistryLocation { get; set; } = true;

    internal string EffectiveRootPath => string.IsNullOrWhiteSpace(RootPath)
        ? WindowsPolicyOptions.GetDefaultRootPath(Vendor, Product)
        : RootPath!.Trim('\\');

    internal string EffectiveNamespace => string.IsNullOrWhiteSpace(Namespace)
        ? $"{Sanitize(Vendor)}.Policies.{Sanitize(Product)}"
        : Namespace!.Trim();

    internal string EffectivePrefix => string.IsNullOrWhiteSpace(Prefix)
        ? Sanitize(Product).ToLowerInvariant()
        : Prefix!.Trim();

    /// <summary>Makes a name usable as ADMX id: letters, digits and <c>_</c>, not starting with a digit.</summary>
    internal static string Sanitize(string name)
    {
        var sb = new StringBuilder(name.Length);
        foreach (var c in name)
            sb.Append(c is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9') or '_' ? c : '_');
        if (sb.Length == 0 || char.IsDigit(sb[0]))
            sb.Insert(0, '_');
        return sb.ToString();
    }
}
