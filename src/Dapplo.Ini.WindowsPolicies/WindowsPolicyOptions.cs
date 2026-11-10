// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Dapplo.Ini.WindowsPolicies.Registry;
using Dapplo.Ini.WindowsPolicies.Watching;
using Microsoft.Win32;

namespace Dapplo.Ini.WindowsPolicies;

/// <summary>Whether a policy is enforced or only a default the user can change.</summary>
public enum PolicyKind
{
    /// <summary>
    /// <c>SOFTWARE\Policies\&lt;Vendor&gt;\&lt;Product&gt;\&lt;Section&gt;</c>: enforced, applied in the
    /// <see cref="Interfaces.ValueSourceLayer.Constant"/> layer (the key becomes constant).
    /// </summary>
    Mandatory,

    /// <summary>
    /// <c>SOFTWARE\Policies\&lt;Vendor&gt;\&lt;Product&gt;\Recommended\&lt;Section&gt;</c>: a default the user can change,
    /// applied in the <see cref="Interfaces.ValueSourceLayer.Default"/> layer.
    /// </summary>
    Recommended
}

/// <summary>Options for <see cref="WindowsPolicyBuilderExtensions.AddWindowsPolicies"/>.</summary>
public sealed class WindowsPolicyOptions
{
    /// <summary>The name of the sub key below the policy root that holds the recommended policies.</summary>
    public const string RecommendedKeyName = "Recommended";

    /// <summary>Read the policies of <c>HKEY_CURRENT_USER</c> (user policies). Default <c>true</c>.</summary>
    public bool IncludeCurrentUser { get; set; } = true;

    /// <summary>Read the policies of <c>HKEY_LOCAL_MACHINE</c> (computer policies, win over user policies). Default <c>true</c>.</summary>
    public bool IncludeLocalMachine { get; set; } = true;

    /// <summary>Read the recommended policies (<see cref="PolicyKind.Recommended"/>). Default <c>true</c>.</summary>
    public bool IncludeRecommended { get; set; } = true;

    /// <summary>
    /// The registry view. Default <see cref="RegistryView.Registry64"/>: a 32-bit process reads the same policies as
    /// a 64-bit one (Group Policy writes them to the 64-bit view). On a 32-bit Windows this is the 32-bit view.
    /// </summary>
    public RegistryView RegistryView { get; set; } = RegistryView.Registry64;

    /// <summary>
    /// The policy root key below the hive, overriding <c>SOFTWARE\Policies\&lt;Vendor&gt;\&lt;Product&gt;</c>.
    /// </summary>
    public string? RootPath { get; set; }

    /// <summary>Watch the policy keys and reload the configuration when a policy changes. Default <c>true</c>.</summary>
    public bool WatchForChanges { get; set; } = true;

    /// <summary>
    /// How long the watcher waits for further registry changes before it reports them (a Group Policy refresh writes
    /// many values). Default 250 ms; the configuration debounces once more (<c>ValueSourceChangeDebounce</c>).
    /// </summary>
    public TimeSpan ChangeDebounce { get; set; } = TimeSpan.FromMilliseconds(250);

    /// <summary>For tests: creates the registry access per hive.</summary>
    internal Func<RegistryHive, IRegistryRoot>? RegistryFactory { get; set; }

    /// <summary>For tests: creates the change watcher per hive.</summary>
    internal Func<RegistryHive, IPolicyChangeWatcher>? WatcherFactory { get; set; }

    /// <summary>The default policy root: <c>SOFTWARE\Policies\&lt;Vendor&gt;\&lt;Product&gt;</c>.</summary>
    public static string GetDefaultRootPath(string vendor, string product)
    {
        if (string.IsNullOrWhiteSpace(vendor))
            throw new ArgumentException("A vendor is needed.", nameof(vendor));
        if (string.IsNullOrWhiteSpace(product))
            throw new ArgumentException("A product is needed.", nameof(product));
        return $@"SOFTWARE\Policies\{vendor.Trim()}\{product.Trim()}";
    }
}
