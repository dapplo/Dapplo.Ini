// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using Dapplo.Ini.Configuration;
using Dapplo.Ini.Interfaces;
#if NET
using System.Diagnostics.CodeAnalysis;
#endif

namespace Dapplo.Ini;

/// <summary>
/// Fluent builder that configures one INI file registration.
/// Call <see cref="Build"/> once all settings have been applied.
/// </summary>
public sealed class IniConfigBuilder
{
    private readonly string _fileName;
    private readonly List<string> _searchPaths = new();
    private readonly List<string> _defaultFilePaths = new();
    private readonly List<string> _constantFilePaths = new();
    private readonly List<IValueSource> _valueSources = new();
    private readonly List<IValueSourceAsync> _valueSourcesAsync = new();

    // Maps interface type → section instance
    private readonly Dictionary<Type, IIniSection> _sections = new();

    private bool _lockFile;
    private bool _monitorFile;
    private FileChangedCallback? _fileChangedCallback;

    // Explicit write-target path (overrides the search-path fallback)
    private string? _writablePath;

    // Encoding for reading/writing INI files (null = UTF-8)
    private Encoding? _encoding;

    // Save-on-exit
    private bool _saveOnExit;

    // Auto-save interval (null = disabled)
    private TimeSpan? _autoSaveInterval;

    // Migration: unknown-key callback (null = no callback)
    private UnknownKeyCallback? _unknownKeyCallback;

    // Migration: optional metadata section config (null = disabled)
    private IniMetadataConfig? _metadataConfig;

    // Diagnostic listeners
    private readonly List<IIniConfigListener> _listeners = new();

    // Global EmptyWhenNull flag — applied to all sections/properties at runtime
    private bool _globalEmptyWhenNull;

    internal IniConfigBuilder(string fileName)
    {
        // Extract just the filename component (in case a full path was passed), then strip
        // the .ini extension so _fileName is always just the basename.
        // The .ini extension is added back in CreateCore when constructing IniConfig.FileName.
        var name = Path.GetFileName(fileName);
        _fileName = string.Equals(Path.GetExtension(name), ".ini", StringComparison.OrdinalIgnoreCase)
            ? Path.GetFileNameWithoutExtension(name)!
            : name;
    }

    // ── location ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Adds a directory to search for the INI file. Directories are tried in the order they are added.
    /// </summary>
    public IniConfigBuilder AddSearchPath(string path)
    {
        _searchPaths.Add(path);
        return this;
    }

    /// <summary>Adds multiple search directories.</summary>
    public IniConfigBuilder AddSearchPaths(IEnumerable<string> paths)
    {
        _searchPaths.AddRange(paths);
        return this;
    }

    /// <summary>
    /// Adds the per-user application-data directory for <paramref name="applicationName"/> as a
    /// search path and, when the INI file is not found anywhere, as the write target for
    /// <see cref="IniConfig.Save"/>.
    /// <list type="bullet">
    ///   <item>On Windows this resolves to <c>%APPDATA%\<paramref name="applicationName"/></c>.</item>
    ///   <item>On Linux it resolves to <c>~/.config/<paramref name="applicationName"/></c>.</item>
    ///   <item>On macOS it resolves to <c>~/Library/Application Support/<paramref name="applicationName"/></c>.</item>
    /// </list>
    /// The directory is created if it does not yet exist so that a subsequent
    /// <see cref="IniConfig.Save"/> can write there immediately.
    /// </summary>
    /// <param name="applicationName">
    /// Sub-directory name under the roaming application-data root (typically the product name).
    /// </param>
    public IniConfigBuilder AddAppDataPath(string applicationName)
    {
        if (string.IsNullOrWhiteSpace(applicationName))
            throw new ArgumentException("Application name must not be empty.", nameof(applicationName));

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var path = Path.Combine(appData, applicationName);
        Directory.CreateDirectory(path);
        return AddSearchPath(path);
    }

