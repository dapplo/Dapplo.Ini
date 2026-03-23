// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Globalization;
using System.Text;
using Dapplo.Ini.Interfaces;

namespace Dapplo.Ini.Internationalization.Configuration;

/// <summary>
/// Manages one or more language sections loaded from full <c>.ini</c> language packs.
/// </summary>
/// <remarks>
/// <para>
/// Language files are standard <c>.ini</c> files. Every translation key
/// <strong>must</strong> be inside a <c>[SectionName]</c> block — keys outside any
/// section header are silently ignored.
/// </para>
/// <para>
/// File naming is controlled by two separate concerns:
/// </para>
/// <list type="bullet">
///   <item>
///     <term>File selection</term>
///     <description>
///     Determined by <see cref="LanguageSectionBase.ModuleName"/>:
///     <c>null</c> → <c>{basename}.{ietf}.ini</c>;
///     non-null → <c>{basename}.{moduleName}.{ietf}.ini</c>.
///     </description>
///   </item>
///   <item>
///     <term>Section routing</term>
///     <description>
///     Determined by <see cref="LanguageSectionBase.SectionName"/>:
///     only keys inside the matching <c>[SectionName]</c> block are loaded.
///     </description>
///   </item>
/// </list>
/// <para>
/// Values support escape sequences: <c>\n</c> → newline, <c>\t</c> → tab, <c>\\</c> → backslash.
/// Keys are normalized: trimmed, underscores and dashes removed, lowercased.
/// </para>
/// <para>
/// Supports a two-phase registration pattern for plugin/addon scenarios:
/// use <see cref="LanguageConfigBuilder.Create()"/> to create the config without loading,
/// let plugins call <see cref="RegisterSection{T}"/> to register their own sections,
/// and then call <see cref="Load"/> (or <see cref="LoadAsync"/>) to load all sections at once.
/// </para>
/// </remarks>
public sealed class LanguageConfig : IDisposable
{
    private readonly string _basename;
    private readonly string _baseLanguage;
    private string _currentLanguage;
    private readonly string? _fallbackLanguage;   // null = use _baseLanguage as fallback

    // Default directory used for sections that don't specify their own.
    private readonly string? _defaultDirectory;

    // Maps section type \u2192 (section instance, directory for its language files)
    private readonly Dictionary<Type, (LanguageSectionBase Section, string Directory)> _sections = new();

    // File watchers keyed by directory
    private readonly Dictionary<string, FileSystemWatcher> _watchers = new(StringComparer.OrdinalIgnoreCase);
    private readonly bool _monitorFiles;

    // Diagnostic listeners
    private readonly List<IIniConfigListener> _listeners = new();

    // Debounce timer to coalesce rapid change events
    private System.Threading.Timer? _debounceTimer;
    private const int DebounceMs = 200;

    private volatile bool _disposed;

    /// <summary>
    /// Raised after the language is reloaded (either via <see cref="SetLanguage"/> or a file-change
    /// notification when monitoring is enabled).
    /// </summary>
    public event EventHandler? LanguageChanged;

    // \u2500\u2500 Constructor (internal \u2014 use LanguageConfigBuilder) \u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500

    internal LanguageConfig(
        string basename,
        string baseLanguage,
        string currentLanguage,
        string? fallbackLanguage,
        bool monitorFiles,
        string? defaultDirectory,
        IEnumerable<(Type Type, LanguageSectionBase Section, string? Directory)> sections,
        IEnumerable<IIniConfigListener>? listeners = null)
    {
        _basename = basename;
        _baseLanguage = baseLanguage;
        _currentLanguage = currentLanguage;
        _fallbackLanguage = fallbackLanguage;
        _monitorFiles = monitorFiles;
        _defaultDirectory = defaultDirectory;

        foreach (var (type, section, dir) in sections)
            _sections[type] = (section, dir ?? defaultDirectory ?? string.Empty);

        if (listeners != null)
            _listeners.AddRange(listeners);
    }

