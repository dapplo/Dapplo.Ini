// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Dapplo.Ini.Interfaces;
using Dapplo.Ini.Internationalization;

namespace Dapplo.Ini.Internationalization.Configuration;

/// <summary>
/// Fluent builder that configures a <see cref="LanguageConfig"/>.
/// </summary>
/// <remarks>
/// <para>
/// The API is aligned with <see cref="IniConfigBuilder"/>:
/// <see cref="Create()"/> creates without loading (deferred, for plugin scenarios),
/// <see cref="Build"/> creates and loads immediately.
/// </para>
/// <para>
/// See the project wiki page <em>Internationalization</em> for usage patterns including the
/// direct-build pattern and the deferred (plugin-friendly) build pattern.
/// </para>
/// </remarks>
public sealed class LanguageConfigBuilder
{
    private readonly string _basename;
    private string? _defaultDirectory;
    private string? _baseLanguage;
    private string? _currentLanguage;
    private bool _useFallback;
    private string? _fallbackLanguage;
    private bool _monitorFiles;

    // Registered sections: type → (instance, optional override directory)
    private readonly List<(Type Type, LanguageSectionBase Section, string? Directory)> _sections = new();

    // Diagnostic listeners
    private readonly List<IIniConfigListener> _listeners = new();

    private LanguageConfigBuilder(string basename)
    {
        _basename = basename;
    }

    // ── Factory ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a new <see cref="LanguageConfigBuilder"/> for the application with the given
    /// <paramref name="basename"/>.
    /// </summary>
    /// <param name="basename">
    /// Base name for the language file naming convention: <c>{basename}.{ietf}.ini</c>
    /// (or <c>{basename}.{module}.{ietf}.ini</c> for sections with a module name).
    /// </param>
    public static LanguageConfigBuilder ForBasename(string basename)
    {
        if (string.IsNullOrWhiteSpace(basename))
            throw new ArgumentException("Basename must not be empty.", nameof(basename));

        return new LanguageConfigBuilder(basename);
    }

    // ── Configuration ─────────────────────────────────────────────────────────

    /// <summary>
    /// Adds a search path for language pack files.
    /// Individual sections can override this via <see cref="RegisterSection{T}(T, string)"/>.
    /// </summary>
    /// <param name="path">The directory path to search for language pack files.</param>
    public LanguageConfigBuilder AddSearchPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Search path must not be empty.", nameof(path));