    /// <summary>
    /// Explicitly sets the path to which <see cref="IniConfig.Save"/> will write when the INI
    /// file does not exist yet.  Use this when the desired write location differs from every
    /// search path (e.g. when reading from a read-only system directory and writing to
    /// a user-specific location that is not in the search list).
    /// <para>
    /// The containing directory must already exist (or be created by the caller beforehand).
    /// The file itself will be created on the first <see cref="IniConfig.Save"/> call.
    /// </para>
    /// </summary>
    /// <param name="path">Absolute path to the INI file that should be created on save.</param>
    public IniConfigBuilder SetWritablePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Path must not be empty.", nameof(path));
        _writablePath = path;
        return this;
    }

    // ── encoding ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Sets the character encoding used when reading and writing the INI file.
    /// Defaults to UTF-8 when not specified.
    /// Use this to support legacy systems that require encodings such as ISO-8859-1 or Windows-1252.
    /// </summary>
    /// <param name="encoding">The encoding to use; must not be <c>null</c>.</param>
    public IniConfigBuilder WithEncoding(Encoding encoding)
    {
        _encoding = encoding ?? throw new ArgumentNullException(nameof(encoding));
        return this;
    }

    // ── layered defaults/constants ────────────────────────────────────────────

    /// <summary>
    /// Registers a file that supplies default values. Defaults are applied first,
    /// then overridden by the real INI file.
    /// </summary>
    public IniConfigBuilder AddDefaultsFile(string filePath)
    {
        _defaultFilePaths.Add(filePath);
        return this;
    }

    /// <summary>
    /// Registers a file that supplies <em>constant</em> (admin-forced) values. These
    /// are applied last and cannot be overridden by users or defaults.
    /// </summary>
    public IniConfigBuilder AddConstantsFile(string filePath)
    {
        _constantFilePaths.Add(filePath);
        return this;
    }

    // ── external value sources ────────────────────────────────────────────────

    /// <summary>
    /// Registers an external <see cref="IValueSource"/> (e.g. Windows Registry, environment
    /// variables, a web-service endpoint) that can supply or override individual values.
    /// Sources are applied after defaults, the user file and constant files, in the order they
    /// are registered.
    /// </summary>
    public IniConfigBuilder AddValueSource(IValueSource source)
    {
        if (source is null) throw new ArgumentNullException(nameof(source));
        _valueSources.Add(source);
        return this;
    }

    /// <summary>
    /// Registers an external <see cref="IValueSourceAsync"/> for configuration sources that
    /// perform asynchronous I/O (e.g. REST APIs, remote configuration services).
    /// Async sources are applied after all synchronous sources, in the order they are registered.
    /// Async sources are only consulted during <see cref="BuildAsync"/> and
    /// <see cref="IniConfig.ReloadAsync"/>; the synchronous <see cref="Build"/> and
    /// <see cref="IniConfig.Reload"/> methods skip them.
    /// </summary>
    public IniConfigBuilder AddValueSource(IValueSourceAsync source)
    {
        if (source is null) throw new ArgumentNullException(nameof(source));
        _valueSourcesAsync.Add(source);
        return this;
    }

    // ── file lock ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Requests that the INI file be kept open (read-locked) for the lifetime of the
    /// <see cref="IniConfig"/> object.  This prevents other processes from overwriting the
    /// file while the application is running.  Dispose <see cref="IniConfig"/> to release the lock.
    /// </summary>
    public IniConfigBuilder LockFile()
    {
        _lockFile = true;
        return this;
    }

    // ── file change monitoring ─────────────────────────────────────────────────

    /// <summary>
    /// Enables file-system monitoring for the INI file.  When the file is modified by an external
    /// process the <paramref name="callback"/> (if supplied) is invoked so the consumer can decide
    /// whether to reload immediately, ignore, or postpone the reload.
    /// When no callback is supplied every detected change triggers an immediate reload.
    /// </summary>
    /// <param name="callback">
    /// Optional hook.  Return <see cref="ReloadDecision.Reload"/> (default), 
    /// <see cref="ReloadDecision.Ignore"/>, or <see cref="ReloadDecision.Postpone"/>.
    /// </param>
    public IniConfigBuilder MonitorFile(FileChangedCallback? callback = null)
    {
        _monitorFile = true;
        _fileChangedCallback = callback;
        return this;
    }

    // ── save-on-exit ──────────────────────────────────────────────────────────

    /// <summary>
    /// Instructs the <see cref="IniConfig"/> to automatically call <see cref="IniConfig.Save"/>
    /// when the process exits (via <see cref="AppDomain.CurrentDomain"/> <c>ProcessExit</c>).
    /// The handler is unregistered when the <see cref="IniConfig"/> is disposed.
    /// </summary>
    public IniConfigBuilder SaveOnExit()
    {
        _saveOnExit = true;
        return this;
    }

    // ── auto-save timer ───────────────────────────────────────────────────────

    /// <summary>
    /// Starts an internal timer that periodically checks for unsaved changes and, when present,
    /// calls <see cref="IniConfig.Save"/> automatically.
    /// </summary>
    /// <param name="interval">
    /// How often to check for pending changes.  A value of <see cref="Timeout.InfiniteTimeSpan"/>
    /// disables the timer.
    /// </param>
    public IniConfigBuilder AutoSaveInterval(TimeSpan interval)
    {
        _autoSaveInterval = interval;
        return this;
    }

    // ── migration support ─────────────────────────────────────────────────────

    /// <summary>
    /// Registers a callback invoked for every key in the INI file that has no matching property
    /// on a registered section interface.  Useful for central migration logging or handling.
    /// See the Migration wiki page for examples.
    /// </summary>
    /// <param name="callback">Delegate receiving the section name, key, and raw value.</param>
    public IniConfigBuilder OnUnknownKey(UnknownKeyCallback callback)
    {
        _unknownKeyCallback = callback ?? throw new ArgumentNullException(nameof(callback));
        return this;
    }

    /// <summary>
    /// Opts in to writing a <c>[__metadata__]</c> section as the first section in the INI file
    /// on every save.  The section contains <c>Version</c>, <c>CommitHash</c>, <c>CreatedBy</c>,
    /// and a locale-formatted <c>SavedOn</c> timestamp.  After load the values are exposed via
    /// <see cref="IniConfig.Metadata"/> for version-gated migration logic in
    /// <see cref="Interfaces.IAfterLoad"/> hooks.
    /// </summary>
    /// <remarks>
    /// <para>
    /// When <paramref name="version"/> is <c>null</c>, the entry assembly's
    /// <see cref="System.Reflection.AssemblyInformationalVersionAttribute.InformationalVersion"/>
    /// is used.  If that attribute is not present, <see cref="System.Reflection.AssemblyName.Version"/>
    /// is used as a fallback.
    /// </para>
    /// <para>
    /// The commit hash is automatically extracted from the informational version when it contains
    /// a <c>+</c> separator (e.g. <c>"1.2.0+abc1234def5678"</c> → commit hash <c>"abc1234def5678"</c>).
    /// </para>
    /// See the Migration wiki page for full examples.
    /// </remarks>
    /// <param name="version">
    /// Semantic version string to write (e.g. <c>"1.2.0"</c>); <c>null</c> = auto-detect from entry assembly.
    /// </param>
    /// <param name="applicationName">Application name to write as <c>CreatedBy</c>; <c>null</c> = entry assembly name.</param>
    public IniConfigBuilder EnableMetadata(string? version = null, string? applicationName = null)
    {
        var entryAssembly = System.Reflection.Assembly.GetEntryAssembly();

        string? resolvedVersion = version;
        string? resolvedCommitHash = null;

        if (resolvedVersion == null)
        {
            // Prefer AssemblyInformationalVersionAttribute (carries SemVer + optional commit hash).
            var infoVersion = entryAssembly?
                .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
                .OfType<System.Reflection.AssemblyInformationalVersionAttribute>()
                .FirstOrDefault()?.InformationalVersion;

            if (!string.IsNullOrEmpty(infoVersion))
            {
                // InformationalVersion may carry a commit hash after '+' (e.g. "1.2.0+abc1234").
                var plusIdx = infoVersion!.IndexOf('+');
                if (plusIdx >= 0)
                {
                    resolvedVersion    = infoVersion.Substring(0, plusIdx);
                    resolvedCommitHash = infoVersion.Substring(plusIdx + 1);
                }
                else
                {
                    resolvedVersion = infoVersion;
                }
            }
            else
            {
                // Fallback: plain assembly version.
                resolvedVersion = entryAssembly?.GetName().Version?.ToString();
            }
        }

        _metadataConfig = new IniMetadataConfig
        {
            Version         = resolvedVersion,
            ApplicationName = applicationName ?? entryAssembly?.GetName().Name,
            CommitHash      = resolvedCommitHash,
        };
        return this;
    }

    // ── diagnostic listeners ──────────────────────────────────────────────────

    /// <summary>
    /// Registers a listener that will be called for diagnostic events such as file loaded,
    /// file not found, saved, reloaded, unknown keys, type-conversion failures, and errors.
    /// Multiple listeners may be registered; they are invoked in registration order.
    /// </summary>
    /// <remarks>
    /// There is zero overhead when no listener is registered.
    /// See the project wiki page <em>Listeners</em> for the full callback reference and examples.
    /// </remarks>
    /// <param name="listener">The listener to register; must not be <c>null</c>.</param>
    public IniConfigBuilder AddListener(IIniConfigListener listener)
    {
        if (listener is null) throw new ArgumentNullException(nameof(listener));
        _listeners.Add(listener);
        return this;
    }

    // ── empty-when-null ───────────────────────────────────────────────────────

    /// <summary>
    /// Instructs every reference-type property (string, list, array, dictionary) across
    /// <em>all</em> registered sections to return an empty value instead of <c>null</c>
    /// when no INI key is present and the property has no explicit
    /// <see cref="Attributes.IniValueAttribute.DefaultValue"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the config-wide equivalent of applying
    /// <c>[IniValue(EmptyWhenNull = true)]</c> to every reference-type property in every
    /// registered section.  It is useful when you want empty-over-null semantics throughout
    /// the application without annotating each property individually.
    /// </para>
    /// <para>
    /// The empty representation by type:
    /// </para>
    /// <list type="table">
    ///   <listheader><term>Property type</term><description>Empty representation</description></listheader>
    ///   <item><term><c>string</c></term><description><see cref="string.Empty"/></description></item>
    ///   <item><term><c>List&lt;T&gt;</c> / <c>IList&lt;T&gt;</c> / collection interfaces</term><description>Empty <see cref="System.Collections.Generic.List{T}"/></description></item>
    ///   <item><term><c>T[]</c></term><description>Empty array</description></item>
    ///   <item><term><c>Dictionary&lt;K,V&gt;</c> / <c>IDictionary&lt;K,V&gt;</c></term><description>Empty <see cref="System.Collections.Generic.Dictionary{TKey,TValue}"/></description></item>
    /// </list>
    /// <para>
    /// Value-type properties (e.g. <c>int</c>, <c>bool</c>, <c>double</c>) are never affected.
    /// </para>
    /// <para>
    /// To scope this behaviour to a single section use <c>[IniSection(EmptyWhenNull = true)]</c>.
    /// To scope it to a single property use <c>[IniValue(EmptyWhenNull = true)]</c>.
    /// </para>
    /// </remarks>
    public IniConfigBuilder EmptyWhenNull()
    {
        _globalEmptyWhenNull = true;
        return this;
    }

    /// <summary>
    /// Registers an <see cref="IIniSection"/> instance under the explicit interface type
    /// <typeparamref name="T"/>. The generated concrete class must be passed; it will be
    /// populated when the file is loaded.
    /// </summary>
    public IniConfigBuilder RegisterSection<T>(T section) where T : IIniSection
    {
        if (section is null) throw new ArgumentNullException(nameof(section));
        // When T is a concrete class rather than an interface, prefer registering under
        // the most-specific IIniSection-derived interface so that GetSection<IMySection>()
        // works as expected — consistent with the behaviour of the non-generic overload.
        Type keyType = typeof(T);
        if (!keyType.IsInterface)
        {
            keyType = keyType.GetInterfaces()
                .FirstOrDefault(i => typeof(IIniSection).IsAssignableFrom(i) && i != typeof(IIniSection))
                ?? keyType;
        }
        _sections[keyType] = section;
        return this;
    }

    /// <summary>
    /// Registers an <see cref="IIniSection"/> instance. The library will infer the
    /// interface type by inspecting the instance's implemented interfaces.
    /// Prefer the generic overload <see cref="RegisterSection{T}"/> for explicit control.
    /// </summary>
