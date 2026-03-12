// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using Dapplo.Ini.Configuration;
using Dapplo.Ini.Interfaces;
using Dapplo.Ini.Parsing;

namespace Dapplo.Ini;

/// <summary>
/// Holds configuration for one registered INI file: its search locations, defaults/constants files,
/// and the <see cref="IIniSection"/> instances that were loaded from it.
/// Implements <see cref="IDisposable"/> to release any held file lock and the file-change watcher.
/// </summary>
public sealed class IniConfig : IDisposable
{
    internal readonly List<string> SearchPaths = new();
    internal readonly List<string> DefaultFilePaths = new();
    internal readonly List<string> ConstantFilePaths = new();
    internal readonly List<IValueSource> ValueSources = new();
    internal readonly List<IValueSourceAsync> ValueSourcesAsync = new();
    internal readonly Dictionary<Type, IIniSection> Sections = new();

    // ── Encoding ──────────────────────────────────────────────────────────────

    /// <summary>Encoding used when reading and writing the INI file. Defaults to UTF-8.</summary>
    internal Encoding Encoding = Encoding.UTF8;

    // ── Migration / unknown-key callback ─────────────────────────────────────

    /// <summary>
    /// Optional callback invoked for every key in the INI file that has no matching property
    /// on the registered section interface.  Set via <see cref="IniConfigBuilder.OnUnknownKey"/>.
    /// </summary>
    internal UnknownKeyCallback? UnknownKeyHandler;

    // ── Metadata section ──────────────────────────────────────────────────────

    /// <summary>
    /// When non-null the framework writes a <c>[__metadata__]</c> section (prepended to the
    /// file so it is always first) on every Save.
    /// Contains the configured <c>Version</c>, <c>CreatedBy</c>, and a locale-formatted
    /// <c>SavedOn</c> timestamp.
    /// Enabled via <see cref="IniConfigBuilder.EnableMetadata"/>.
    /// </summary>
    internal IniMetadataConfig? MetadataConfig;

    /// <summary>
    /// The metadata that was read from the <c>[__metadata__]</c> section of the INI file
    /// on the last load / reload.
    /// <c>null</c> when the section did not exist in the file (e.g. first-run or no metadata enabled).
    /// </summary>
    public IniMetadata? Metadata { get; internal set; }

    // ── File lock ─────────────────────────────────────────────────────────────

    // Held when the caller requested file locking via IniConfigBuilder.LockFile().
    private FileStream? _lockStream;
    private readonly object _lockStreamSyncRoot = new();

    // ── File monitoring ───────────────────────────────────────────────────────

    private FileSystemWatcher? _watcher;
    private FileChangedCallback? _fileChangedCallback;

    // Tracks whether a postponed reload is pending.
    private volatile bool _postponedReloadPending;

    // Debounce timer: coalesces rapid Changed events (e.g. truncate + write) into one reload.
    private System.Threading.Timer? _reloadDebounceTimer;
    private const int ReloadDebounceMs = 200;

    // ── Save re-entrance guard ────────────────────────────────────────────────

    // 0 = idle, 1 = save in progress.  Manipulated via Interlocked to allow concurrent callers
    // (e.g. the auto-save timer and a manual Save() call) to detect overlap and bail out.
    private int _isSaving;

    // ── Auto-save pause ───────────────────────────────────────────────────────

    // Nestable pause counter. When > 0 the auto-save timer skips its save.
    // PauseAutoSave increments; ResumeAutoSave decrements.
    private int _autoSavePauseCount;

    // ── Reload sync/async ─────────────────────────────────────────────────────

    private readonly SemaphoreSlim _reloadSemaphore = new(1, 1);

    // ── Save-on-exit ──────────────────────────────────────────────────────────

    private EventHandler? _processExitHandler;

    // ── Auto-save timer ───────────────────────────────────────────────────────

    private System.Threading.Timer? _autoSaveTimer;

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>The logical name of the INI file (e.g. "myapp.ini").</summary>
    public string FileName { get; }

    /// <summary>The resolved absolute path from which the file was loaded, or <c>null</c> if not yet loaded.</summary>
    public string? LoadedFromPath { get; internal set; }

    /// <summary>
    /// Raised after <see cref="Reload"/> successfully re-loads all sections from disk.
    /// </summary>
    public event EventHandler? Reloaded;