        _defaultDirectory = path;
        return this;
    }

    /// <summary>
    /// Sets the base (reference) language.
    /// The base language file is always loaded first so that any key missing from the
    /// active language is displayed in the base language rather than as a
    /// <c>###key###</c> sentinel.
    /// </summary>
    /// <param name="ietf">IETF language tag (e.g. <c>"en-US"</c>).</param>
    public LanguageConfigBuilder WithBaseLanguage(string ietf)
    {
        if (string.IsNullOrWhiteSpace(ietf))
            throw new ArgumentException("Base language tag must not be empty.", nameof(ietf));

        _baseLanguage = ietf;
        return this;
    }

    /// <summary>
    /// Sets the language to activate on first load.
    /// Defaults to the base language when not specified.
    /// </summary>
    /// <param name="ietf">IETF language tag (e.g. <c>"de-DE"</c>).</param>
    public LanguageConfigBuilder WithCurrentLanguage(string ietf)
    {
        if (string.IsNullOrWhiteSpace(ietf))
            throw new ArgumentException("Current language tag must not be empty.", nameof(ietf));

        _currentLanguage = ietf;
        return this;
    }

    /// <summary>
    /// Enables fallback behaviour: when a key is missing from the active language the
    /// framework uses the base language value instead of the <c>###key###</c> sentinel.
    /// </summary>
    /// <param name="ietf">
    /// Optional IETF tag of a specific fallback language.
    /// When <c>null</c> (or not supplied) the base language is used as the fallback.
    /// </param>
    public LanguageConfigBuilder UseFallback(string? ietf = null)
    {
        _useFallback = true;
        _fallbackLanguage = ietf;
        return this;
    }

    /// <summary>
    /// Enables file-system monitoring. When any language file in a registered directory
    /// is modified, all sections are reloaded and <see cref="LanguageConfig.LanguageChanged"/>
    /// is raised.
    /// </summary>
    public LanguageConfigBuilder MonitorFiles()
    {
        _monitorFiles = true;
        return this;
    }

    /// <summary>
    /// Registers a language section on the builder.
    /// The section name and optional module name are read from the section's
    /// <see cref="LanguageSectionBase.SectionName"/> and <see cref="LanguageSectionBase.ModuleName"/>
    /// at load time.
    /// </summary>
    /// <typeparam name="T">The language section interface or class type.</typeparam>
    /// <param name="section">The generated concrete section instance.</param>
    /// <param name="path">
    /// Optional override search path for this section's language files.
    /// When <c>null</c> the default path set by <see cref="AddSearchPath"/> is used.
    /// </param>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="section"/> does not derive from <see cref="LanguageSectionBase"/>.
    /// </exception>
    public LanguageConfigBuilder RegisterSection<T>(T section, string? path = null)
        where T : class
    {
        if (section is null) throw new ArgumentNullException(nameof(section));
        if (section is not LanguageSectionBase baseSection)
            throw new ArgumentException(
                $"Section must be a generated language section (must derive from {nameof(LanguageSectionBase)}).",
                nameof(section));

        _sections.Add((typeof(T), baseSection, path));
        return this;
    }

    // ── Diagnostic listeners ──────────────────────────────────────────────────

    /// <summary>
    /// Registers a listener that will be called for diagnostic events such as file loaded,
    /// file not found, reloaded, and errors.  Multiple listeners may be registered;
    /// they are invoked in registration order.
    /// </summary>
    /// <remarks>
    /// There is zero overhead when no listener is registered.
    /// See the project wiki page <em>Listeners</em> for the full callback reference and notes on
    /// which callbacks are raised by <c>LanguageConfig</c>.
    /// </remarks>
    /// <param name="listener">The listener to register; must not be <c>null</c>.</param>
    public LanguageConfigBuilder AddListener(IIniConfigListener listener)
    {
        if (listener is null) throw new ArgumentNullException(nameof(listener));
        _listeners.Add(listener);
        return this;
    }

    // ── Create / Build ────────────────────────────────────────────────────────

    /// <summary>
    /// Creates the <see cref="LanguageConfig"/> and registers all builder sections
    /// <em>without loading any language files</em>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the deferred-loading entry point, aligned with <see cref="IniConfigBuilder.Create"/>.
    /// Use it when plugins or other components need to register their own sections before
    /// loading begins.
    /// </para>
    /// <para>
    /// The returned <see cref="LanguageConfig"/> is immediately accessible via
    /// <see cref="LanguageConfigRegistry.Get"/> so that plugins can discover it during phase 2.
    /// </para>
    /// </remarks>
    /// <returns>The newly created (not yet loaded) <see cref="LanguageConfig"/>.</returns>
    public LanguageConfig Create()
    {
        if (string.IsNullOrEmpty(_baseLanguage))
            throw new InvalidOperationException(
                "A base language must be specified via WithBaseLanguage().");

        string? effectiveFallback = _useFallback ? _fallbackLanguage : null;

        var sections = _sections.Select(s => (s.Type, s.Section, s.Directory)).ToList();

        var config = new LanguageConfig(
            _basename,
            _baseLanguage!,
            _currentLanguage ?? _baseLanguage!,
            effectiveFallback,
            _monitorFiles,
            _defaultDirectory,
            sections,
            _listeners);

        LanguageConfigRegistry.Register(_basename, config);
        return config;
    }

    /// <summary>
    /// Builds and loads a <see cref="LanguageConfig"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <see cref="WithBaseLanguage"/> has not been called, or when a section
    /// has no directory configured and no default directory is set.
    /// </exception>
    public LanguageConfig Build()
    {
        var config = Create();
        config.Load();
        return config;
    }

    /// <summary>
    /// Asynchronously builds and loads a <see cref="LanguageConfig"/>.
    /// </summary>
    public async Task<LanguageConfig> BuildAsync(CancellationToken cancellationToken = default)
    {
        var config = Create();
        return await config.LoadAsync(cancellationToken).ConfigureAwait(false);
    }
}
