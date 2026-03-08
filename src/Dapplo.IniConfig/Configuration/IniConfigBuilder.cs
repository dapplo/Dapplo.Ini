// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Dapplo.IniConfig.Interfaces;
using Dapplo.IniConfig.Parsing;

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

    // Maps interface type → section instance
    private readonly Dictionary<Type, IIniSection> _sections = new();

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
            // Use first writable search path as target for future saves
            var firstWritable = _searchPaths.FirstOrDefault(p => Directory.Exists(p));
            if (firstWritable != null)
                config.LoadedFromPath = Path.Combine(firstWritable, _fileName);
        }

        // Apply constant files (admin overrides, last wins)
        foreach (var path in _constantFilePaths)
        {
            if (File.Exists(path))
                ApplyIniFile(config, IniFileParser.ParseFile(path));
        }

        // Fire IAfterLoad hooks
        foreach (var section in config.Sections.Values)
        {
            if (section is IAfterLoad afterLoad)
                afterLoad.OnAfterLoad();
        }

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
