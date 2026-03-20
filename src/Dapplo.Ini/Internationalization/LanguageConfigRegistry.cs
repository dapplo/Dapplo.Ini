// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Dapplo.Ini.Internationalization.Configuration;

namespace Dapplo.Ini.Internationalization;

/// <summary>
/// Thread-safe global registry that maps language file basenames to their <see cref="LanguageConfig"/>
/// instances.
/// Consumers can retrieve their language configuration from anywhere in the application without DI.
/// </summary>
/// <remarks>
/// The API mirrors <see cref="IniConfigRegistry"/>:
/// <code>
/// // Application startup — build and register:
/// LanguageConfigRegistry.ForFile("myapp")
///     .AddSearchPath("/path/to/lang")
///     .WithBaseLanguage("en-US")
///     .RegisterSection&lt;IMainLanguage&gt;(new MainLanguageImpl())
///     .Build();
///
/// // Anywhere in the app — retrieve:
/// var lang = LanguageConfigRegistry.GetSection&lt;IMainLanguage&gt;("myapp");
/// </code>
/// </remarks>
public static class LanguageConfigRegistry
{
    private static readonly Dictionary<string, LanguageConfig> _registry =
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
    /// Creates a new <see cref="LanguageConfigBuilder"/> for the language files with the given
    /// <paramref name="basename"/>.
    /// The <c>.ini</c> extension is optional and is stripped if present; both
    /// <c>ForFile("myapp")</c> and <c>ForFile("myapp.ini")</c> produce the same builder.
    /// </summary>
    /// <param name="basename">
    /// Base name for the language file naming convention: <c>{basename}.{ietf}.ini</c>.
    /// </param>
    public static LanguageConfigBuilder ForFile(string basename)
    {
        if (string.IsNullOrWhiteSpace(basename))
            throw new ArgumentException("Basename must not be empty.", nameof(basename));

        return LanguageConfigBuilder.ForBasename(NormalizeBasename(basename));
    }

    internal static void Register(string basename, LanguageConfig config)
    {
        lock (_lock)
        {
            _registry[basename] = config;
        }
    }

    // ── Lookup ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the <see cref="LanguageConfig"/> registered for <paramref name="basename"/>.
    /// The <c>.ini</c> extension is optional; both <c>"myapp"</c> and <c>"myapp.ini"</c> resolve
    /// to the same entry.
    /// </summary>
    /// <exception cref="KeyNotFoundException">
    /// Thrown when no config is registered for the given basename.
    /// </exception>
    public static LanguageConfig Get(string basename)
    {
        var key = NormalizeBasename(basename);
        lock (_lock)
        {
            if (_registry.TryGetValue(key, out var config))
                return config;
        }
        throw new KeyNotFoundException(
            $"No language configuration has been registered for basename '{basename}'. " +
            "Call LanguageConfigRegistry.ForFile(...).Build() during application startup.");
    }

    /// <summary>
    /// Returns the single registered <see cref="LanguageConfig"/>.
    /// This convenience overload is useful when the application registers exactly one language
    /// configuration, which is the common case, so the caller does not need to specify the basename.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when no language configuration is registered, or when more than one is registered
    /// (in which case use <see cref="Get(string)"/> with an explicit basename).
    /// </exception>
    public static LanguageConfig Get()
    {
        lock (_lock)
        {
            return _registry.Count switch
            {
                0 => throw new InvalidOperationException(
                    "No language configuration has been registered. " +
                    "Call LanguageConfigRegistry.ForFile(...).Build() during application startup."),
                1 => _registry.Values.First(),
                _ => throw new InvalidOperationException(
                    $"More than one language configuration is registered ({_registry.Count}). " +
                    "Use Get(basename) to specify which configuration to retrieve.")
            };
        }
    }

    /// <summary>
    /// Returns the section of type <typeparamref name="T"/> from the configuration registered for
    /// <paramref name="basename"/>.
    /// </summary>
    public static T GetSection<T>(string basename) where T : class
        => Get(basename).GetSection<T>();

    /// <summary>
    /// Returns the section of type <typeparamref name="T"/> from the single registered language
    /// configuration.
    /// This convenience overload is useful when the application registers exactly one language
    /// configuration.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when no language configuration is registered, or when more than one is registered
    /// (in which case use <see cref="GetSection{T}(string)"/> with an explicit basename).
    /// </exception>
    public static T GetSection<T>() where T : class
        => Get().GetSection<T>();

    /// <summary>
    /// Attempts to return the <see cref="LanguageConfig"/> registered for <paramref name="basename"/>.
    /// Returns <c>false</c> when not found.
    /// </summary>
    public static bool TryGet(string basename, out LanguageConfig? config)
    {
        var key = NormalizeBasename(basename);
        lock (_lock)
        {
            return _registry.TryGetValue(key, out config);
        }
    }

    /// <summary>
    /// Removes the registration for <paramref name="basename"/>.
    /// Useful in tests to reset state between runs.
    /// </summary>
    public static bool Unregister(string basename)
    {
        var key = NormalizeBasename(basename);
        lock (_lock)
        {
            return _registry.Remove(key);
        }
    }

    /// <summary>Removes all registered language configurations. Mainly useful in tests.</summary>
    public static void Clear()
    {
        lock (_lock)
        {
            _registry.Clear();
        }
    }
}
