// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Dapplo.Ini.Interfaces;

namespace Dapplo.Ini;

/// <summary>
/// Thread-safe global registry that maps INI file basenames to their <see cref="IniConfig"/> instances.
/// Consumers can retrieve their configuration from anywhere in the application without DI.
/// </summary>
public static class IniConfigRegistry
{
    private static readonly Dictionary<string, IniConfig> _registry =
        new(StringComparer.OrdinalIgnoreCase);

    private static readonly object _lock = new();

    // ── Key normalisation ─────────────────────────────────────────────────────

    /// <summary>
    /// Strips the <c>.ini</c> extension (case-insensitive) so that both
    /// <c>"myapp"</c> and <c>"myapp.ini"</c> resolve to the same registry entry.
    /// Also strips any leading directory path so that full paths are handled safely.
    /// </summary>
    private static string NormalizeBasename(string fileNameOrBasename)
    {
        var name = Path.GetFileName(fileNameOrBasename) ?? fileNameOrBasename;
        return string.Equals(Path.GetExtension(name), ".ini", StringComparison.OrdinalIgnoreCase)
            ? Path.GetFileNameWithoutExtension(name)!
            : name;
    }

    // ── Registration ──────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a new <see cref="IniConfigBuilder"/> for the file with the given <paramref name="fileName"/>.
    /// The <c>.ini</c> extension is optional and is stripped if present; both
    /// <c>ForFile("myapp")</c> and <c>ForFile("myapp.ini")</c> produce the same builder.
    /// </summary>
    public static IniConfigBuilder ForFile(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            throw new ArgumentException("File name must not be empty.", nameof(fileName));

        return new IniConfigBuilder(fileName);
    }

    internal static void Register(string basename, IniConfig config)
    {
        lock (_lock)
        {
            _registry[basename] = config;
        }
    }

    /// <summary>
    /// Registers a section of type <typeparamref name="T"/> on the <see cref="IniConfig"/>
    /// registered for <paramref name="fileName"/> without triggering any file I/O.
    /// </summary>
    /// <remarks>
    /// This is a convenience overload for plugin-style distributed registrations.
    /// It is equivalent to <c>IniConfigRegistry.Get(fileName).AddSection&lt;T&gt;(section)</c>.
    /// Call <see cref="IniConfig.Load"/> (or <see cref="IniConfig.LoadAsync"/>) after all
    /// sections have been added to read all files at once.
    /// </remarks>
    /// <typeparam name="T">The INI section interface type.</typeparam>
    /// <param name="fileName">The INI file name or basename the config was registered under.</param>
    /// <param name="section">The concrete section instance to register.</param>
    /// <returns>The <paramref name="section"/> instance (for fluent chaining).</returns>
    /// <exception cref="KeyNotFoundException">
    /// Thrown when no config is registered for <paramref name="fileName"/>.
    /// </exception>
    public static T AddSection<T>(string fileName, T section) where T : IIniSection
        => Get(fileName).AddSection(section);

    // ── Lookup ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the <see cref="IniConfig"/> registered for <paramref name="fileName"/>.
    /// The <c>.ini</c> extension is optional; both <c>"myapp"</c> and <c>"myapp.ini"</c> resolve
    /// to the same entry.
    /// </summary>
    /// <exception cref="KeyNotFoundException">Thrown when no config is registered for the given file name.</exception>
    public static IniConfig Get(string fileName)
    {
        var key = NormalizeBasename(fileName);
        lock (_lock)
        {
            if (_registry.TryGetValue(key, out var config))
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
        var key = NormalizeBasename(fileName);
        lock (_lock)
        {
            return _registry.TryGetValue(key, out config);
        }
    }

    /// <summary>
    /// Removes the registration for <paramref name="fileName"/>.
    /// Useful in tests to reset state between runs.
    /// </summary>
    public static bool Unregister(string fileName)
    {
        var key = NormalizeBasename(fileName);
        lock (_lock)
        {
            return _registry.Remove(key);
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