#if NET
    [RequiresUnreferencedCode(
        "Inspects implemented interfaces at runtime to infer the section type. " +
        "Use the generic RegisterSection<T> overload instead to preserve trim/AOT compatibility.")]
#endif
    public IniConfigBuilder RegisterSection(IIniSection section)
    {
        if (section is null) throw new ArgumentNullException(nameof(section));

        // Infer the most-specific IIniSection-derived interface
        var ifaceType = section.GetType().GetInterfaces()
            .FirstOrDefault(i => typeof(IIniSection).IsAssignableFrom(i) && i != typeof(IIniSection))
            ?? section.GetType();

        _sections[ifaceType] = section;
        return this;
    }

    // ── build / create ────────────────────────────────────────────────────────

    /// <summary>
    /// Creates and registers the <see cref="IniConfig"/> in the global registry
    /// <em>without</em> loading any INI files.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Use this instead of <see cref="Build"/> when plugins or other components need to
    /// register their own INI sections before the files are parsed.
    /// See the project wiki page <em>Plugin-Registrations</em> for the typical three-phase flow.
    /// </para>
    /// <para>
    /// The returned <see cref="IniConfig"/> is immediately accessible via
    /// <see cref="IniConfigRegistry.Get"/> so that plugins can discover it during phase 2.
    /// </para>
    /// </remarks>
    /// <returns>The newly created (not yet loaded) <see cref="IniConfig"/>.</returns>
    public IniConfig Create()
    {
        var config = CreateCore();
        IniConfigRegistry.Register(_fileName, config);
        return config;
    }

    /// <summary>
    /// Builds, loads and registers the <see cref="IniConfig"/> in the global registry.
    /// Returns the fully-populated <see cref="IniConfig"/>.
    /// </summary>
    public IniConfig Build()
    {
        var config = CreateCore();
        IniConfigRegistry.Register(_fileName, config);
        return config.Load();
    }

    /// <summary>
    /// Asynchronously builds, loads and registers the <see cref="IniConfig"/> in the global registry.
    /// Returns the fully-populated <see cref="IniConfig"/> once loading is complete.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The <see cref="IniConfig"/> is registered in <see cref="IniConfigRegistry"/> and its
    /// <see cref="IniConfig.InitialLoadTask"/> is set <em>before</em> any I/O begins.
    /// This enables dependency-injection scenarios where the configuration object (or its sections)
    /// must be available for injection before loading is complete.
    /// See the project wiki page <em>Singleton-and-DI</em> for a complete example.
    /// </para>
    /// <para>
    /// Async lifecycle hooks (<see cref="IAfterLoadAsync"/>) are preferred; when a section
    /// implements only the synchronous <see cref="IAfterLoad"/> hook, that is called instead.
    /// </para>
    /// </remarks>
    /// <param name="cancellationToken">Token to cancel the async operation.</param>
    public async Task<IniConfig> BuildAsync(CancellationToken cancellationToken = default)
    {
        var config = CreateCore();

        // Register in the global registry and expose InitialLoadTask BEFORE any I/O starts.
        // This lets DI consumers get a reference to the config (or its sections) and
        // await InitialLoadTask to know when values are ready.
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        config.SetInitialLoadTask(tcs.Task);
        IniConfigRegistry.Register(_fileName, config);

        try
        {
            await config.LoadAsync(cancellationToken).ConfigureAwait(false);
            tcs.SetResult(true);
        }
        catch (Exception ex)
        {
            // Unregister from registry on failure and propagate the exception via InitialLoadTask.
            IniConfigRegistry.Unregister(_fileName);
            tcs.SetException(ex);
            throw;
        }

        return config;
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates the <see cref="IniConfig"/>, transfers all builder state to it, and seeds all
    /// registered sections — without touching the file system.
    /// </summary>
    private IniConfig CreateCore()
    {
        if (_lockFile && _monitorFile)
            throw new InvalidOperationException(
                $"{nameof(LockFile)} and {nameof(MonitorFile)} are mutually exclusive: " +
                "a read-locked file cannot be modified by external processes, so monitoring for changes is meaningless.");

        // _fileName is the basename (no extension); IniConfig.FileName needs the extension for file I/O.
        var config = new IniConfig(_fileName + ".ini");
        config.Encoding = _encoding ?? Encoding.UTF8;
        config.WritablePath = _writablePath;
        config.ShouldLockFile = _lockFile;
        config.ShouldMonitorFile = _monitorFile;
        config.PendingMonitorCallback = _fileChangedCallback;
        config.ShouldSaveOnExit = _saveOnExit;
        config.ConfiguredAutoSaveInterval = _autoSaveInterval;
        config.UnknownKeyHandler = _unknownKeyCallback;
        config.MetadataConfig = _metadataConfig;
        config.GlobalEmptyWhenNull = _globalEmptyWhenNull;

        config.SearchPaths.AddRange(_searchPaths);
        config.DefaultFilePaths.AddRange(_defaultFilePaths);
        config.ConstantFilePaths.AddRange(_constantFilePaths);
        config.ValueSources.AddRange(_valueSources);
        config.ValueSourcesAsync.AddRange(_valueSourcesAsync);
        config.Listeners.AddRange(_listeners);

        // Seed sections (no I/O — Load() will reset and populate them)
        foreach (var kvp in _sections)
            config.Sections[kvp.Key] = kvp.Value;

        return config;
    }
}