    // ── Initial load task ─────────────────────────────────────────────────────

    private Task _initialLoadTask = Task.CompletedTask;

    /// <summary>
    /// A <see cref="Task"/> that completes when the initial load of the INI file has finished.
    /// <para>
    /// After a synchronous <see cref="IniConfigBuilder.Build"/> call this is always
    /// <see cref="Task.CompletedTask"/> because loading is already done by the time <c>Build</c> returns.
    /// After an asynchronous <see cref="IniConfigBuilder.BuildAsync"/> call this task completes
    /// (successfully or with an exception) when the async loading sequence finishes.
    /// </para>
    /// <para>
    /// Use this property in dependency-injection scenarios where the <see cref="IniConfig"/> or its
    /// sections are injected before loading is complete:
    /// <code>
    /// await iniConfig.InitialLoadTask; // await here before accessing section values
    /// </code>
    /// </para>
    /// </summary>
    public Task InitialLoadTask => _initialLoadTask;

    /// <summary>Replaces the initial-load task. Called by <see cref="IniConfigBuilder.BuildAsync"/>.</summary>
    internal void SetInitialLoadTask(Task task) => _initialLoadTask = task;

    internal IniConfig(string fileName)
    {
        FileName = fileName;
    }

    /// <summary>
    /// Returns the registered section of type <typeparamref name="T"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when the type has not been registered.</exception>
    public T GetSection<T>() where T : IIniSection
    {
        if (Sections.TryGetValue(typeof(T), out var section))
            return (T)section;

        throw new InvalidOperationException(
            $"Section '{typeof(T).Name}' has not been registered with the INI configuration '{FileName}'.");
    }

    // ── Change tracking ───────────────────────────────────────────────────────

    /// <summary>
    /// Returns <c>true</c> when at least one registered section has unsaved changes
    /// (i.e. its <see cref="IIniSection.HasChanges"/> is <c>true</c>).
    /// </summary>
    public bool HasPendingChanges()
    {
        foreach (var section in Sections.Values)
        {
            if (section.HasChanges)
                return true;
        }
        return false;
    }

    /// <summary>Clears the dirty flag on every registered section.</summary>
    internal void ClearAllDirtyFlags()
    {
        foreach (var section in Sections.Values)
        {
            if (section is IniSectionBase sectionBase)
                sectionBase.ClearDirtyFlag();
        }
    }

    // ── Save ──────────────────────────────────────────────────────────────────

    /// <summary>Saves all sections back to <see cref="LoadedFromPath"/>.</summary>
    /// <remarks>
    /// Concurrent or re-entrant calls (e.g. from the auto-save timer while a manual save is
    /// already running) return immediately without doing anything.
    /// </remarks>
    /// <exception cref="InvalidOperationException">Thrown when the file path is not known.</exception>
    public void Save()
    {
        if (string.IsNullOrEmpty(LoadedFromPath))
            throw new InvalidOperationException("Cannot save: the INI file path is not known.");

        // Re-entrance / concurrent-save guard: only one Save() runs at a time.
        if (Interlocked.CompareExchange(ref _isSaving, 1, 0) != 0)
            return;

        try
        {
            // Call IBeforeSave hooks; abort if any returns false.
            // Note: `return` inside a try block always triggers the finally below,
            // so _isSaving is always reset to 0 regardless of how Save() exits.
            foreach (var section in Sections.Values)
            {
                if (section is IBeforeSave beforeSave && !beforeSave.OnBeforeSave())
                    return;
            }

            // Build an IniFile from current section values
            var iniFile = BuildIniFile();

            // Pause the watcher around the write so the OS change notification generated by
            // our own write is never dispatched.  Disabling drops the kernel buffer; re-enabling
            // starts fresh, so no spurious OnFileChanged event fires after Save() returns.
            if (_watcher != null) _watcher.EnableRaisingEvents = false;
            try
            {
                IniFileWriter.WriteFile(LoadedFromPath!, iniFile, Encoding);
            }
            finally
            {
                if (_watcher != null) _watcher.EnableRaisingEvents = true;
            }

            // Clear dirty flags after successful write
            ClearAllDirtyFlags();

            // Call IAfterSave hooks
            foreach (var section in Sections.Values)
            {
                if (section is IAfterSave afterSave)
                    afterSave.OnAfterSave();
            }
        }
        finally
        {
            Interlocked.Exchange(ref _isSaving, 0);
        }
    }

