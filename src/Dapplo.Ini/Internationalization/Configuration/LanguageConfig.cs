// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Globalization;
using System.Text;
using Dapplo.Ini.Parsing;
using System.Text.RegularExpressions;
using Dapplo.Ini.Interfaces;
using Dapplo.Ini.Internationalization.Interfaces;

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
/// With <see cref="LanguageConfigBuilder.AllowLateSectionRegistration"/> sections can also be
/// registered after the load.
/// </para>
/// <para>
/// Thread safety: reading the files for a load, a language switch, a file-change reload or a section
/// registration is serialised. The translations are applied, <c>PropertyChanged</c> / <see cref="LanguageChanged"/>
/// are raised and listeners are called afterwards, outside that lock, on the calling thread: handlers may
/// marshal to the UI thread, switch the language or register sections. Every section always applies the
/// translations of the latest operation, so it ends up in the current language. Reading translations never
/// blocks: every section swaps in a complete set of translations at once.
/// </para>
/// </remarks>
public sealed class LanguageConfig : IDisposable
{
    /// <summary>
    /// The name of the reserved section in a language file that describes the language itself, e.g.
    /// <c>[__language__]</c> with <c>Description=Fränkisch</c>. It is never routed to a registered section.
    /// </summary>
    public const string LanguageSectionName = "__language__";

    private const string DescriptionKey = "description";

    private readonly string _basename;
    private readonly string _baseLanguage;
    private string _requestedLanguage;
    private volatile string _currentLanguage;
    private readonly string? _fallbackLanguage;   // null = the base language is the fallback
    private readonly bool _allowLateSectionRegistration;
    private readonly bool _mergeSearchPaths;
    private readonly bool _resolveLanguages;

    // Default directories used for sections that don't specify their own.
    private readonly IReadOnlyList<string> _searchPaths;

    // Registered sections in registration order. Copy-on-write (replaced under _registrationLock), so
    // readers (UI threads looking up translations) never need a lock.
    private volatile SectionEntry[] _entries = [];
    private readonly object _registrationLock = new();

    // File watchers keyed by directory (only touched under the gate)
    private readonly Dictionary<string, FileSystemWatcher> _watchers = new(StringComparer.OrdinalIgnoreCase);
    private readonly bool _monitorFiles;

    // Diagnostic listeners
    private readonly List<IIniConfigListener> _listeners = new();

    // Debounce timer to coalesce rapid change events
    private System.Threading.Timer? _debounceTimer;
    private const int DebounceMs = 200;

    private volatile bool _disposed;
    private volatile bool _isLoaded;

    // ── Lifecycle gate ────────────────────────────────────────────────────────
    // Serialises reading files and changing state for Load, SetLanguage, file-change reloads and section
    // registrations. No user code (handlers, listeners) runs while it is held: an operation collects what to
    // apply and report in a Batch, which runs after the gate is released.

    private readonly SemaphoreSlim _gate = new(1, 1);

    // Only used under the gate.
    private readonly TranslationLoader _loader = new();

    /// <summary>Listener notifications and actions of one operation, run after the gate is released.</summary>
    private sealed class Batch
    {
        private readonly LanguageConfig _config;
        private readonly List<Action> _notifications = new();
        private readonly List<Action> _actions = new();

        public Batch(LanguageConfig config) => _config = config;

        public void Notify(Action<IIniConfigListener> notify)
        {
            if (_config._listeners.Count > 0) _notifications.Add(() => _config.NotifyListeners(notify));
        }

        public void NotifyLanguage(Action<ILanguageConfigListener> notify)
        {
            if (_config._listeners.Count > 0) _notifications.Add(() => _config.NotifyLanguageListeners(notify));
        }

        /// <summary>Runs after the notifications, only when the operation succeeded.</summary>
        public void Then(Action action) => _actions.Add(action);

        // Every notification and action runs, even when one throws (e.g. a faulty listener or PropertyChanged
        // handler), so an operation is never left half applied; the failures are reported afterwards.
        public void RunNotifications(List<Exception> failures) => RunAll(_notifications, failures);

        public void RunActions(List<Exception> failures) => RunAll(_actions, failures);

        private static void RunAll(List<Action> actions, List<Exception> failures)
        {
            foreach (var action in actions)
            {
                try { action(); }
                catch (Exception ex) { failures.Add(ex); }
            }
        }
    }

    /// <summary>
    /// Runs <paramref name="work"/> under the gate, then the batch's notifications and (on success) its actions.
    /// A failure of <paramref name="work"/> is reported via <see cref="IIniConfigListener.OnError"/> and re-thrown.
    /// </summary>
    private void Execute(string operation, Action<Batch> work) => Execute(operation, work, rethrow: true);

    /// <param name="operation">The operation name for <see cref="IIniConfigListener.OnError"/>.</param>
    /// <param name="work">The work to do under the gate.</param>
    /// <param name="rethrow"><c>false</c> to only report a failure of <paramref name="work"/> (background work).</param>
    private void Execute(string operation, Action<Batch> work, bool rethrow)
    {
        var batch = new Batch(this);
        Exception? error = null;
        _gate.Wait();
        try
        {
            work(batch);
        }
        catch (Exception ex)
        {
            error = ex;
        }
        finally
        {
            _gate.Release();
        }
        Complete(operation, batch, error, rethrow);
    }