    // ── Listener helpers ──────────────────────────────────────────────────────

    /// <summary>
    /// Notifies all registered listeners (zero-overhead guard when none are registered).
    /// </summary>
    private void NotifyListeners(Action<IIniConfigListener> notify)
    {
        if (_listeners.Count == 0) return;
        foreach (var listener in _listeners)
            notify(listener);
    }

    // \u2500\u2500 Public API \u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500

    /// <summary>The IETF language tag that is currently active.</summary>
    public string CurrentLanguage => _currentLanguage;

    /// <summary>The base (reference) language specified at build time.</summary>
    public string BaseLanguage => _baseLanguage;

    /// <summary>
    /// Returns the language section registered under <typeparamref name="T"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when the type has not been registered.</exception>
    public T GetSection<T>() where T : class
    {
        if (_sections.TryGetValue(typeof(T), out var entry))
            return (T)(object)entry.Section;

        throw new InvalidOperationException(
            $"Language section '{typeof(T).Name}' has not been registered.");
    }

    /// <summary>
    /// Registers a language section without loading any values from disk.
    /// </summary>
    /// <remarks>
    /// Use this in plugin/addon scenarios where the host creates the <see cref="LanguageConfig"/>
    /// via <see cref="LanguageConfigBuilder.Create()"/> and plugins then register their own sections
    /// before the host calls <see cref="Load"/> (or <see cref="LoadAsync"/>).
    /// </remarks>
    /// <typeparam name="T">The language section interface or class type.</typeparam>
    /// <param name="section">The generated concrete section instance.</param>
    /// <param name="path">
    /// Optional search path for this section's language files.
    /// When <c>null</c> the default search path of this <see cref="LanguageConfig"/> is used.
    /// </param>
    /// <returns>The <paramref name="section"/> instance (for fluent chaining).</returns>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="section"/> is not a generated language section.
    /// </exception>
    public T RegisterSection<T>(T section, string? path = null) where T : class
    {
        if (section is null) throw new ArgumentNullException(nameof(section));
        if (section is not LanguageSectionBase baseSection)
            throw new ArgumentException(
                $"Section must be a generated language section (must derive from {nameof(LanguageSectionBase)}).",
                nameof(section));

        _sections[typeof(T)] = (baseSection, path ?? _defaultDirectory ?? string.Empty);
        return section;
    }

    /// <summary>
    /// Loads all language sections using the current language.
    /// Automatically called by <see cref="LanguageConfigBuilder.Build"/>.
    /// Call this explicitly when using <see cref="LanguageConfigBuilder.Create()"/> for deferred loading.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when a registered section has no directory configured.
    /// </exception>
    public void Load()
    {
        try
        {
            ValidateSectionDirectories();
            LoadLanguage(_currentLanguage);
        }
        catch (Exception ex)
        {
            NotifyListeners(l => l.OnError("Load", ex));
            throw;
        }

        if (_monitorFiles)
            StartMonitoring();
    }

    /// <summary>
    /// Asynchronously loads all language sections using the current language.
    /// Automatically called by <see cref="LanguageConfigBuilder.BuildAsync"/>.
    /// Call this explicitly when using <see cref="LanguageConfigBuilder.Create()"/> for deferred loading.
    /// </summary>
    /// <returns>This <see cref="LanguageConfig"/> instance (for fluent chaining).</returns>
    public async Task<LanguageConfig> LoadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            ValidateSectionDirectories();
            await LoadLanguageAsync(_currentLanguage, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            NotifyListeners(l => l.OnError("Load", ex));
            throw;
        }

        if (_monitorFiles)
            StartMonitoring();

