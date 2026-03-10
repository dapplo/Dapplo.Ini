// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Dapplo.IniConfig.Interfaces;
using Dapplo.IniConfig.Parsing;
#if NET
using System.Diagnostics.CodeAnalysis;
#endif

namespace Dapplo.IniConfig.Configuration;

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

    // Maps interface type → section instance
    private readonly Dictionary<Type, IIniSection> _sections = new();

    private bool _lockFile;
    private bool _monitorFile;
    private FileChangedCallback? _fileChangedCallback;

    // Explicit write-target path (overrides the search-path fallback)
    private string? _writablePath;

    internal IniConfigBuilder(string fileName)
    {
        _fileName = fileName;
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

    // ── sections ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Registers an <see cref="IIniSection"/> instance under the explicit interface type
    /// <typeparamref name="T"/>. The generated concrete class must be passed; it will be
    /// populated when the file is loaded.
    /// </summary>
    public IniConfigBuilder RegisterSection<T>(T section) where T : IIniSection
    {
        if (section is null) throw new ArgumentNullException(nameof(section));
        _sections[typeof(T)] = section;
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

    // ── build ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds, loads and registers the <see cref="IniConfig"/> in the global registry.
    /// Returns the fully-populated <see cref="IniConfig"/>.
    /// </summary>
    public IniConfig Build()
    {
        var config = new IniConfig(_fileName);
        config.SearchPaths.AddRange(_searchPaths);
        config.DefaultFilePaths.AddRange(_defaultFilePaths);
        config.ConstantFilePaths.AddRange(_constantFilePaths);
        config.ValueSources.AddRange(_valueSources);

        // Seed sections with defaults
        foreach (var kvp in _sections)
        {
            kvp.Value.ResetToDefaults();
            config.Sections[kvp.Key] = kvp.Value;
        }

        // Load default files (layered)
        foreach (var path in _defaultFilePaths)
        {
            if (File.Exists(path))
                ApplyIniFile(config, IniFileParser.ParseFile(path));
        }

        // Load user file
        var resolved = ResolveFilePath(_fileName, _searchPaths);
        if (resolved != null)
        {
            config.LoadedFromPath = resolved;
            ApplyIniFile(config, IniFileParser.ParseFile(resolved));
        }
        else
        {
            // Determine write target for future saves:
            // 1. explicit SetWritablePath wins
            // 2. fall back to first existing search directory
            if (_writablePath != null)
            {
                config.LoadedFromPath = _writablePath;
            }
            else
            {
                var firstWritable = _searchPaths.FirstOrDefault(p => Directory.Exists(p));
                if (firstWritable != null)
                    config.LoadedFromPath = Path.Combine(firstWritable, _fileName);
            }
        }

        // Apply constant files (admin overrides, last wins)
        foreach (var path in _constantFilePaths)
        {
            if (File.Exists(path))
                ApplyIniFile(config, IniFileParser.ParseFile(path));
        }

        // Apply external value sources
        config.ApplyValueSources();

        // Fire IAfterLoad hooks
        foreach (var section in config.Sections.Values)
        {
            if (section is IAfterLoad afterLoad)
                afterLoad.OnAfterLoad();
        }

        // Acquire file lock (if requested)
        if (_lockFile)
            config.AcquireFileLock();

        // Start file monitoring (if requested)
        if (_monitorFile)
            config.StartMonitoring(_fileChangedCallback);

        IniConfigRegistry.Register(_fileName, config);
        return config;
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private static string? ResolveFilePath(string fileName, IEnumerable<string> searchPaths)
    {
        foreach (var dir in searchPaths)
        {
            var candidate = Path.Combine(dir, fileName);
            if (File.Exists(candidate))
                return candidate;
        }
        return null;
    }

    private static void ApplyIniFile(IniConfig config, IniFile iniFile)
    {
        foreach (var section in config.Sections.Values)
        {
            var iniSection = iniFile.GetSection(section.SectionName);
            if (iniSection == null) continue;

            foreach (var entry in iniSection.Entries)
                section.SetRawValue(entry.Key, entry.Value);
        }
    }
}