    private async Task ExecuteAsync(string operation, Func<Batch, Task> work, CancellationToken cancellationToken)
    {
        var batch = new Batch(this);
        Exception? error = null;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await work(batch).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            error = ex;
        }
        finally
        {
            _gate.Release();
        }
        Complete(operation, batch, error, rethrow: true);
    }

    private void Complete(string operation, Batch batch, Exception? error, bool rethrow)
    {
        var failures = new List<Exception>();
        batch.RunNotifications(failures);
        if (error == null)
            batch.RunActions(failures);
        else
            failures.Insert(0, error);
        if (failures.Count == 0) return;

        foreach (var failure in failures)
        {
            try { NotifyListeners(l => l.OnError(operation, failure)); }
            catch { /* a failing listener must not hide the original exception */ }
        }
        if (rethrow)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failures[0]).Throw();
    }

    /// <summary>
    /// Applies the latest translations built for a section. Runs outside the gate (it raises PropertyChanged);
    /// when another operation built newer translations meanwhile, those are applied too, so concurrent and
    /// re-entrant operations still leave every section in the current language.
    /// </summary>
    /// <remarks>
    /// Only one thread applies to a section at a time (without holding a lock while handlers run): a thread that
    /// finds another one applying returns, and the active one also applies the newer translations. This keeps the
    /// compare-swap-notify of the generated <c>UpdateTranslations</c> from interleaving, so the last
    /// <c>PropertyChanged</c> always matches the final text. A handler that switches the language re-entrantly
    /// (same thread) is picked up by the running loop too.
    /// </remarks>
    private static void ApplyLatest(SectionEntry entry)
    {
        while (true)
        {
            if (Interlocked.CompareExchange(ref entry.Applying, 1, 0) != 0)
                return;   // the active applier picks up entry.Latest
            try
            {
                var latest = entry.Latest;
                while (latest != null && !ReferenceEquals(latest, entry.Applied))
                {
                    entry.Applied = latest;
                    entry.Section.ApplyBuiltTranslations(latest);
                    latest = entry.Latest;
                }
            }
            finally
            {
                Volatile.Write(ref entry.Applying, 0);
            }

            // Newer translations published while this thread was finishing (their publisher saw it applying)
            var pending = entry.Latest;
            if (pending == null || ReferenceEquals(pending, entry.Applied))
                return;
        }
    }

    private static void ThenApply(Batch batch, IEnumerable<SectionEntry> entries)
    {
        foreach (var entry in entries)
            batch.Then(() => ApplyLatest(entry));
    }

    /// <summary>
    /// Raised after the language is reloaded (either via <see cref="SetLanguage"/> or a file-change
    /// notification when monitoring is enabled). Not raised for a section registered after the load.
    /// </summary>
    public event EventHandler? LanguageChanged;

    // ── Constructor (internal — use LanguageConfigBuilder) ────────────────────

    internal LanguageConfig(
        string basename,
        string baseLanguage,
        string currentLanguage,
        string? fallbackLanguage,
        bool monitorFiles,
        IEnumerable<string> searchPaths,
        IEnumerable<(Type Type, LanguageSectionBase Section, string? Directory)> sections,
        IEnumerable<IIniConfigListener>? listeners = null,
        LanguageConfigOptions options = default)
    {
        _basename = basename;
        _baseLanguage = baseLanguage;
        _requestedLanguage = currentLanguage;
        _currentLanguage = currentLanguage;
        _fallbackLanguage = fallbackLanguage;
        _monitorFiles = monitorFiles;
        _searchPaths = searchPaths.ToList();
        _allowLateSectionRegistration = options.AllowLateSectionRegistration;
        _mergeSearchPaths = options.MergeSearchPaths;
        _resolveLanguages = options.ResolveLanguages;

        if (listeners != null)
            _listeners.AddRange(listeners);

        foreach (var (type, section, dir) in sections)
            AddOrReplaceEntry(type, section, dir);
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

    private void NotifyExtendedListeners(Action<IIniConfigExtendedListener> notify)
    {
        if (_listeners.Count == 0) return;
        foreach (var listener in _listeners)
            if (listener is IIniConfigExtendedListener extended)
                notify(extended);
    }

    private void NotifyLanguageListeners(Action<ILanguageConfigListener> notify)
    {
        if (_listeners.Count == 0) return;
        foreach (var listener in _listeners)
            if (listener is ILanguageConfigListener languageListener)
                notify(languageListener);
    }

    // Called by LanguageSectionBase.Format; must never throw.
    internal void ReportFormatFailed(string sectionName, string key, Exception exception)
    {
        try { NotifyLanguageListeners(l => l.OnFormatFailed(sectionName, key, exception)); }
        catch { /* a failing listener must not break the UI that asked for a text */ }
    }

    internal void ReportTranslationNotFound(string? moduleOrSection, string key)
    {
        try { NotifyLanguageListeners(l => l.OnTranslationNotFound(moduleOrSection, key)); }
        catch { /* see ReportFormatFailed */ }
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// The IETF language tag that is currently active. With <see cref="LanguageConfigBuilder.ResolveLanguages"/>
    /// this is the resolved tag (see <see cref="ResolveLanguage"/>) once the config is loaded.
    /// </summary>
    public string CurrentLanguage => _currentLanguage;

    /// <summary>
    /// The IETF language tag that was last requested via <see cref="LanguageConfigBuilder.WithCurrentLanguage"/>
    /// or <see cref="SetLanguage"/>. Differs from <see cref="CurrentLanguage"/> only when
    /// <see cref="LanguageConfigBuilder.ResolveLanguages"/> resolved it to another tag.
    /// </summary>
    public string RequestedLanguage => _requestedLanguage;

    /// <summary>The base (reference) language specified at build time.</summary>
    public string BaseLanguage => _baseLanguage;

    /// <summary><c>true</c> once <see cref="Load"/>, <see cref="LoadAsync"/> or a language switch has completed.</summary>
    public bool IsLoaded => _isLoaded;

    /// <summary>
    /// Returns the language section registered under <typeparamref name="T"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when the type has not been registered.</exception>
    public T GetSection<T>() where T : class
    {
        foreach (var entry in _entries)
            if (entry.Type == typeof(T))
                return (T)(object)entry.Section;

        throw new InvalidOperationException(
            $"Language section '{typeof(T).Name}' has not been registered.");
    }

    /// <summary>Returns the section with the specified section name, or <c>null</c> when not registered.</summary>
    public LanguageSectionBase? GetSection(string sectionName)
        => _entries.Select(entry => entry.Section)
            .FirstOrDefault(section => string.Equals(section.SectionName, sectionName, StringComparison.OrdinalIgnoreCase));

    /// <summary>Returns the section with the specified module name, or <c>null</c> when not registered.</summary>
    public LanguageSectionBase? GetSectionByModule(string moduleName)
        => _entries.Select(entry => entry.Section)
            .FirstOrDefault(section => string.Equals(section.ModuleName, moduleName, StringComparison.OrdinalIgnoreCase));

    /// <summary>Returns a translation by section name or module name and key.</summary>
    public string? this[string moduleOrSection, string key]
        => (GetSection(moduleOrSection) ?? GetSectionByModule(moduleOrSection))?[key];

    // ── Lookup without a section ──────────────────────────────────────────────

    /// <summary>
    /// Looks up a translation by key without knowing the section it belongs to, e.g. for keys built at runtime
    /// (enum values, labels from configuration files, plugin keys).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The key is normalised like any other key (trimmed, <c>_</c> and <c>-</c> removed, case-insensitive);
    /// dots are kept.
    /// </para>
    /// <para>
    /// A key of the form <c>prefix.key</c> whose <c>prefix</c> is the <see cref="LanguageSectionBase.ModuleName"/>
    /// or <see cref="LanguageSectionBase.SectionName"/> of a registered section only searches those sections
    /// for <c>key</c> (e.g. <c>imgur.history</c>). Otherwise the whole key (dots included) is searched in all sections.
    /// </para>
    /// <para>
    /// Search order when a key exists in more than one section: sections without a module name first, then
    /// module sections, each group in registration order; the first section that has the key wins. With a
    /// prefix, the sections whose module name matches come before the sections whose section name matches.
    /// </para>
    /// </remarks>
    /// <param name="key">The key, optionally prefixed with a module or section name and a dot.</param>
    /// <param name="value">The translation, or <c>null</c> when not found.</param>
    /// <returns><c>true</c> when the key was found.</returns>
    public bool TryGetTranslation(string key, out string? value)
    {
        value = null;
        if (key is null) return false;
        var trimmed = key.AsSpan().Trim();
        if (trimmed.IsEmpty) return false;

        // Allocation-free: spans, loops instead of LINQ, keys normalized in a stack buffer by the section.
        var entries = _entries;
        var dot = trimmed.IndexOf('.');
        if (dot > 0 && dot < trimmed.Length - 1)
        {
            var prefix = trimmed.Slice(0, dot);
            var scopedKey = trimmed.Slice(dot + 1);
            var scoped = false;
            foreach (var entry in entries)
            {
                if (entry.Section.ModuleName is not { } module || !prefix.Equals(module.AsSpan(), StringComparison.OrdinalIgnoreCase)) continue;
                scoped = true;
                if (entry.Section.TryGetByKey(scopedKey, out value)) return true;
            }
            foreach (var entry in entries)
            {
                if (!prefix.Equals(entry.Section.SectionName.AsSpan(), StringComparison.OrdinalIgnoreCase)) continue;
                scoped = true;
                if (entry.Section.TryGetByKey(scopedKey, out value)) return true;
            }
            if (scoped)
            {
                value = null;
                return false;
            }
        }

        foreach (var entry in entries)
            if (entry.Section.ModuleName == null && entry.Section.TryGetByKey(trimmed, out value))
                return true;
        foreach (var entry in entries)
            if (entry.Section.ModuleName != null && entry.Section.TryGetByKey(trimmed, out value))
                return true;

        value = null;
        return false;
    }

    /// <summary>
    /// Returns the translation for <paramref name="key"/> (see <see cref="TryGetTranslation"/> for the key format
    /// and search order), or the sentinel <c>###key###</c> when it is not found.
    /// A missing key is reported via <see cref="ILanguageConfigListener.OnTranslationNotFound"/>.
    /// </summary>
    public string GetTranslation(string key)
    {
        if (TryGetTranslation(key, out var value))
            return value!;
        ReportTranslationNotFound(null, key);
        return $"###{key}###";
    }

    // ── Registration ──────────────────────────────────────────────────────────

    /// <summary>
    /// Registers a language section.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Before the load</b> (after <see cref="LanguageConfigBuilder.Create()"/>) this only registers the
    /// section; the coming <see cref="Load"/> (or <see cref="LoadAsync"/>) loads it with all other sections.
    /// </para>
    /// <para>
    /// <b>After the load</b> with <see cref="LanguageConfigBuilder.AllowLateSectionRegistration"/> the section is
    /// loaded right away for the current language and its fallback chain. Other sections are not reloaded and
    /// <see cref="LanguageChanged"/> is not raised. Later language switches and file-change reloads include it.
    /// </para>
    /// <para>
    /// <b>After the load without that option</b> the section is registered but stays empty (every property returns
    /// <c>###Key###</c>) until the next <see cref="Load"/> or <see cref="SetLanguage"/>. This is reported via
    /// <see cref="IIniConfigExtendedListener.OnSectionAdded"/> with <c>loaded: false</c> and
    /// <see cref="IIniConfigListener.OnError"/> (operation <c>"RegisterSection"</c>, nothing is thrown).
    /// </para>
    /// <para>
    /// Registering a type again replaces the earlier instance. Safe to call from any thread.
    /// </para>
    /// </remarks>
    /// <typeparam name="T">The language section interface or class type.</typeparam>
    /// <param name="section">The generated concrete section instance.</param>
    /// <param name="path">
    /// Optional search path for this section's language files.
    /// When <c>null</c> the default search paths of this <see cref="LanguageConfig"/> are used.
    /// </param>
    /// <returns>The <paramref name="section"/> instance (for fluent chaining).</returns>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="section"/> is not a generated language section, or uses the reserved
    /// section name <see cref="LanguageSectionName"/>.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the section is loaded right away and has no search path.
    /// </exception>
    public T RegisterSection<T>(T section, string? path = null) where T : class
    {
        var baseSection = ValidateSection(section);
        Execute("RegisterSection", batch =>
        {
            if (!_isLoaded || !_allowLateSectionRegistration)
            {
                RegisterWithoutLoading(typeof(T), baseSection, path, batch);
                return;
            }

            var lateEntry = CreateEntry(typeof(T), baseSection, path);
            ValidateDirectories(lateEntry);
            BuildEntries(new[] { lateEntry }, _currentLanguage, batch);
            CompleteLateRegistration(lateEntry, batch);
        });
        return section;
    }

    /// <summary>
    /// Asynchronously registers a language section. Identical to <see cref="RegisterSection{T}"/>, except that a
    /// section registered after the load (with <see cref="LanguageConfigBuilder.AllowLateSectionRegistration"/>)
    /// reads its files asynchronously.
    /// </summary>
    /// <param name="section">The generated concrete section instance.</param>
    /// <param name="path">Optional search path for this section's language files.</param>
    /// <param name="cancellationToken">
    /// Token to cancel the operation; when cancelled before the files were read the section is not registered.
    /// </param>
    /// <returns>The <paramref name="section"/> instance.</returns>
    public async Task<T> RegisterSectionAsync<T>(T section, string? path = null, CancellationToken cancellationToken = default) where T : class
    {
        var baseSection = ValidateSection(section);
        await ExecuteAsync("RegisterSection", async batch =>
        {
            if (!_isLoaded || !_allowLateSectionRegistration)
            {
                RegisterWithoutLoading(typeof(T), baseSection, path, batch);
                return;
            }

            var lateEntry = CreateEntry(typeof(T), baseSection, path);
            ValidateDirectories(lateEntry);
            await BuildEntriesAsync(new[] { lateEntry }, _currentLanguage, batch, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            CompleteLateRegistration(lateEntry, batch);
        }, cancellationToken).ConfigureAwait(false);
        return section;
    }

    // Under the gate: the section is registered; a later operation includes it.
    private void CompleteLateRegistration(SectionEntry lateEntry, Batch batch)
    {
        AddOrReplaceEntry(lateEntry);
        if (_monitorFiles) StartMonitoring();
        var sectionName = lateEntry.Section.SectionName;
        batch.Then(() =>
        {
            ApplyLatest(lateEntry);
            NotifyExtendedListeners(l => l.OnSectionAdded(sectionName, true));
        });
    }

    private static LanguageSectionBase ValidateSection<T>(T section) where T : class
    {
        if (section is null) throw new ArgumentNullException(nameof(section));
        if (section is not LanguageSectionBase baseSection)
            throw new ArgumentException(
                $"Section must be a generated language section (must derive from {nameof(LanguageSectionBase)}).",
                nameof(section));
        if (string.Equals(baseSection.SectionName, LanguageSectionName, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException(
                $"The section name '{LanguageSectionName}' is reserved for the language description.", nameof(section));
        return baseSection;
    }

    private void RegisterWithoutLoading(Type type, LanguageSectionBase section, string? path, Batch batch)
    {
        var entry = AddOrReplaceEntry(type, section, path);
        var sectionName = section.SectionName;
        if (_listeners.Count == 0) return;
        if (_isLoaded)
        {
            // Registered after the load without AllowLateSectionRegistration: the section stays empty until the
            // next Load()/SetLanguage(). Not thrown (existing behaviour), but made visible.
            var problem = new InvalidOperationException(
                $"Language section '{entry.Type.Name}' was registered after '{_basename}' was loaded; it stays empty until the next Load() or SetLanguage(). " +
                $"Register it before Load()/Build(), or enable {nameof(LanguageConfigBuilder)}.{nameof(LanguageConfigBuilder.AllowLateSectionRegistration)}().");
            batch.Notify(l => l.OnError("RegisterSection", problem));
        }
        batch.Then(() => NotifyExtendedListeners(l => l.OnSectionAdded(sectionName, false)));
    }

    private SectionEntry CreateEntry(Type type, LanguageSectionBase section, string? path)
        => new(type, section, DirectoriesFor(path));

    private SectionEntry AddOrReplaceEntry(Type type, LanguageSectionBase section, string? path)
        => AddOrReplaceEntry(CreateEntry(type, section, path));

    private SectionEntry AddOrReplaceEntry(SectionEntry entry)
    {
        entry.Section.Owner = this;
        lock (_registrationLock)
        {
            var entries = _entries;
            var index = Array.FindIndex(entries, e => e.Type == entry.Type);
            var copy = new SectionEntry[index >= 0 ? entries.Length : entries.Length + 1];
            Array.Copy(entries, copy, entries.Length);
            copy[index >= 0 ? index : entries.Length] = entry;
            _entries = copy;
        }
        return entry;
    }

    /// <summary>
    /// The directories for a section: the search paths, or the section's own path. With
    /// <see cref="LanguageConfigBuilder.MergeSearchPaths"/> an own path that is also a search path means
    /// the section uses (and merges) all search paths.
    /// </summary>
    private IReadOnlyList<string> DirectoriesFor(string? path)
    {
        if (path is null) return _searchPaths;
        if (_mergeSearchPaths && _searchPaths.Any(searchPath => SamePath(searchPath, path)))
            return _searchPaths;
        return [path];
    }

    private static bool SamePath(string a, string b)
    {
        try
        {
            return string.Equals(
                Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }
    }

    // ── Load / language switch ────────────────────────────────────────────────

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
        Execute("Load", batch =>
        {
            ValidateSectionDirectories();
            var requested = _requestedLanguage;
            var language = ResolveForLoad(requested, batch);
            var entries = BuildAll(language, batch);
            CommitLanguage(requested, language);
            if (_monitorFiles)
                StartMonitoring();
            ThenApply(batch, entries);
        });
    }

    /// <summary>
    /// Asynchronously loads all language sections using the current language.
    /// Automatically called by <see cref="LanguageConfigBuilder.BuildAsync"/>.
    /// Call this explicitly when using <see cref="LanguageConfigBuilder.Create()"/> for deferred loading.
    /// </summary>
    /// <returns>This <see cref="LanguageConfig"/> instance (for fluent chaining).</returns>
    public async Task<LanguageConfig> LoadAsync(CancellationToken cancellationToken = default)
    {
        await ExecuteAsync("Load", async batch =>
        {
            ValidateSectionDirectories();
            var requested = _requestedLanguage;
            var language = ResolveForLoad(requested, batch);
            var entries = await BuildAllAsync(language, batch, cancellationToken).ConfigureAwait(false);
            CommitLanguage(requested, language);
            if (_monitorFiles)
                StartMonitoring();
            ThenApply(batch, entries);
        }, cancellationToken).ConfigureAwait(false);

        return this;
    }

    /// <summary>
    /// Switches to a new language and reloads all language sections.
    /// </summary>
    /// <remarks>
    /// With <see cref="LanguageConfigBuilder.ResolveLanguages"/> the tag is first resolved to an available
    /// language (see <see cref="ResolveLanguage"/>); <see cref="CurrentLanguage"/> then returns the resolved tag.
    /// </remarks>
    /// <param name="ietf">IETF language tag (e.g. <c>"de-DE"</c>, <c>"fr"</c>).</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="ietf"/> is null or empty.</exception>
    public void SetLanguage(string ietf)
    {
        if (string.IsNullOrWhiteSpace(ietf))
            throw new ArgumentException("Language tag must not be empty.", nameof(ietf));

        Execute("SetLanguage", batch =>
        {
            var language = ResolveForLoad(ietf, batch);
            var entries = BuildAll(language, batch);
            CommitLanguage(ietf, language);
            ThenApply(batch, entries);
            batch.Then(RaiseLanguageChanged);
        });
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

        await ExecuteAsync("SetLanguage", async batch =>
        {
            var language = ResolveForLoad(ietf, batch);
            var entries = await BuildAllAsync(language, batch, cancellationToken).ConfigureAwait(false);
            CommitLanguage(ietf, language);
            ThenApply(batch, entries);
            batch.Then(RaiseLanguageChanged);
        }, cancellationToken).ConfigureAwait(false);
    }

    private void RaiseLanguageChanged()
    {
        LanguageChanged?.Invoke(this, EventArgs.Empty);
        var language = _currentLanguage;
        NotifyListeners(l => l.OnReloaded(language));
    }

    /// <summary>The language to load for <paramref name="requested"/>: resolved when enabled, otherwise as is.</summary>
    private string ResolveForLoad(string requested, Batch batch)
    {
        if (!_resolveLanguages) return requested;
        var resolved = ResolveLanguage(requested);
        batch.NotifyLanguage(l => l.OnLanguageResolved(requested, resolved));
        return resolved;
    }

    /// <summary>Under the gate, after the build succeeded: a failed or cancelled switch keeps the previous language.</summary>
    private void CommitLanguage(string requested, string language)
    {
        _requestedLanguage = requested;
        _currentLanguage = language;
        _isLoaded = true;
    }

    // ── Available languages ───────────────────────────────────────────────────

    /// <summary>
    /// Returns a list of available languages by scanning the language file directories.
    /// Each entry contains the IETF language tag and a display name: the <c>Description</c> from the
    /// <c>[__language__]</c> section of the language file when present, otherwise the
    /// <see cref="CultureInfo.NativeName"/>, otherwise the tag itself.
    /// </summary>
    /// <remarks>See <see cref="GetLanguages"/> for which files define a language.</remarks>
    public IReadOnlyList<(string Ietf, string NativeName)> GetAvailableLanguages()
        => GetLanguages().Select(language => (language.Ietf, language.DisplayName)).ToList();

    /// <summary>
    /// Returns the available languages with their details, sorted by tag.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Base files (<c>{basename}.{ietf}.ini</c>) define the languages. Module files
    /// (<c>{basename}.{module}.{ietf}.ini</c>) only add translations to a language and never add an entry of
    /// their own — except when every registered section is a module section, then the files of the registered
    /// modules define the languages.
    /// </para>
    /// <para>
    /// The description is read from the <c>[__language__]</c> section of the highest priority file that has one.
    /// A tag must look like an IETF tag (a two or three letter language subtag, e.g. <c>de</c>, <c>de-DE</c>,
    /// <c>de-x-franconia</c>); a file name segment that equals a registered module name is never a tag.
    /// </para>
    /// </remarks>
    public IReadOnlyList<LanguageInfo> GetLanguages()
        => ScanLanguages()
            .Select(language => new LanguageInfo(
                language.Ietf,
                TryGetCultureInfo(language.Ietf, out var culture) ? culture!.NativeName : null,
                language.Files.Select(ReadDescription).FirstOrDefault(d => d != null),
                language.HasBaseFile))
            .ToList();

    /// <summary>
    /// Finds the available languages by file name only (no file is read), sorted by tag; the files that define
    /// each language are in priority order.
    /// </summary>
    private List<(string Ietf, List<string> Files, bool HasBaseFile)> ScanLanguages()
    {
        var entries = _entries;
        var baseFilesDefine = entries.Any(e => e.Section.ModuleName == null);
        var modules = new HashSet<string>(
            entries.Select(e => e.Section.ModuleName).Where(m => m != null)!, StringComparer.OrdinalIgnoreCase);

        // Tag (spelling of the first defining file) → defining files in priority order
        var defined = new Dictionary<string, (string Ietf, List<string> Files)>(StringComparer.OrdinalIgnoreCase);
        var withBaseFile = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var dir in entries.SelectMany(e => e.Directories).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(dir)) continue;

            foreach (var file in Directory.EnumerateFiles(dir, "*.ini"))
            {
                if (!TryParseLanguageFileName(file, modules, out var module, out var ietf)) continue;
                var isBaseFile = module == null;
                if (isBaseFile)
                    withBaseFile.Add(ietf);
                else if (!modules.Contains(module!))
                    continue;

                if (isBaseFile != baseFilesDefine) continue;
                if (!defined.TryGetValue(ietf, out var language))
                {
                    language = (ietf, new List<string>());
                    defined[ietf] = language;
                }
                language.Files.Add(file);
            }
        }

        return defined.Values
            .OrderBy(language => language.Ietf, StringComparer.OrdinalIgnoreCase)
            .Select(language => (language.Ietf, language.Files, withBaseFile.Contains(language.Ietf)))
            .ToList();
    }

    /// <summary>
    /// Reads <c>Description</c> from the <c>[__language__]</c> section, stopping at the end of that section.
    /// Lines are read through a pooled buffer; only the description itself is allocated.
    /// </summary>
    private static string? ReadDescription(string filePath)
    {
        try
        {
            using var reader = PooledLineReader.OpenFile(filePath);
            var state = 0;   // 0 = before, 1 = in [__language__], 2 = done
            string? description = null;
            while (state < 2)
            {
                while (state < 2 && reader.TryReadLine(out var line))
                    state = ReadDescriptionLine(line, state, ref description);
                if (reader.IsCompleted) break;
                if (state < 2) reader.Fill();
            }
            return description;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static int ReadDescriptionLine(ReadOnlySpan<char> line, int state, ref string? description)
    {
        var trimmed = line.Trim();
        if (trimmed.IsEmpty || trimmed[0] == ';' || trimmed[0] == '#') return state;
        if (trimmed[0] == '[')
        {
            if (state == 1) return 2;
            var close = trimmed.IndexOf(']');
            return close > 1 && trimmed.Slice(1, close - 1).Trim().Equals(LanguageSectionName.AsSpan(), StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        }
        if (state != 1) return state;

        var eq = trimmed.IndexOf('=');
        if (eq <= 0) return state;
        Span<char> key = stackalloc char[Math.Min(eq, LanguageSectionBase.MaxStackKeyLength)];
        if (eq > key.Length) return state;
        if (!key.Slice(0, LanguageSectionBase.NormalizeKey(trimmed.Slice(0, eq), key)).SequenceEqual(DescriptionKey.AsSpan())) return state;

        var raw = trimmed.Slice(eq + 1).Trim();
        var buffer = new char[raw.Length];
        var value = new string(buffer, 0, UnescapeValue(raw, buffer));
        if (!string.IsNullOrWhiteSpace(value)) description = value;
        return 2;
    }

    // ── Language resolution ───────────────────────────────────────────────────

    /// <summary>
    /// Resolves a requested language tag to one of the available languages (<see cref="GetLanguages"/>).
    /// </summary>
    /// <remarks>
    /// In this order:
    /// <list type="number">
    ///   <item>An exact match (case-insensitive).</item>
    ///   <item>The same tag without hyphens or with underscores: <c>ptBR</c> / <c>pt_BR</c> → <c>pt-BR</c>.</item>
    ///   <item>
    ///     A tag with the same language subtag (private-use tags like <c>de-x-franconia</c> excluded): one with the
    ///     same region (<c>zh-Hant-TW</c> → <c>zh-TW</c>); then, for the requested tag and each of its parents in turn
    ///     (<see cref="CultureInfo.Parent"/> and the tag without its last subtag, most specific first): the parent
    ///     itself (<c>de-AT</c> → <c>de</c>), its default specific culture (<c>de</c>, <c>de-AT</c> → <c>de-DE</c>),
    ///     one of its children (<c>zh-HK</c> → <c>zh-TW</c> via <c>zh-Hant</c>); finally the first available tag with
    ///     that language subtag.
    ///   </item>
    ///   <item>Otherwise the base language.</item>
    /// </list>
    /// The available tag is returned in its own spelling. Only file names are scanned; no state is changed.
    /// The parent and specific cultures come from the system's culture data, which can differ between
    /// .NET (ICU) and .NET Framework (NLS) for rare tags.
    /// </remarks>
    /// <param name="requested">The requested IETF tag, e.g. from the user settings or an installer.</param>
    /// <returns>The tag of an available language, or the base language.</returns>
    public string ResolveLanguage(string requested)
        => ResolveLanguage(requested, ScanLanguages().Select(language => language.Ietf).ToList(), _baseLanguage);

    internal static string ResolveLanguage(string requested, IReadOnlyList<string> available, string baseLanguage)
    {
        if (string.IsNullOrWhiteSpace(requested) || available.Count == 0)
            return baseLanguage;

        var tag = requested.Trim().Replace('_', '-');

        // 1. Exact match
        var match = available.FirstOrDefault(a => EqualsIgnoreCase(a, tag));
        if (match != null) return match;

        // 2. Same tag without hyphens (installer legacy values like ptBR or zhCN)
        var compact = tag.Replace("-", "");
        match = available.FirstOrDefault(a => EqualsIgnoreCase(a.Replace("-", ""), compact));
        if (match != null) return match;

        // A legacy value without hyphen and no exact match: split it so the language subtag is found ("ptPT" → "pt-PT")
        if (tag.IndexOf('-') < 0 && tag.Length is 4 or 5 && tag.All(char.IsLetter))
            tag = tag.Substring(0, tag.Length - 2) + "-" + tag.Substring(tag.Length - 2);

        // 3. Same language subtag; a private-use variant (a dialect) is only used when asked for exactly
        var language = LanguageSubtag(tag);
        var candidates = available
            .Where(a => EqualsIgnoreCase(LanguageSubtag(a), language) && !IsPrivateUse(a))
            .ToList();
        if (candidates.Count == 0) return baseLanguage;

        // 3a. The same region, unless the script differs (zh-Hans-HK must not become the Traditional zh-HK)
        var region = RegionSubtag(tag);
        if (region != null)
        {
            var script = ScriptOf(tag);
            match = candidates.FirstOrDefault(c => EqualsIgnoreCase(RegionSubtag(c), region)
                                                   && (script == null || ScriptOf(c) is not { } other || EqualsIgnoreCase(other, script)));
            if (match != null) return match;
        }

        // 3b. The tag and then each parent, most specific first: the parent itself, its specific culture, a child
        var level = 0;
        foreach (var t in new[] { tag }.Concat(GetAncestors(tag)))
        {
            if (level++ > 0)
            {
                match = candidates.FirstOrDefault(c => EqualsIgnoreCase(c, t));
                if (match != null) return match;
            }

            var specific = GetSpecificCultureName(t);
            if (specific != null)
            {
                match = candidates.FirstOrDefault(c => EqualsIgnoreCase(c, specific));
                if (match != null) return match;
            }

            match = candidates.FirstOrDefault(c => GetAncestors(c).Any(a => EqualsIgnoreCase(a, t)));
            if (match != null) return match;
        }

        // 3c. Any tag with the same language subtag
        return candidates[0];
    }

    /// <summary>
    /// The script of a tag: an explicit script subtag (<c>zh-Hant-TW</c>), otherwise the script of a parent culture
    /// (<c>zh-HK</c> → <c>Hant</c> via <c>zh-Hant</c>), or <c>null</c>.
    /// </summary>
    private static string? ScriptOf(string tag)
    {
        static string? Explicit(string t)
        {
            var parts = t.Split('-');
            return parts.Length > 1 && parts[1].Length == 4 && parts[1].All(char.IsLetter) ? parts[1] : null;
        }

        return Explicit(tag) ?? GetAncestors(tag).Select(Explicit).FirstOrDefault(s => s != null);
    }

    private static bool IsPrivateUse(string tag)
        => tag.Split('-').Skip(1).Any(part => part.Length == 1);   // x-..., or an extension

    private static bool EqualsIgnoreCase(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private static string LanguageSubtag(string tag)
    {
        var hyphen = tag.IndexOf('-');
        return hyphen < 0 ? tag : tag.Substring(0, hyphen);
    }

    /// <summary>The region subtag (two letters or three digits) before any private-use part, or <c>null</c>.</summary>
    private static string? RegionSubtag(string tag)
    {
        var parts = tag.Split('-');
        for (var i = 1; i < parts.Length; i++)
        {
            var part = parts[i];
            if (part.Length == 1) break;   // extension or private use (x-...)
            if ((part.Length == 2 && part.All(char.IsLetter)) || (part.Length == 3 && part.All(char.IsDigit)))
                return part;
        }
        return null;
    }

    /// <summary>
    /// The parents of a tag, most specific first: the <see cref="CultureInfo.Parent"/> chain and the tags
    /// obtained by removing subtags (<c>zh-Hant-TW</c> → <c>zh-Hant</c>, <c>zh</c>).
    /// </summary>
    private static IEnumerable<string> GetAncestors(string tag)
    {
        var result = new List<string>();
        void Add(string ancestor)
        {
            if (!string.IsNullOrEmpty(ancestor) && !EqualsIgnoreCase(ancestor, tag) && !result.Any(r => EqualsIgnoreCase(r, ancestor)))
                result.Add(ancestor);
        }

        if (TryGetCultureInfo(tag, out var culture))
        {
            for (var parent = culture!.Parent; !string.IsNullOrEmpty(parent.Name); parent = parent.Parent)
                Add(parent.Name);
        }
        foreach (var parent in GetParentLanguages(tag).Reverse())
            Add(parent);
        return result;
    }

    private static string? GetSpecificCultureName(string tag)
    {
        try
        {
            var specific = CultureInfo.CreateSpecificCulture(tag).Name;
            return string.IsNullOrEmpty(specific) ? null : specific;
        }
        catch (CultureNotFoundException)
        {
            return null;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    // ── Section directory validation ──────────────────────────────────────────

    private void ValidateSectionDirectories()
    {
        foreach (var entry in _entries)
            ValidateDirectories(entry);
    }

    private static void ValidateDirectories(SectionEntry entry)
    {
        if (entry.Directories.Count == 0)
            throw new InvalidOperationException(
                $"No search path configured for language section '{entry.Type.Name}'. " +
                "Specify a path via RegisterSection() or LanguageConfigBuilder.AddSearchPath().");
    }

    // ── Language loading ──────────────────────────────────────────────────────

    /// <summary>
    /// Builds the translations of all registered sections for <paramref name="language"/> (under the gate) and
    /// publishes them as the sections' latest translations. Returns the sections to apply them to.
    /// </summary>
    private SectionEntry[] BuildAll(string language, Batch batch)
    {
        var entries = _entries;
        BuildEntries(entries, language, batch);
        return entries;
    }

    private async Task<SectionEntry[]> BuildAllAsync(string language, Batch batch, CancellationToken cancellationToken)
    {
        var entries = _entries;
        await BuildEntriesAsync(entries, language, batch, cancellationToken).ConfigureAwait(false);
        return entries;
    }

    /// <summary>
    /// The plan of a build: the targets, grouped by the files they read (same module and directories), and per
    /// group and language of the chain the files to apply (lowest priority first).
    /// </summary>
    private sealed class BuildPlan
    {
        public BuildPlan(LanguageConfig config, IReadOnlyList<SectionEntry> entries, string language)
        {
            Entries = entries;
            Chain = config.GetLoadChain(language);
            Targets = new TranslationLoader.Target[entries.Count];
            GroupOf = new int[entries.Count];
            for (var i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                Targets[i] = new TranslationLoader.Target(i, entry.Section.SectionName, entry.Latest ?? entry.Section.CurrentTranslations);
                var group = Groups.FindIndex(g => g.Module == entry.Section.ModuleName && ReferenceEquals(g.Directories, entry.Directories));
                if (group < 0)
                {
                    group = Groups.Count;
                    Groups.Add((entry.Section.ModuleName, entry.Directories, new List<TranslationLoader.Target>()));
                }
                Groups[group].Targets.Add(Targets[i]);
                GroupOf[i] = group;
            }
            Files = new IReadOnlyList<string>[Groups.Count, Chain.Count];
            for (var g = 0; g < Groups.Count; g++)
                for (var c = 0; c < Chain.Count; c++)
                    Files[g, c] = config.ResolveLanguageFiles(Groups[g].Directories, Groups[g].Module, Chain[c]);
        }

        public IReadOnlyList<SectionEntry> Entries { get; }
        public IReadOnlyList<string> Chain { get; }
        public TranslationLoader.Target[] Targets { get; }
        public int[] GroupOf { get; }
        public List<(string? Module, IReadOnlyList<string> Directories, List<TranslationLoader.Target> Targets)> Groups { get; } = new();
        public IReadOnlyList<string>[,] Files { get; }
    }

    private void BuildEntries(IReadOnlyList<SectionEntry> entries, string language, Batch batch)
    {
        var plan = new BuildPlan(this, entries, language);
        var loader = _loader;   // builds run under the gate: its scratch collections are reused
        // Most specific language and highest priority file first; see TranslationLoader.
        for (var c = plan.Chain.Count - 1; c >= 0; c--)
            for (var g = 0; g < plan.Groups.Count; g++)
            {
                var files = plan.Files[g, c];
                for (var f = files.Count - 1; f >= 0; f--)
                    loader.ReadFile(files[f], plan.Groups[g].Targets);
            }
        loader.Reset();
        Publish(plan, batch);
    }

    private async Task BuildEntriesAsync(IReadOnlyList<SectionEntry> entries, string language, Batch batch, CancellationToken cancellationToken)
    {
        var plan = new BuildPlan(this, entries, language);
        var loader = _loader;   // builds run under the gate: its scratch collections are reused
        for (var c = plan.Chain.Count - 1; c >= 0; c--)
            for (var g = 0; g < plan.Groups.Count; g++)
            {
                var files = plan.Files[g, c];
                for (var f = files.Count - 1; f >= 0; f--)
                    await loader.ReadFileAsync(files[f], plan.Groups[g].Targets, cancellationToken).ConfigureAwait(false);
            }
        loader.Reset();
        cancellationToken.ThrowIfCancellationRequested();
        Publish(plan, batch);
    }

    /// <summary>
    /// Publishes the built translations as the sections' latest (under the gate) and reports the files per
    /// section in the order they apply: per language of the chain, lowest priority first.
    /// </summary>
    private void Publish(BuildPlan plan, Batch batch)
    {
        for (var i = 0; i < plan.Entries.Count; i++)
        {
            var entry = plan.Entries[i];
            entry.Latest = plan.Targets[i].Result;
            for (var c = 0; c < plan.Chain.Count; c++)
            {
                var files = plan.Files[plan.GroupOf[i], c];
                if (files.Count == 0)
                {
                    var fileName = GetFileName(entry.Section.ModuleName, plan.Chain[c]);
                    batch.Notify(l => l.OnFileNotFound(fileName));
                    continue;
                }
                foreach (var filePath in files)
                    batch.Notify(l => l.OnFileLoaded(filePath));
            }
        }
    }

    /// <summary>
    /// The languages loaded for <paramref name="language"/>, least specific first, each once: the fallback
    /// language (the base language unless <see cref="LanguageConfigBuilder.UseFallbackLanguage"/> set another
    /// one) with its parent cultures as the floor for missing keys, then the parent cultures of the requested
    /// language, then the requested language itself. Later files override earlier ones.
    /// </summary>
    /// <example><c>fallback en-US, requested zh-Hant-TW</c> → en, en-US, zh, zh-Hant, zh-Hant-TW.</example>
    internal IReadOnlyList<string> GetLoadChain(string language)
    {
        var fallback = _fallbackLanguage ?? _baseLanguage;
        var chain = new List<string>();
        void Add(string ietf)
        {
            if (!chain.Any(c => string.Equals(c, ietf, StringComparison.OrdinalIgnoreCase)))
                chain.Add(ietf);
        }
        foreach (var parent in GetParentLanguages(fallback)) Add(parent);
        Add(fallback);
        foreach (var parent in GetParentLanguages(language)) Add(parent);
        Add(language);
        return chain;
    }

    /// <summary>
    /// Returns the parent tags of an IETF language tag, least specific first:
    /// <c>zh-Hant-TW</c> gives <c>zh</c> and <c>zh-Hant</c>.
    /// </summary>
    internal static IEnumerable<string> GetParentLanguages(string language)
    {
        for (var hyphen = language.IndexOf('-'); hyphen > 0; hyphen = language.IndexOf('-', hyphen + 1))
            yield return language.Substring(0, hyphen);
    }

    private string GetFileName(string? moduleName, string ietf)
        => moduleName != null
            ? $"{_basename}.{moduleName}.{ietf}.ini"
            : $"{_basename}.{ietf}.ini";

    /// <summary>
    /// Returns the language files to apply for the given IETF tag, in the order to apply them.
    /// Without <see cref="LanguageConfigBuilder.MergeSearchPaths"/> this is the file of the first directory
    /// that has it; with it, the file of every directory that has it, lowest priority first.
    /// </summary>
    /// <param name="directories">Search directories, in priority order.</param>
    /// <param name="moduleName">
    /// Optional module name: when set the file is <c>{basename}.{moduleName}.{ietf}.ini</c>;
    /// when <c>null</c> the file is <c>{basename}.{ietf}.ini</c>.
    /// </param>
    /// <param name="ietf">IETF language tag.</param>
    private IReadOnlyList<string> ResolveLanguageFiles(IReadOnlyList<string> directories, string? moduleName, string ietf)
    {
        var fileName = GetFileName(moduleName, ietf);

        List<string>? files = null;
        foreach (var directory in directories)
        {
            var path = Path.Combine(directory, fileName);
            if (!File.Exists(path)) continue;
            if (!_mergeSearchPaths) return [path];

            files ??= new List<string>();
            if (!files.Any(f => SamePath(f, path)))
                files.Add(path);
        }

        if (files == null) return [];
        files.Reverse();
        return files;
    }

    /// <summary>Processes escape sequences: <c>\n</c>, <c>\t</c>, <c>\\</c>.</summary>
    internal static string UnescapeValue(string raw)
    {
        if (raw.IndexOf('\\') < 0) return raw;
        var buffer = new char[raw.Length];
        return new string(buffer, 0, UnescapeValue(raw.AsSpan(), buffer));
    }

    /// <summary>
    /// Processes escape sequences (<c>\n</c>, <c>\t</c>, <c>\\</c>; other backslashes are kept) from
    /// <paramref name="raw"/> into <paramref name="destination"/> (at least as long as <paramref name="raw"/>)
    /// without allocating. Returns the length written.
    /// </summary>
    internal static int UnescapeValue(ReadOnlySpan<char> raw, Span<char> destination)
    {
        var backslash = raw.IndexOf('\\');
        if (backslash < 0)
        {
            raw.CopyTo(destination);
            return raw.Length;
        }

        raw.Slice(0, backslash).CopyTo(destination);
        var length = backslash;
        for (var i = backslash; i < raw.Length; i++)
        {
            var c = raw[i];
            if (c == '\\' && i + 1 < raw.Length)
            {
                switch (raw[i + 1])
                {
                    case 'n': destination[length++] = '\n'; i++; continue;
                    case 't': destination[length++] = '\t'; i++; continue;
                    case '\\': destination[length++] = '\\'; i++; continue;
                }
            }
            destination[length++] = c;
        }
        return length;
    }

    // ── File name helpers ─────────────────────────────────────────────────────

    // A two or three letter language subtag, followed by subtags of letters/digits (covers de, de-DE, zh-Hant-TW,
    // es-419 and de-x-franconia, whose last subtag is longer than BCP 47's eight characters). The language subtag is
    // deliberately stricter than BCP 47 so module names are not taken for tags.
    private static readonly Regex IetfTagPattern = new("^[A-Za-z]{2,3}(-[A-Za-z0-9]{1,16})*$", RegexOptions.CultureInvariant);

    /// <summary>
    /// Splits a language file name into module and IETF tag: <c>{basename}.{ietf}.ini</c> (module <c>null</c>)
    /// or <c>{basename}.{module}.{ietf}.ini</c>. Returns <c>false</c> for other files, for a tag that does not
    /// look like an IETF tag and for a tag that equals a registered module name (<c>{basename}.{module}.ini</c>).
    /// </summary>
    internal bool TryParseLanguageFileName(string filePath, ISet<string> moduleNames, out string? module, out string ietf)
    {
        module = null;
        ietf = "";

        var fileName = Path.GetFileNameWithoutExtension(filePath);
        if (string.IsNullOrEmpty(fileName) || !fileName.StartsWith(_basename + ".", StringComparison.OrdinalIgnoreCase))
            return false;

        var remainder = fileName.Substring(_basename.Length + 1);
        if (string.IsNullOrEmpty(remainder)) return false;

        var dotIdx = remainder.LastIndexOf('.');
        if (dotIdx == 0 || dotIdx == remainder.Length - 1) return false;
        if (dotIdx > 0)
            module = remainder.Substring(0, dotIdx);
        var tag = dotIdx >= 0 ? remainder.Substring(dotIdx + 1) : remainder;

        if (!IetfTagPattern.IsMatch(tag) || moduleNames.Contains(tag))
            return false;

        ietf = tag;
        return true;
    }

    private static bool TryGetCultureInfo(string ietf, out CultureInfo? culture)
    {
        try
        {
            culture = CultureInfo.GetCultureInfo(ietf);
            if (string.Equals(culture.Name, ietf, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            culture = null;
            return false;
        }
        catch
        {
            culture = null;
            return false;
        }
    }

    // ── File monitoring ───────────────────────────────────────────────────────

    // Called under the gate; adds watchers for the existing directories that are not watched yet (late sections).
    // A directory that does not exist yet (e.g. a user folder created later) is not watched.
    private void StartMonitoring()
    {
        if (_disposed) return;

        _debounceTimer ??= new System.Threading.Timer(_ =>
        {
            if (_disposed) return;
            // This runs on a thread-pool thread: an unhandled exception here would terminate the
            // process (e.g. an IOException while an editor is still writing the file).
            try
            {
                ReloadCurrentLanguage();
            }
            catch (Exception ex)
            {
                // A failure of the reload itself is reported (not thrown) by Execute; this is e.g. an exception of a
                // PropertyChanged / LanguageChanged handler or a listener.
                try { NotifyListeners(l => l.OnError("Reload", ex)); }
                catch { /* a failing listener must not crash the process either */ }
            }
        }, null, Timeout.Infinite, Timeout.Infinite);

        foreach (var dir in _entries.SelectMany(e => e.Directories).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(dir) || _watchers.ContainsKey(dir)) continue;

            var watcher = new FileSystemWatcher(dir, "*.ini")
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName
            };
            watcher.Changed += OnFileChanged;
            // A translation file dropped into (or removed from) a merged search path changes the result too.
            watcher.Created += OnFileChanged;
            watcher.Deleted += OnFileChanged;
            watcher.Renamed += OnFileChanged;
            watcher.EnableRaisingEvents = true;
            _watchers[dir] = watcher;
        }
    }

    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        try
        {
            _debounceTimer?.Change(DebounceMs, Timeout.Infinite);
        }
        catch (ObjectDisposedException)
        {
            // disposed concurrently
        }
    }

    private void ReloadCurrentLanguage()
    {
        Execute("Reload", batch =>
        {
            if (_disposed) return;
            var entries = BuildAll(_currentLanguage, batch);
            ThenApply(batch, entries);
            batch.Then(RaiseLanguageChanged);
        }, rethrow: false);
    }

    // ── IDisposable ───────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // Wait for a running operation, so it cannot add a watcher or timer while they are disposed.
        // No user code runs under the gate, so this cannot deadlock with a handler that calls Dispose.
        _gate.Wait();
        try
        {
            var timer = _debounceTimer;
            _debounceTimer = null;
            timer?.Change(Timeout.Infinite, Timeout.Infinite);
            timer?.Dispose();

            foreach (var watcher in _watchers.Values)
                watcher.Dispose();
            _watchers.Clear();
        }
        finally
        {
            _gate.Release();
        }
    }

    // ── Types ─────────────────────────────────────────────────────────────────

    private sealed class SectionEntry
    {
        public SectionEntry(Type type, LanguageSectionBase section, IReadOnlyList<string> directories)
        {
            Type = type;
            Section = section;
            Directories = directories;
        }

        public Type Type { get; }
        public LanguageSectionBase Section { get; }
        public IReadOnlyList<string> Directories { get; }

        /// <summary>The translations built by the latest operation; set under the gate, applied outside it.</summary>
        public volatile Dictionary<string, string>? Latest;

        /// <summary>The translations last applied to the section (only written by the active applier).</summary>
        public volatile Dictionary<string, string>? Applied;

        /// <summary>1 while a thread applies translations to the section.</summary>
        public int Applying;
    }
}

/// <summary>Options passed from <see cref="LanguageConfigBuilder"/> to <see cref="LanguageConfig"/>.</summary>
internal readonly struct LanguageConfigOptions
{
    public LanguageConfigOptions(bool allowLateSectionRegistration, bool mergeSearchPaths, bool resolveLanguages)
    {
        AllowLateSectionRegistration = allowLateSectionRegistration;
        MergeSearchPaths = mergeSearchPaths;
        ResolveLanguages = resolveLanguages;
    }

    public bool AllowLateSectionRegistration { get; }
    public bool MergeSearchPaths { get; }
    public bool ResolveLanguages { get; }
}