        return this;
    }

    /// <summary>
    /// Switches to a new language and reloads all language sections.
    /// </summary>
    /// <param name="ietf">IETF language tag (e.g. <c>"de-DE"</c>, <c>"fr"</c>).</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="ietf"/> is null or empty.</exception>
    public void SetLanguage(string ietf)
    {
        if (string.IsNullOrWhiteSpace(ietf))
            throw new ArgumentException("Language tag must not be empty.", nameof(ietf));

        _currentLanguage = ietf;
        try
        {
            LoadLanguage(ietf);
        }
        catch (Exception ex)
        {
            NotifyListeners(l => l.OnError("SetLanguage", ex));
            throw;
        }
        LanguageChanged?.Invoke(this, EventArgs.Empty);
        NotifyListeners(l => l.OnReloaded(ietf));
    }

    /// <summary>
    /// Asynchronously switches to a new language and reloads all language sections.
    /// </summary>
    /// <param name="ietf">IETF language tag.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    public async Task SetLanguageAsync(string ietf, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(ietf))
            throw new ArgumentException("Language tag must not be empty.", nameof(ietf));

        _currentLanguage = ietf;
        try
        {
            await LoadLanguageAsync(ietf, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            NotifyListeners(l => l.OnError("SetLanguage", ex));
            throw;
        }
        LanguageChanged?.Invoke(this, EventArgs.Empty);
        NotifyListeners(l => l.OnReloaded(ietf));
    }

    /// <summary>
    /// Returns a list of available languages by scanning the language file directories.
    /// Each entry contains the IETF language tag and the native name of the language
    /// according to <see cref="CultureInfo.NativeName"/>.
    /// </summary>
    public IReadOnlyList<(string Ietf, string NativeName)> GetAvailableLanguages()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<(string Ietf, string NativeName)>();

        foreach (var entry in _sections.Values)
        {
            var dir = entry.Directory;
            if (!Directory.Exists(dir)) continue;

            foreach (var file in Directory.EnumerateFiles(dir, "*.ini"))
            {
                var ietf = ExtractIetfFromFileName(file);
                if (ietf == null || !seen.Add(ietf)) continue;

                if (!TryGetCultureInfo(ietf, out var ci)) continue;

                result.Add((ietf, ci!.NativeName));
            }
        }

        return result;
    }

    // \u2500\u2500 Section directory validation \u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500

    private void ValidateSectionDirectories()
    {
        foreach (var kvp in _sections)
        {
            if (string.IsNullOrEmpty(kvp.Value.Directory))
                throw new InvalidOperationException(
                    $"No search path configured for language section '{kvp.Key.Name}'. " +
                    "Specify a path via RegisterSection() or LanguageConfigBuilder.AddSearchPath().");
        }
    }

    // \u2500\u2500 Language loading \u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500

    private void LoadLanguage(string language)
    {
        var fallback = _fallbackLanguage ?? _baseLanguage;

        foreach (var kvp in _sections)
        {
            var section = kvp.Value.Section;
            var dir = kvp.Value.Directory;

            section.ClearTranslations();

            // 1. Load fallback/base language first
            LoadIetfIntoSection(section, dir, fallback);

            if (!string.Equals(language, fallback, StringComparison.OrdinalIgnoreCase))
            {
                // 2. Progressive fallback: parent culture (e.g. "fr" before "fr-FR")
                var hyphen = language.IndexOf('-');
                if (hyphen > 0)
                    LoadIetfIntoSection(section, dir, language.Substring(0, hyphen));

                // 3. Most-specific language (overrides all previous)
                LoadIetfIntoSection(section, dir, language);
            }
        }
    }

    private async Task LoadLanguageAsync(string language, CancellationToken cancellationToken)
    {
        var fallback = _fallbackLanguage ?? _baseLanguage;

        foreach (var kvp in _sections)
        {
            var section = kvp.Value.Section;
            var dir = kvp.Value.Directory;

            section.ClearTranslations();

            await LoadIetfIntoSectionAsync(section, dir, fallback, cancellationToken).ConfigureAwait(false);

            if (!string.Equals(language, fallback, StringComparison.OrdinalIgnoreCase))
            {
                var hyphen = language.IndexOf('-');
                if (hyphen > 0)
                    await LoadIetfIntoSectionAsync(section, dir, language.Substring(0, hyphen), cancellationToken).ConfigureAwait(false);

                await LoadIetfIntoSectionAsync(section, dir, language, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Loads translations for one IETF tag into a section.
    /// Uses <see cref="LanguageSectionBase.ModuleName"/> to select the file and
    /// <see cref="LanguageSectionBase.SectionName"/> to select the section within that file.
    /// </summary>
    private void LoadIetfIntoSection(LanguageSectionBase section, string directory, string ietf)
    {
        var filePath = ResolveLanguageFilePath(directory, section.ModuleName, ietf);
        if (filePath == null)
        {
            var fileName = section.ModuleName != null
                ? $"{_basename}.{section.ModuleName}.{ietf}.ini"
                : $"{_basename}.{ietf}.ini";
            NotifyListeners(l => l.OnFileNotFound(fileName));
            return;
        }

        var content = File.ReadAllText(filePath, Encoding.UTF8);
        ParseAndApply(section, content, section.SectionName);
        NotifyListeners(l => l.OnFileLoaded(filePath));
    }

    private async Task LoadIetfIntoSectionAsync(
        LanguageSectionBase section, string directory, string ietf, CancellationToken cancellationToken)
    {
        var filePath = ResolveLanguageFilePath(directory, section.ModuleName, ietf);
        if (filePath == null)
        {
            var fileName = section.ModuleName != null
                ? $"{_basename}.{section.ModuleName}.{ietf}.ini"
                : $"{_basename}.{ietf}.ini";
            NotifyListeners(l => l.OnFileNotFound(fileName));
            return;
        }

        string content;
#if NET
        content = await File.ReadAllTextAsync(filePath, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
#else
        using var reader = new StreamReader(filePath, Encoding.UTF8);
        content = await reader.ReadToEndAsync().ConfigureAwait(false);
#endif
        ParseAndApply(section, content, section.SectionName);
        NotifyListeners(l => l.OnFileLoaded(filePath));
    }

    /// <summary>
    /// Returns the path of the language file for the given IETF tag, or <c>null</c>
    /// when no matching file exists.
    /// </summary>
    /// <param name="directory">Search directory.</param>
    /// <param name="moduleName">
    /// Optional module name: when set the file is <c>{basename}.{moduleName}.{ietf}.ini</c>;
    /// when <c>null</c> the file is <c>{basename}.{ietf}.ini</c>.
    /// </param>
    /// <param name="ietf">IETF language tag.</param>
    private string? ResolveLanguageFilePath(string directory, string? moduleName, string ietf)
    {
        var fileName = moduleName != null
            ? $"{_basename}.{moduleName}.{ietf}.ini"
            : $"{_basename}.{ietf}.ini";

        var path = Path.Combine(directory, fileName);
        return File.Exists(path) ? path : null;
    }

    /// <summary>
    /// Parses a full <c>.ini</c> file and applies matching key=value entries to the section.
    /// Only keys inside the <c>[<paramref name="sectionName"/>]</c> block are read.
    /// Keys outside any section header (or in a different section) are silently ignored.
    /// </summary>
    /// <param name="section">Target section.</param>
    /// <param name="content">Raw file content.</param>
    /// <param name="sectionName">
    /// The section header to match. Only keys inside this section are loaded.
    /// </param>
    private static void ParseAndApply(LanguageSectionBase section, string content, string sectionName)
    {
        bool inScope = false;  // keys outside sections are never in scope

        var span = content.AsSpan();
        while (!span.IsEmpty)
        {
            var line = ReadLine(ref span).Trim();
            if (line.IsEmpty) continue;

            var first = line[0];

            if (first == ';' || first == '#') continue;

            if (first == '[')
            {
                var closeIdx = line.IndexOf(']');
                if (closeIdx > 1)
                {
                    var header = line.Slice(1, closeIdx - 1).Trim().ToString();
                    inScope = string.Equals(header, sectionName, StringComparison.OrdinalIgnoreCase);
                }
                continue;
            }

            if (!inScope) continue;

            var eq = line.IndexOf('=');
            if (eq <= 0) continue;

            var rawKey = line.Slice(0, eq).TrimEnd().ToString();
            var normalizedKey = LanguageSectionBase.NormalizeKey(rawKey);
            var rawValue = line.Slice(eq + 1).ToString();
            var value = UnescapeValue(rawValue);

            section.SetTranslation(normalizedKey, value);
        }
    }

    /// <summary>Processes escape sequences: <c>\n</c>, <c>\t</c>, <c>\\</c>.</summary>
    internal static string UnescapeValue(string raw)
    {
        if (raw.IndexOf('\\') < 0) return raw;

        var sb = new StringBuilder(raw.Length);
        for (var i = 0; i < raw.Length; i++)
        {
            if (raw[i] == '\\' && i + 1 < raw.Length)
            {
                switch (raw[i + 1])
                {
                    case 'n': sb.Append('\n'); i++; continue;
                    case 't': sb.Append('\t'); i++; continue;
                    case '\\': sb.Append('\\'); i++; continue;
                }
            }
            sb.Append(raw[i]);
        }
        return sb.ToString();
    }

    // \u2500\u2500 IETF extraction helpers \u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500

    private string? ExtractIetfFromFileName(string filePath)
    {
        var fileName = Path.GetFileNameWithoutExtension(filePath);
        if (fileName == null) return null;

        if (!fileName.StartsWith(_basename + ".", StringComparison.OrdinalIgnoreCase))
            return null;

        var remainder = fileName.Substring(_basename.Length + 1);
        if (string.IsNullOrEmpty(remainder)) return null;

        var dotIdx = remainder.LastIndexOf('.');
        return dotIdx >= 0 ? remainder.Substring(dotIdx + 1) : remainder;
    }

    private static bool TryGetCultureInfo(string ietf, out CultureInfo? culture)
    {
        try
        {
            culture = CultureInfo.GetCultureInfo(ietf);
            return true;
        }
        catch
        {
            culture = null;
            return false;
        }
    }

    // \u2500\u2500 File monitoring \u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500

    private void StartMonitoring()
    {
        var directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kvp in _sections)
            directories.Add(kvp.Value.Directory);

        _debounceTimer = new System.Threading.Timer(_ =>
        {
            if (!_disposed)
                ReloadCurrentLanguage();
        }, null, Timeout.Infinite, Timeout.Infinite);

        foreach (var dir in directories)
        {
            if (!Directory.Exists(dir) || _watchers.ContainsKey(dir)) continue;

            var watcher = new FileSystemWatcher(dir, "*.ini")
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
                EnableRaisingEvents = true
            };
            watcher.Changed += OnFileChanged;
            _watchers[dir] = watcher;
        }
    }

    private void OnFileChanged(object sender, FileSystemEventArgs e)
        => _debounceTimer?.Change(DebounceMs, Timeout.Infinite);

    private void ReloadCurrentLanguage()
    {
        LoadLanguage(_currentLanguage);
        LanguageChanged?.Invoke(this, EventArgs.Empty);
        NotifyListeners(l => l.OnReloaded(_currentLanguage));
    }

    // \u2500\u2500 Line reader \u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500

    private static ReadOnlySpan<char> ReadLine(ref ReadOnlySpan<char> remaining)
    {
        var newLine = remaining.IndexOfAny('\r', '\n');
        if (newLine < 0)
        {
            var line = remaining;
            remaining = ReadOnlySpan<char>.Empty;
            return line;
        }

        var result = remaining.Slice(0, newLine);
        remaining = remaining.Slice(newLine + 1);
        if (!remaining.IsEmpty && remaining[0] == '\n')
            remaining = remaining.Slice(1);

        return result;
    }

    // \u2500\u2500 IDisposable \u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        var timer = _debounceTimer;
        _debounceTimer = null;
        timer?.Change(Timeout.Infinite, Timeout.Infinite);
        timer?.Dispose();

        foreach (var watcher in _watchers.Values)
            watcher.Dispose();
        _watchers.Clear();
    }
}