    /// <summary>
    /// Asynchronously saves all sections back to <see cref="LoadedFromPath"/>.
    /// </summary>
    /// <remarks>
    /// Concurrent or re-entrant calls return immediately without doing anything.
    /// Async lifecycle hooks (<see cref="IBeforeSaveAsync"/>, <see cref="IAfterSaveAsync"/>) are
    /// preferred; when a section implements only the synchronous hooks (<see cref="IBeforeSave"/>,
    /// <see cref="IAfterSave"/>), those are called instead.
    /// </remarks>
    /// <param name="cancellationToken">Token to cancel the async operation.</param>
    /// <exception cref="InvalidOperationException">Thrown when the file path is not known.</exception>
    public async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(LoadedFromPath))
            throw new InvalidOperationException("Cannot save: the INI file path is not known.");

        if (Interlocked.CompareExchange(ref _isSaving, 1, 0) != 0)
            return;

        try
        {
            // Call IBeforeSaveAsync hooks (preferred) or fall back to sync IBeforeSave.
            foreach (var section in Sections.Values)
            {
                if (section is IBeforeSaveAsync beforeSaveAsync)
                {
                    if (!await beforeSaveAsync.OnBeforeSaveAsync(cancellationToken).ConfigureAwait(false))
                        return;
                }
                else if (section is IBeforeSave beforeSave && !beforeSave.OnBeforeSave())
                {
                    return;
                }
            }

            var iniFile = BuildIniFile();

            if (_watcher != null) _watcher.EnableRaisingEvents = false;
            try
            {
                await IniFileWriter.WriteFileAsync(LoadedFromPath!, iniFile, Encoding, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                if (_watcher != null) _watcher.EnableRaisingEvents = true;
            }

            ClearAllDirtyFlags();

            // Call IAfterSaveAsync hooks (preferred) or fall back to sync IAfterSave.
            foreach (var section in Sections.Values)
            {
                if (section is IAfterSaveAsync afterSaveAsync)
                    await afterSaveAsync.OnAfterSaveAsync(cancellationToken).ConfigureAwait(false);
                else if (section is IAfterSave afterSave)
                    afterSave.OnAfterSave();
            }
        }
        finally
        {
            Interlocked.Exchange(ref _isSaving, 0);
        }
    }

    // ── Reload ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Reloads all sections in-place from the INI file and any registered default/constant files
    /// and external value sources.  Existing object references remain valid (singleton guarantee).
    /// </summary>
    /// <remarks>
    /// The reload sequence is:
    /// <list type="number">
    ///   <item>Reset every section to its compiled defaults.</item>
    ///   <item>Apply registered default files (layered).</item>
    ///   <item>Apply the user INI file.</item>
    ///   <item>Apply registered constant files (admin overrides).</item>
    ///   <item>Apply registered external <see cref="IValueSource"/> instances.</item>
    ///   <item>Fire <see cref="IAfterLoad"/> hooks on every section.</item>
    ///   <item>Clear dirty flags (freshly loaded data is not considered unsaved).</item>
    ///   <item>Raise <see cref="Reloaded"/>.</item>
    /// </list>
    /// </remarks>
    public void Reload()
    {
        _reloadSemaphore.Wait();
        try
        {
            _postponedReloadPending = false;

            // 1. Reset to defaults
            foreach (var section in Sections.Values)
                section.ResetToDefaults();

            // 2. Apply default files
            foreach (var path in DefaultFilePaths)
            {
                if (File.Exists(path))
                    ApplyIniFile(IniFileParser.ParseFile(path, Encoding));
            }

            // 3. Apply user file
            if (!string.IsNullOrEmpty(LoadedFromPath) && File.Exists(LoadedFromPath))
                ApplyIniFile(IniFileParser.ParseFile(LoadedFromPath!, Encoding));

            // 4. Apply constant files
            foreach (var path in ConstantFilePaths)
            {
                if (File.Exists(path))
                    ApplyIniFile(IniFileParser.ParseFile(path, Encoding));
            }

            // 5. Apply external value sources
            ApplyValueSources();

            // 6. Fire IAfterLoad hooks
            foreach (var section in Sections.Values)
            {
                if (section is IAfterLoad afterLoad)
                    afterLoad.OnAfterLoad();
            }

            // 7. Clear dirty flags — freshly loaded data is not considered unsaved
            ClearAllDirtyFlags();
        }
        finally
        {
            _reloadSemaphore.Release();
        }

        // 8. Raise Reloaded
        Reloaded?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Asynchronously reloads all sections in-place from the INI file and any registered
    /// default/constant files and external value sources.
    /// Existing object references remain valid (singleton guarantee).
    /// </summary>
    /// <remarks>
    /// Async lifecycle hooks (<see cref="IAfterLoadAsync"/>) are preferred; when a section
    /// implements only the synchronous <see cref="IAfterLoad"/> hook, that is called instead.
    /// </remarks>
    /// <param name="cancellationToken">Token to cancel the async operation.</param>
    public async Task ReloadAsync(CancellationToken cancellationToken = default)
    {
        await _reloadSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _postponedReloadPending = false;

            // 1. Reset to defaults
            foreach (var section in Sections.Values)
                section.ResetToDefaults();

            // 2. Apply default files
            foreach (var path in DefaultFilePaths)
            {
                if (File.Exists(path))
                    ApplyIniFile(await IniFileParser.ParseFileAsync(path, Encoding, cancellationToken).ConfigureAwait(false));
            }

            // 3. Apply user file
            if (!string.IsNullOrEmpty(LoadedFromPath) && File.Exists(LoadedFromPath))
                ApplyIniFile(await IniFileParser.ParseFileAsync(LoadedFromPath!, Encoding, cancellationToken).ConfigureAwait(false));

            // 4. Apply constant files
            foreach (var path in ConstantFilePaths)
            {
                if (File.Exists(path))
                    ApplyIniFile(await IniFileParser.ParseFileAsync(path, Encoding, cancellationToken).ConfigureAwait(false));
            }

            // 5. Apply external value sources (sync and async)
            await ApplyValueSourcesAsync(cancellationToken).ConfigureAwait(false);

            // 6. Fire IAfterLoadAsync hooks (preferred) or fall back to sync IAfterLoad.
            foreach (var section in Sections.Values)
            {
                if (section is IAfterLoadAsync afterLoadAsync)
                    await afterLoadAsync.OnAfterLoadAsync(cancellationToken).ConfigureAwait(false);
                else if (section is IAfterLoad afterLoad)
                    afterLoad.OnAfterLoad();
            }

            // 7. Clear dirty flags — freshly loaded data is not considered unsaved
            ClearAllDirtyFlags();
        }
        finally
        {
            _reloadSemaphore.Release();
        }

        // 8. Raise Reloaded
        Reloaded?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// If a file-change notification was previously <see cref="ReloadDecision.Postpone">postponed</see>,
    /// triggers the reload now.  Has no effect when no postponed reload is pending.
    /// </summary>
    public void RequestPostponedReload()
    {
        if (_postponedReloadPending)
            Reload();
    }

    // ── File locking ──────────────────────────────────────────────────────────

    /// <summary>
    /// Acquires an exclusive read lock on <see cref="LoadedFromPath"/>, preventing other processes
    /// from writing to the file while this application is running.
    /// Other processes may still open the file for reading.
    /// Calling this method when a lock is already held is a no-op.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when <see cref="LoadedFromPath"/> is unknown.</exception>
    internal void AcquireFileLock()
    {
        if (string.IsNullOrEmpty(LoadedFromPath)) return;

        lock (_lockStreamSyncRoot)
        {
            if (_lockStream != null) return; // already locked

            _lockStream = new FileStream(
                LoadedFromPath!,
                FileMode.OpenOrCreate,
                FileAccess.Read,
                FileShare.Read);
        }
    }

    /// <summary>
    /// Releases the file lock acquired by <see cref="AcquireFileLock"/> (if any).
    /// </summary>
    internal void ReleaseFileLock()
    {
        lock (_lockStreamSyncRoot)
        {
            _lockStream?.Dispose();
            _lockStream = null;
        }
    }

    // ── File monitoring ───────────────────────────────────────────────────────

    /// <summary>
    /// Starts monitoring <see cref="LoadedFromPath"/> for external changes.
    /// When a change is detected the optional <paramref name="callback"/> is invoked to decide
    /// whether to <see cref="ReloadDecision.Reload"/>, <see cref="ReloadDecision.Ignore"/>, or
    /// <see cref="ReloadDecision.Postpone"/> the reload.
    /// </summary>
    /// <param name="callback">
    /// Optional callback.  When <c>null</c> every change triggers an immediate reload.
    /// </param>
    internal void StartMonitoring(FileChangedCallback? callback)
    {
        if (string.IsNullOrEmpty(LoadedFromPath)) return;

        _fileChangedCallback = callback;

        // Debounce timer starts in the stopped state; OnFileChanged arms it on demand.
        _reloadDebounceTimer = new System.Threading.Timer(_ =>
        {
            if (!_disposed)
                Reload();
        }, null, Timeout.Infinite, Timeout.Infinite);

        var dir  = Path.GetDirectoryName(LoadedFromPath)!;
        var file = Path.GetFileName(LoadedFromPath)!;

        _watcher = new FileSystemWatcher(dir, file)
        {
            NotifyFilter         = NotifyFilters.LastWrite | NotifyFilters.Size,
            EnableRaisingEvents  = true
        };
        _watcher.Changed += OnFileChanged;
    }

    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        // The watcher is paused during Save(), so this handler is only invoked for genuine
        // external file changes — no self-write suppression needed here.
        var decision = _fileChangedCallback?.Invoke(e.FullPath) ?? ReloadDecision.Reload;

        switch (decision)
        {
            case ReloadDecision.Reload:
                // Debounce: schedule the reload after a short delay so that rapid successive
                // Changed events (e.g. file truncated then written by File.WriteAllText) are
                // coalesced into a single reload once the write is complete.
                // Use a local copy to avoid a null-reference race with Dispose().
                _reloadDebounceTimer?.Change(ReloadDebounceMs, Timeout.Infinite);
                break;

            case ReloadDecision.Postpone:
                _postponedReloadPending = true;
                break;

            case ReloadDecision.Ignore:
            default:
                break;
        }
    }

    // ── Save-on-exit ──────────────────────────────────────────────────────────

    /// <summary>
    /// Registers a <see cref="AppDomain.CurrentDomain"/> <c>ProcessExit</c> handler that calls
    /// <see cref="Save"/> when the process exits.  The handler is unregistered on <see cref="Dispose"/>.
    /// </summary>
    internal void EnableSaveOnExit()
    {
        _processExitHandler = (_, _) =>
        {
            if (!_disposed)
                Save();
        };
        AppDomain.CurrentDomain.ProcessExit += _processExitHandler;
    }

    // ── Auto-save pause ───────────────────────────────────────────────────────

    /// <summary>
    /// Suspends the auto-save timer so that automatic saves are skipped until
    /// <see cref="ResumeAutoSave"/> is called.  Calls may be nested: each
    /// <see cref="PauseAutoSave"/> must be matched by a corresponding
    /// <see cref="ResumeAutoSave"/>.
    /// <para>
    /// Typical use: a UI dialog pauses auto-save while the user edits settings,
    /// then resumes it when the dialog closes (or discards changes).
    /// </para>
    /// </summary>
    public void PauseAutoSave() => Interlocked.Increment(ref _autoSavePauseCount);

    /// <summary>
    /// Resumes auto-save after a prior <see cref="PauseAutoSave"/> call.
    /// If auto-save was not paused (or has already been fully resumed), this is a no-op.
    /// </summary>
    public void ResumeAutoSave()
    {
        // Decrement but clamp at 0 to guard against unbalanced calls
        var updated = Interlocked.Decrement(ref _autoSavePauseCount);
        if (updated < 0)
            Interlocked.Increment(ref _autoSavePauseCount);
    }

    // ── Auto-save timer ───────────────────────────────────────────────────────

    /// <summary>
    /// Starts a timer that periodically calls <see cref="Save"/> when <see cref="HasPendingChanges"/>
    /// returns <c>true</c> and auto-save is not paused.  The timer is stopped on <see cref="Dispose"/>.
    /// </summary>
    internal void StartAutoSave(TimeSpan interval)
    {
        _autoSaveTimer = new System.Threading.Timer(_ =>
        {
            if (!_disposed && Volatile.Read(ref _autoSavePauseCount) == 0 && HasPendingChanges())
                Save();
        }, null, interval, interval);
    }

    // ── Value sources ─────────────────────────────────────────────────────────

    internal void ApplyValueSources()
    {
        foreach (var source in ValueSources)
        {
            foreach (var section in Sections.Values)
            {
                foreach (var entry in GetSectionKeys(section))
                {
                    if (source.TryGetValue(section.SectionName, entry, out var value))
                        section.SetRawValue(entry, value);
                }
            }
        }
    }

    internal async Task ApplyValueSourcesAsync(CancellationToken cancellationToken = default)
    {
        // Apply synchronous sources first
        ApplyValueSources();

        // Then apply async sources
        foreach (var source in ValueSourcesAsync)
        {
            foreach (var section in Sections.Values)
            {
                foreach (var entry in GetSectionKeys(section))
                {
                    var (found, value) = await source.TryGetValueAsync(
                        section.SectionName, entry, cancellationToken).ConfigureAwait(false);
                    if (found)
                        section.SetRawValue(entry, value);
                }
            }
        }
    }

    /// <summary>Returns all known keys for a section (from GetAllRawValues if available).</summary>
    private static IEnumerable<string> GetSectionKeys(IIniSection section)
    {
        if (section is IniSectionBase sectionBase)
            return sectionBase.GetAllRawValues().Select(kvp => kvp.Key);
        return Array.Empty<string>();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    // The name of the special metadata section prepended to the INI file when opted in.
    internal const string MetadataSectionName = "__metadata__";

    internal IniFile BuildIniFile()
    {
        var iniFile = new Parsing.IniFile();
        foreach (var kvp in Sections)
        {
            var section = kvp.Value;
            var iniSection = iniFile.GetOrAddSection(section.SectionName);
            if (section is IniSectionBase sectionBase)
            {
                foreach (var rawKvp in sectionBase.GetAllRawValues())
                    iniSection.SetValue(rawKvp.Key, rawKvp.Value);
            }
        }

        // When metadata is enabled, build the [__metadata__] section and prepend it
        // so it is always the first section in the written file.
        if (MetadataConfig != null)
        {
            var metaSection = new Parsing.IniSection(MetadataSectionName, Array.Empty<string>());
            metaSection.SetValue("Version", MetadataConfig.Version);
            metaSection.SetValue("CreatedBy", MetadataConfig.ApplicationName);
            metaSection.SetValue("SavedOn", DateTime.Now.ToString());
            iniFile.PrependSection(metaSection);
        }

        return iniFile;
    }

    private void ApplyIniFile(IniFile iniFile)
    {
        // Read and store the metadata section when it exists in the file.
        var metaIniSection = iniFile.GetSection(MetadataSectionName);
        if (metaIniSection != null)
        {
            Metadata = new IniMetadata
            {
                Version         = metaIniSection.GetValue("Version"),
                ApplicationName = metaIniSection.GetValue("CreatedBy"),
                SavedOn         = metaIniSection.GetValue("SavedOn"),
            };
        }
        else
        {
            Metadata = null;
        }

        foreach (var section in Sections.Values)
        {
            var iniSection = iniFile.GetSection(section.SectionName);
            if (iniSection == null) continue;

            foreach (var entry in iniSection.Entries)
            {
                section.SetRawValue(entry.Key, entry.Value);

                // Notify listeners when the key is not recognised by the section's interface.
                if (section is IniSectionBase sectionBase && !sectionBase.IsKnownKey(entry.Key))
                {
                    if (section is IUnknownKey unknownKeyHandler)
                        unknownKeyHandler.OnUnknownKey(entry.Key, entry.Value);
                    UnknownKeyHandler?.Invoke(section.SectionName, entry.Key, entry.Value);
                }
            }
        }
    }

    // ── IDisposable ───────────────────────────────────────────────────────────

    private volatile bool _disposed;

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _watcher?.Dispose();
        _watcher = null;

        // Stop the debounce timer (cancel any pending callback) before disposing it.
        // _disposed is already true so any in-flight callback will bail out immediately.
        var debounceTimer = _reloadDebounceTimer;
        _reloadDebounceTimer = null;
        debounceTimer?.Change(Timeout.Infinite, Timeout.Infinite);
        debounceTimer?.Dispose();

        _autoSaveTimer?.Dispose();
        _autoSaveTimer = null;

        if (_processExitHandler != null)
        {
            AppDomain.CurrentDomain.ProcessExit -= _processExitHandler;
            _processExitHandler = null;
        }

        ReleaseFileLock();
        _reloadSemaphore.Dispose();
    }
}
