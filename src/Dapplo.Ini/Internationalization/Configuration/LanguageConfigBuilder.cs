// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

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
/// Two usage patterns are supported:
/// <list type="number">
///   <item>
///     <term>Direct build</term>
///     <description>
///     All sections are registered on the builder and the config is built and loaded in one step:
///     <code>
///     using var config = LanguageConfigBuilder.ForBasename("myapp")
///         .WithDirectory("/path/to/lang")
///         .WithBaseLanguage("en-US")
///         .AddSection&lt;IMainLanguage&gt;(new MainLanguageImpl())
///         .Build();
///     </code>
///     </description>
///   </item>
///   <item>
///     <term>Deferred (plugin-friendly) build</term>
///     <description>
///     The host creates the config without loading it, plugins register their own sections,
///     and then the host triggers loading:
///     <code>
///     // Host (Phase 1) — create without loading:
///     var config = LanguageConfigBuilder.ForBasename("myapp")
///         .WithDirectory("/path/to/lang")
///         .WithBaseLanguage("en-US")
///         .AddSection&lt;IMainLanguage&gt;(new MainLanguageImpl())
///         .Create();
///
///     // Plugin (Phase 2) — register own section:
///     config.AddSection&lt;IPluginLanguage&gt;(new PluginLanguageImpl(), "/path/to/plugin/lang");
///
///     // Host (Phase 3) — load all sections at once:
///     config.Load();
///     </code>
///     </description>
///   </item>
/// </list>
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
    /// Base name used in the language file naming convention.  For an application named
    /// <c>"myapp"</c> the files would be named <c>myapp.en-US.ini</c>, etc.
    /// </param>
    public static LanguageConfigBuilder ForBasename(string basename)
    {
        if (string.IsNullOrWhiteSpace(basename))
            throw new ArgumentException("Basename must not be empty.", nameof(basename));

        return new LanguageConfigBuilder(basename);
    }

    /// <summary>
    /// Creates a new <see cref="LanguageConfigBuilder"/>.
    /// </summary>
    /// <remarks>Alias for <see cref="ForBasename"/> kept for backward compatibility.</remarks>
    /// <param name="basename">Base name used in the language file naming convention.</param>
    public static LanguageConfigBuilder Create(string basename)
        => ForBasename(basename);

    // ── Configuration ─────────────────────────────────────────────────────────

    /// <summary>
    /// Sets the default directory that is searched for language pack files.
    /// Individual sections can override this via <see cref="AddSection{T}(T, string)"/>.
    /// </summary>
    public LanguageConfigBuilder WithDirectory(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
            throw new ArgumentException("Directory must not be empty.", nameof(directory));

        _defaultDirectory = directory;
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
    /// The module name (if any) is read from the section's <see cref="LanguageSectionBase.SectionName"/>
    /// at load time and determines which file / section within a file is used.
    /// </summary>
    /// <typeparam name="T">The language section interface or class type.</typeparam>
    /// <param name="section">The generated concrete section instance.</param>
    /// <param name="directory">
    /// Optional override directory for this section's language files.
    /// When <c>null</c> the default directory set by <see cref="WithDirectory"/> is used.
    /// </param>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="section"/> does not derive from <see cref="LanguageSectionBase"/>.
    /// </exception>
    public LanguageConfigBuilder AddSection<T>(T section, string? directory = null)
        where T : class
    {
        if (section is null) throw new ArgumentNullException(nameof(section));
        if (section is not LanguageSectionBase baseSection)
            throw new ArgumentException(
                $"Section must be a generated language section (must derive from {nameof(LanguageSectionBase)}).",
                nameof(section));

        _sections.Add((typeof(T), baseSection, directory));
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
    /// </remarks>
    /// <returns>The newly created (not yet loaded) <see cref="LanguageConfig"/>.</returns>
    public LanguageConfig Create()
    {
        if (string.IsNullOrEmpty(_baseLanguage))
            throw new InvalidOperationException(
                "A base language must be specified via WithBaseLanguage().");

        string? effectiveFallback = _useFallback ? _fallbackLanguage : null;

        var sections = _sections.Select(s => (s.Type, s.Section, s.Directory)).ToList();

        return new LanguageConfig(
            _basename,
            _baseLanguage!,
            _currentLanguage ?? _baseLanguage!,
            effectiveFallback,
            _monitorFiles,
            _defaultDirectory,
            sections);
    }

    /// <summary>
    /// Obsolete: use <see cref="Create()"/> instead.
    /// </summary>
    [Obsolete("Use Create() instead. Prepare() is an alias kept for backward compatibility.")]
    public LanguageConfig Prepare() => Create();

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
        await config.LoadAsync(cancellationToken).ConfigureAwait(false);
        return config;
    }
}
