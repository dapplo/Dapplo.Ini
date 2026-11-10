// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Dapplo.Ini.Configuration;
using Dapplo.Ini.Interfaces;
using Dapplo.Ini.WindowsPolicies.Registry;
using Microsoft.Win32;

namespace Dapplo.Ini.WindowsPolicies;

/// <summary>Registers Windows policies as value sources.</summary>
public static class WindowsPolicyBuilderExtensions
{
    /// <summary>
    /// Reads the Windows policies of the application below <c>SOFTWARE\Policies\&lt;vendor&gt;\&lt;product&gt;</c>
    /// (<c>HKEY_CURRENT_USER</c> and <c>HKEY_LOCAL_MACHINE</c>, 64-bit view) and reloads when they change.
    /// </summary>
    /// <remarks>
    /// Registers four sources, in this order (a later one wins):
    /// <list type="number">
    /// <item>recommended HKCU, then recommended HKLM, in the <see cref="ValueSourceLayer.Default"/> layer: a default the user can change;</item>
    /// <item>mandatory HKCU, then mandatory HKLM, in the <see cref="ValueSourceLayer.Constant"/> layer: the key becomes constant.</item>
    /// </list>
    /// Computer policies win over user policies, as in Group Policy.
    /// </remarks>
    /// <param name="builder">The builder.</param>
    /// <param name="vendor">The vendor (company) name, e.g. <c>Dapplo</c>.</param>
    /// <param name="product">The product name, e.g. <c>Greenshot</c>.</param>
    /// <param name="options">Which hives and kinds, the registry view, a root path override and the watching.</param>
    public static IniConfigBuilder AddWindowsPolicies(this IniConfigBuilder builder, string vendor, string product,
        WindowsPolicyOptions? options = null)
    {
        if (builder == null)
            throw new ArgumentNullException(nameof(builder));
        foreach (var (source, layer) in CreateSources(vendor, product, options))
            builder.AddValueSource(source, layer);
        return builder;
    }

    /// <summary>
    /// Creates the sources <see cref="AddWindowsPolicies"/> registers, in registration order, with their layer.
    /// </summary>
    public static IReadOnlyList<(WindowsPolicySource Source, ValueSourceLayer Layer)> CreateSources(string vendor, string product,
        WindowsPolicyOptions? options = null)
    {
        options ??= new WindowsPolicyOptions();
        var rootPath = string.IsNullOrWhiteSpace(options.RootPath)
            ? WindowsPolicyOptions.GetDefaultRootPath(vendor, product)
            : options.RootPath!.Trim('\\');

        var hives = new List<RegistryHive>();
        if (options.IncludeCurrentUser)
            hives.Add(RegistryHive.CurrentUser);
        if (options.IncludeLocalMachine)
            hives.Add(RegistryHive.LocalMachine);

        var result = new List<(WindowsPolicySource, ValueSourceLayer)>();
        if (options.IncludeRecommended)
        {
            foreach (var hive in hives)
                result.Add((Create(hive, PolicyKind.Recommended), ValueSourceLayer.Default));
        }
        foreach (var hive in hives)
            result.Add((Create(hive, PolicyKind.Mandatory), ValueSourceLayer.Constant));
        return result;

        WindowsPolicySource Create(RegistryHive hive, PolicyKind kind)
        {
            var registry = options.RegistryFactory?.Invoke(hive) ?? new WindowsRegistryRoot(hive, options.RegistryView);
            Func<Watching.IPolicyChangeWatcher>? watcher = null;
            if (options.WatchForChanges)
            {
                watcher = options.WatcherFactory != null
                    ? () => options.WatcherFactory(hive)
                    : () => Watching.RegistryChangeWatcher.Acquire(hive, options.RegistryView, rootPath);
            }
            return new WindowsPolicySource(hive, kind, rootPath, registry, watcher, options.ChangeDebounce);
        }
    }
}
