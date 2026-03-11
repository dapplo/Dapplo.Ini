// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Dapplo.Ini.Interfaces;

namespace Dapplo.Ini;

/// <summary>
/// Thread-safe global registry that maps INI file names to their <see cref="IniConfig"/> instances.
/// Consumers can retrieve their configuration from anywhere in the application without DI.
/// </summary>
public static class IniConfigRegistry
{
    private static readonly Dictionary<string, IniConfig> _registry =
        new(StringComparer.OrdinalIgnoreCase);

    private static readonly object _lock = new();

    // ── Registration ──────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a new <see cref="IniConfigBuilder"/> for the file with the given <paramref name="fileName"/>.
    /// </summary>
    public static IniConfigBuilder ForFile(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            throw new ArgumentException("File name must not be empty.", nameof(fileName));

        return new IniConfigBuilder(fileName);
    }

    internal static void Register(string fileName, IniConfig config)
    {
        lock (_lock)
        {
            _registry[fileName] = config;
        }
    }

    // ── Lookup ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the <see cref="IniConfig"/> registered for <paramref name="fileName"/>.
    /// </summary>
    /// <exception cref="KeyNotFoundException">Thrown when no config is registered for the given file name.</exception>
    public static IniConfig Get(string fileName)
    {
        lock (_lock)
        {
            if (_registry.TryGetValue(fileName, out var config))
                return config;
        }
        throw new KeyNotFoundException(
            $"No INI configuration has been registered for file '{fileName}'. " +
            "Call IniConfigRegistry.ForFile(...).Build() during application startup.");
    }

    /// <summary>
    /// Returns the section of type <typeparamref name="T"/> from the configuration registered for
    /// <paramref name="fileName"/>.
    /// </summary>
    public static T GetSection<T>(string fileName) where T : IIniSection
        => Get(fileName).GetSection<T>();

    /// <summary>
    /// Attempts to return the <see cref="IniConfig"/> registered for <paramref name="fileName"/>.
    /// Returns <c>false</c> when not found.
    /// </summary>
    public static bool TryGet(string fileName, out IniConfig? config)
    {
        lock (_lock)
        {
            return _registry.TryGetValue(fileName, out config);
        }
    }

    /// <summary>
    /// Removes the registration for <paramref name="fileName"/>.
    /// Useful in tests to reset state between runs.
    /// </summary>
    public static bool Unregister(string fileName)
    {
        lock (_lock)
        {
            return _registry.Remove(fileName);
        }
    }

    /// <summary>Removes all registered configurations. Mainly useful in tests.</summary>
    public static void Clear()
    {
        lock (_lock)
        {
            _registry.Clear();
        }
    }
}
