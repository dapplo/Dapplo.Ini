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
/// <remarks>
/// <para>
/// Every load or reload happens in two steps: the defaults, user and constants files are read once
/// into a snapshot, and that snapshot is then applied to the registered sections.
/// </para>
/// <para>
/// <see cref="Load"/>, <see cref="Reload"/> and <see cref="Save"/> (and their async variants) are
/// serialised by one lifecycle gate, so an auto-save can never write a half-reloaded state and a
/// section registered on another thread never disturbs a running save.
/// </para>
/// </remarks>
public sealed class IniConfig : IDisposable
{
    internal readonly List<string> SearchPaths = new();
    internal readonly List<string> DefaultFilePaths = new();
    internal readonly List<string> ConstantFilePaths = new();
    internal readonly List<IValueSource> ValueSources = new();
    internal readonly List<IValueSourceAsync> ValueSourcesAsync = new();
    internal readonly SectionStore Sections = new();
    internal readonly List<IIniConfigListener> Listeners = new();

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
    /// Enabled via <see cref="IniConfigBuilder.EnableMetadata"/>.
    /// </summary>
    internal IniMetadataConfig? MetadataConfig;

    // ── Deferred-load configuration (set by IniConfigBuilder.Create) ──────────

    internal bool ShouldLockFile;
    internal bool ShouldMonitorFile;
    internal FileChangedCallback? PendingMonitorCallback;
    /// <summary>Milliseconds to wait before triggering a reload after a file-change notification. Defaults to 200.</summary>
    internal int MonitorDebounceMs = 200;
    internal bool ShouldSaveOnExit;
    internal TimeSpan? ConfiguredAutoSaveInterval;
    internal string? WritablePath;

    /// <summary>
    /// When <c>true</c>, every reference-type property (string, list, array, dictionary) across
    /// all registered sections returns an empty value instead of <c>null</c> when no INI key is
    /// present and the property has no explicit default value.
    /// Set via <see cref="IniConfigBuilder.EmptyWhenNull"/>.
    /// </summary>
    internal bool GlobalEmptyWhenNull;

    /// <summary>Writer options that control how INI files are written on save.</summary>
    internal Parsing.IniWriterOptions WriterOptions = Parsing.IniWriterOptions.Default;

    /// <summary>Parser options that control how INI files are interpreted on load/reload.</summary>
    internal Parsing.IniParserOptions ParserOptions = Parsing.IniParserOptions.Default;

    /// <summary>
    /// The metadata that was read from the <c>[__metadata__]</c> section of the INI file
    /// on the last load / reload.
    /// <c>null</c> when the section did not exist in the file (e.g. first-run or no metadata enabled).
    /// </summary>
    public IniMetadata? Metadata { get; internal set; }

    // ── File lock ─────────────────────────────────────────────────────────────

    private FileStream? _lockStream;
    private readonly object _lockStreamSyncRoot = new();

    // ── File monitoring ───────────────────────────────────────────────────────

    private FileSystemWatcher? _watcher;
    private FileChangedCallback? _fileChangedCallback;
    private volatile bool _postponedReloadPending;
    private System.Threading.Timer? _reloadDebounceTimer;

    // ── Lifecycle gate ────────────────────────────────────────────────────────

    // Serialises Load, Reload, Save and (late) section registration, sync and async alike.
    private readonly SemaphoreSlim _gate = new(1, 1);

    // True in the execution flow that currently holds the gate: lifecycle hooks and listeners that run
    // inside an operation see it, so a nested Save() can run directly and a nested Reload() fails fast
    // instead of deadlocking.
    private readonly AsyncLocal<bool> _holdsGate = new();

    // Re-entrance guard for Save itself (e.g. an IBeforeSave hook that calls Save()).
    private int _isSaving;

    // ── Load state ────────────────────────────────────────────────────────────

    private const int StateNotLoaded = 0;
    private const int StateLoaded = 1;
    private volatile int _loadState = StateNotLoaded;

    // Post-load setup (lock, monitor, save-on-exit, auto-save) must only run once.
    private int _postLoadSetupDone;

    private readonly TaskCompletionSource<bool> _initialLoad =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    // ── Auto-save ─────────────────────────────────────────────────────────────

    private int _autoSavePauseCount;
    private System.Threading.Timer? _autoSaveTimer;
    private EventHandler? _processExitHandler;

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>The logical name of the INI file (e.g. "myapp.ini").</summary>
    public string FileName { get; }

    /// <summary>The resolved absolute path from which the file was loaded, or <c>null</c> if not yet loaded.</summary>
    public string? LoadedFromPath { get; internal set; }

    /// <summary>
    /// <c>true</c> once <see cref="Load"/> / <see cref="LoadAsync"/> (or <see cref="IniConfigBuilder.Build"/>)
    /// has completed successfully.
    /// </summary>
    public bool IsLoaded => _loadState == StateLoaded;

    /// <summary>
    /// Raised after <see cref="Reload"/> successfully re-loads all sections from disk.
    /// </summary>
    public event EventHandler? Reloaded;

    /// <summary>
    /// A <see cref="Task"/> that completes when the initial load of the INI file has finished.
    /// <para>
    /// The task completes when the first <see cref="Load"/> / <see cref="LoadAsync"/> succeeds, whichever way
    /// it was started: <see cref="IniConfigBuilder.Build"/>, <see cref="IniConfigBuilder.BuildAsync"/>, or
    /// <see cref="IniConfigBuilder.Create"/> followed by an explicit load. It faults when that load fails.
    /// After <see cref="IniConfigBuilder.Create"/> the task stays pending until the load has run.
    /// </para>
    /// <para>
    /// Use this property in dependency-injection scenarios where the <see cref="IniConfig"/> or its
    /// sections are injected before loading is complete.
    /// See the project wiki page <em>Singleton-and-DI</em> for a complete example.
    /// </para>
    /// </summary>
    public Task InitialLoadTask => _initialLoad.Task;

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

    /// <summary>
    /// Returns the registered section whose <see cref="IIniSection.SectionName"/> matches
    /// <paramref name="sectionName"/> (case-insensitive), or <c>null</c> when no such
    /// section has been registered.
    /// </summary>
    /// <param name="sectionName">The INI section name as it appears in the file.</param>
    public IIniSection? GetSection(string sectionName) => Sections.FindByName(sectionName);

    /// <summary>
    /// Returns all registered sections in registration order, allowing generic iteration over the
    /// meta model without needing to know the concrete section types at compile time.
    /// </summary>
    public IEnumerable<IIniSection> GetSections() => Sections.Values;

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
    internal void ClearAllDirtyFlags() => ClearDirtyFlags(Sections.Values);

    private static void ClearDirtyFlags(IReadOnlyList<IIniSection> sections)
    {
        foreach (var section in sections)
        {
            if (section is IniSectionBase sectionBase)
                sectionBase.ClearDirtyFlag();
        }
    }

    // ── Lifecycle gate helpers ────────────────────────────────────────────────

    private void ThrowIfInsideLifecycle(string operation)
    {
        if (_holdsGate.Value)
            throw new InvalidOperationException(
                $"{operation}() cannot be called from a lifecycle hook or listener while another load, reload or save " +
                $"of '{FileName}' is running. Mark the section dirty instead, or call {operation}() after the operation has finished.");
    }

    // ── Save ──────────────────────────────────────────────────────────────────

    /// <summary>Saves all sections back to <see cref="LoadedFromPath"/>.</summary>
    /// <remarks>
    /// When another load, reload or save is running, this call waits for it to finish and then saves,
    /// so the latest values are always written. A <c>Save()</c> made from inside a save
    /// (for example from an <see cref="IBeforeSave"/> hook) returns immediately.
    /// </remarks>
    /// <exception cref="InvalidOperationException">Thrown when the file path is not known.</exception>
    public void Save()
    {
        if (_holdsGate.Value)
        {
            // Called from a hook or listener of an operation this flow already runs: we own the gate.
            SaveCore();
            return;
        }

        _gate.Wait();
        _holdsGate.Value = true;
        try
        {
            SaveCore();
        }
        finally
        {
            _holdsGate.Value = false;
            _gate.Release();
        }
    }

    private void SaveCore()
    {
        if (string.IsNullOrEmpty(LoadedFromPath))
            throw new InvalidOperationException("Cannot save: the INI file path is not known.");

        if (Interlocked.CompareExchange(ref _isSaving, 1, 0) != 0)
            return;

        try
        {
            try
            {
                var sections = Sections.Values;

                // Call IBeforeSave hooks; abort if any returns false.
                foreach (var section in sections)
                {
                    if (section is IBeforeSave beforeSave && !beforeSave.OnBeforeSave())
                        return;
                }

                var versions = CaptureChangeVersions(sections);
                var iniFile = BuildIniFile(sections);
                WriteToDisk(() => IniFileWriter.WriteFile(LoadedFromPath!, iniFile, Encoding, WriterOptions));
                MarkSaved(sections, versions);

                NotifyListeners(l => l.OnSaved(LoadedFromPath!));

                foreach (var section in sections)
                {
                    if (section is IAfterSave afterSave)
                        afterSave.OnAfterSave();
                }
            }
            catch (Exception ex)
            {
                NotifyError("Save", ex);
                throw;
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
    /// Waits for a running load, reload or save to finish first. Async lifecycle hooks
    /// (<see cref="IBeforeSaveAsync"/>, <see cref="IAfterSaveAsync"/>) are preferred; when a section
    /// implements only the synchronous hooks (<see cref="IBeforeSave"/>, <see cref="IAfterSave"/>),
    /// those are called instead.
    /// </remarks>
    /// <param name="cancellationToken">Token to cancel the async operation.</param>
    /// <exception cref="InvalidOperationException">Thrown when the file path is not known.</exception>
    public async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        if (_holdsGate.Value)
        {
            await SaveCoreAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        _holdsGate.Value = true;
        try
        {
            await SaveCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _holdsGate.Value = false;
            _gate.Release();
        }
    }

    private async Task SaveCoreAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(LoadedFromPath))
            throw new InvalidOperationException("Cannot save: the INI file path is not known.");

        if (Interlocked.CompareExchange(ref _isSaving, 1, 0) != 0)
            return;

        try
        {
            try
            {
                var sections = Sections.Values;

                foreach (var section in sections)
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

                var versions = CaptureChangeVersions(sections);
                var iniFile = BuildIniFile(sections);

                if (_watcher != null) _watcher.EnableRaisingEvents = false;
                try
                {
                    if (ShouldLockFile) ReleaseFileLock();
                    try
                    {
                        await IniFileWriter.WriteFileAsync(LoadedFromPath!, iniFile, Encoding, WriterOptions, cancellationToken).ConfigureAwait(false);
                    }
                    finally
                    {
                        if (ShouldLockFile) AcquireFileLock();
                    }
                }
                finally
                {
                    if (_watcher != null) _watcher.EnableRaisingEvents = true;
                }

                MarkSaved(sections, versions);
                NotifyListeners(l => l.OnSaved(LoadedFromPath!));

                foreach (var section in sections)
                {
                    if (section is IAfterSaveAsync afterSaveAsync)
                        await afterSaveAsync.OnAfterSaveAsync(cancellationToken).ConfigureAwait(false);
                    else if (section is IAfterSave afterSave)
                        afterSave.OnAfterSave();
                }
            }
            catch (Exception ex)
            {
                NotifyError("Save", ex);
                throw;
            }
        }
        finally
        {
            Interlocked.Exchange(ref _isSaving, 0);
        }
    }

    /// <summary>
    /// Runs <paramref name="write"/> with the file watcher paused (so our own write is never reported as an
    /// external change) and the file lock released (so this process can replace the file).
    /// </summary>
    private void WriteToDisk(Action write)
    {
        if (_watcher != null) _watcher.EnableRaisingEvents = false;
        try
        {
            if (ShouldLockFile) ReleaseFileLock();
            try
            {
                write();
            }
            finally
            {
                if (ShouldLockFile) AcquireFileLock();
            }
        }
        finally
        {
            if (_watcher != null) _watcher.EnableRaisingEvents = true;
        }
    }

    private static int[] CaptureChangeVersions(IReadOnlyList<IIniSection> sections)
    {
        var versions = new int[sections.Count];
        for (var i = 0; i < sections.Count; i++)
            versions[i] = (sections[i] as IniSectionBase)?.ChangeVersion ?? 0;
        return versions;
    }

    private static void MarkSaved(IReadOnlyList<IIniSection> sections, int[] versions)
    {
        for (var i = 0; i < sections.Count; i++)
            (sections[i] as IniSectionBase)?.MarkSaved(versions[i]);
    }

    // ── Reload ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Reloads all sections in-place from the INI file and any registered default/constant files
    /// and external value sources.  Existing object references remain valid (singleton guarantee).
    /// </summary>
    /// <remarks>
    /// The reload sequence is:
    /// <list type="number">
    ///   <item>Read the defaults, user and constants files.</item>
    ///   <item>Reset every section to its compiled defaults.</item>
    ///   <item>Apply the defaults files, the user file and the constants files, in that order.</item>
    ///   <item>Apply registered external <see cref="IValueSource"/> instances.</item>
    ///   <item>Clear dirty flags (freshly loaded data is not considered unsaved).</item>
    ///   <item>Fire <see cref="IAfterLoad"/> hooks on every section (changes they make stay dirty).</item>
    ///   <item>Raise <see cref="Reloaded"/>.</item>
    /// </list>
    /// </remarks>
    /// <exception cref="InvalidOperationException">Thrown when called from a lifecycle hook or listener of a running operation.</exception>
    public void Reload()
    {
        ThrowIfInsideLifecycle(nameof(Reload));
        _gate.Wait();
        _holdsGate.Value = true;
        try
        {
            try
            {
                _postponedReloadPending = false;
                var snapshot = ReadLayers(resolveUserFile: false);
                var sections = Sections.Values;
                ApplySnapshot(sections, snapshot, updateMetadata: true);
                ApplyValueSources(sections);
                ClearDirtyFlags(sections);
                RunAfterLoadHooks(sections);
            }
            catch (Exception ex)
            {
                NotifyError("Reload", ex);
                throw;
            }
        }
        finally
        {
            _holdsGate.Value = false;
            _gate.Release();
        }

        Reloaded?.Invoke(this, EventArgs.Empty);
        NotifyListeners(l => l.OnReloaded(LoadedFromPath ?? FileName));
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
    /// <exception cref="InvalidOperationException">Thrown when called from a lifecycle hook or listener of a running operation.</exception>
    public async Task ReloadAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfInsideLifecycle(nameof(ReloadAsync));
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        _holdsGate.Value = true;
        try
        {
            try
            {
                _postponedReloadPending = false;
                var snapshot = await ReadLayersAsync(resolveUserFile: false, cancellationToken).ConfigureAwait(false);
                var sections = Sections.Values;
                ApplySnapshot(sections, snapshot, updateMetadata: true);
                await ApplyValueSourcesAsync(sections, cancellationToken).ConfigureAwait(false);
                ClearDirtyFlags(sections);
                await RunAfterLoadHooksAsync(sections, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                NotifyError("Reload", ex);
                throw;
            }
        }
        finally
        {
            _holdsGate.Value = false;
            _gate.Release();
        }

        Reloaded?.Invoke(this, EventArgs.Empty);
        NotifyListeners(l => l.OnReloaded(LoadedFromPath ?? FileName));
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
    /// Calling this method when a lock is already held is a no-op.
    /// </summary>
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

    /// <summary>Releases the file lock acquired by <see cref="AcquireFileLock"/> (if any).</summary>
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
    /// </summary>
    internal void StartMonitoring(FileChangedCallback? callback)
    {
        if (string.IsNullOrEmpty(LoadedFromPath)) return;

        _fileChangedCallback = callback;

        // Debounce timer starts in the stopped state; OnFileChanged arms it on demand.
        _reloadDebounceTimer = new System.Threading.Timer(_ =>
        {
            if (!_disposed)
                RunInBackground("Reload", Reload);
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
        var decision = _fileChangedCallback?.Invoke(e.FullPath) ?? ReloadDecision.Reload;

        switch (decision)
        {
            case ReloadDecision.Reload:
                _reloadDebounceTimer?.Change(MonitorDebounceMs, Timeout.Infinite);
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

    internal void EnableSaveOnExit()
    {
        _processExitHandler = (_, _) =>
        {
            if (!_disposed)
                RunInBackground("Save", Save);
        };
        AppDomain.CurrentDomain.ProcessExit += _processExitHandler;
    }

    // ── Auto-save pause ───────────────────────────────────────────────────────

    /// <summary>
    /// Suspends the auto-save timer so that automatic saves are skipped until
    /// <see cref="ResumeAutoSave"/> is called.  Calls may be nested: each
    /// <see cref="PauseAutoSave"/> must be matched by a corresponding
    /// <see cref="ResumeAutoSave"/>.
    /// </summary>
    public void PauseAutoSave() => Interlocked.Increment(ref _autoSavePauseCount);

    /// <summary>
    /// Resumes auto-save after a prior <see cref="PauseAutoSave"/> call.
    /// If auto-save was not paused (or has already been fully resumed), this is a no-op.
    /// </summary>
    public void ResumeAutoSave()
    {
        var updated = Interlocked.Decrement(ref _autoSavePauseCount);
        if (updated < 0)
            Interlocked.Increment(ref _autoSavePauseCount);
    }

    // ── Auto-save timer ───────────────────────────────────────────────────────

    /// <summary>
    /// Starts a timer that periodically saves when <see cref="HasPendingChanges"/> returns <c>true</c>
    /// and auto-save is not paused. When another operation is running, that tick is skipped and the
    /// next tick tries again.
    /// </summary>
    internal void StartAutoSave(TimeSpan interval)
    {
        _autoSaveTimer = new System.Threading.Timer(_ =>
        {
            if (_disposed || Volatile.Read(ref _autoSavePauseCount) != 0 || !HasPendingChanges())
                return;
            if (!_gate.Wait(0))
                return;
            _holdsGate.Value = true;
            try
            {
                RunInBackground("Save", SaveCore);
            }
            finally
            {
                _holdsGate.Value = false;
                _gate.Release();
            }
        }, null, interval, interval);
    }

    // ── Section registration ──────────────────────────────────────────────────

    /// <summary>
    /// Registers a section instance without loading any values from disk.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the mechanism for <em>distributed registrations</em> used in plugin-based
    /// applications.  The host calls <see cref="IniConfigBuilder.Create"/> to create the
    /// <see cref="IniConfig"/> and register it in the global registry without loading files.
    /// Each plugin's pre-initialization method then retrieves the shared config from
    /// <see cref="IniConfigRegistry.Get(string)"/> and registers its own section.
    /// See the project wiki page <em>Plugin-Registrations</em> for the three-phase flow.
    /// </para>
    /// <para>
    /// If a section of the same type has already been registered, it is replaced.
    /// Call <see cref="Load"/> (or <see cref="LoadAsync"/>) after all sections have been
    /// added to read the INI file(s) exactly once for all sections.
    /// </para>
    /// </remarks>
    /// <typeparam name="T">
    /// The INI section interface type. When <typeparamref name="T"/> is a concrete class, the section
    /// is registered under the most derived <see cref="IIniSection"/>-derived interface it implements.
    /// </typeparam>
    /// <param name="section">The concrete section instance to register.</param>
    /// <returns>The <paramref name="section"/> instance (for fluent chaining).</returns>
#if NET
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2026",
        Justification = "Reflection is only used when T is a concrete class; pass the interface type for trim safety.")]
#endif
    public T AddSection<T>(T section) where T : IIniSection
    {
        if (section is null) throw new ArgumentNullException(nameof(section));
        Sections.Set(SectionStore.ResolveKeyType(typeof(T), section.GetType()), section);
        return section;
    }

#if NET
    /// <summary>
    /// Registers a section instance without loading any values from disk,
    /// inferring the section interface type at runtime.
    /// </summary>
    /// <remarks>
    /// Prefer the generic overload <see cref="AddSection{T}"/> for explicit control and
    /// AOT/trim compatibility.
    /// </remarks>
    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode(
        "Inspects implemented interfaces at runtime to infer the section type. " +
        "Use the generic AddSection<T> overload instead to preserve trim/AOT compatibility.")]
#endif
    public IIniSection AddSection(IIniSection section)
    {
        if (section is null) throw new ArgumentNullException(nameof(section));
        Sections.Set(SectionStore.ResolveKeyType(section.GetType(), section.GetType()), section);
        return section;
    }

    // ── Load (initial / deferred) ─────────────────────────────────────────────

    /// <summary>
    /// Reads all registered default, user, and constant INI files and applies external value
    /// sources to every registered section — exactly once, in a single pass.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Call this after all sections have been registered via
    /// <see cref="IniConfigBuilder.RegisterSection{T}"/> (builder) or
    /// <see cref="AddSection{T}"/> (plugin pre-init) to load all of them together without
    /// repeated file I/O.
    /// </para>
    /// <para>
    /// This method is called internally by <see cref="IniConfigBuilder.Build"/>;
    /// call it explicitly only when using <see cref="IniConfigBuilder.Create"/> for
    /// deferred loading. Calling it again re-reads the files; the file lock, file monitor,
    /// save-on-exit handler and auto-save timer are only set up by the first successful load.
    /// </para>
    /// </remarks>
    /// <returns>This <see cref="IniConfig"/> instance for fluent chaining.</returns>
    /// <exception cref="InvalidOperationException">Thrown when called from a lifecycle hook or listener of a running operation.</exception>
    public IniConfig Load()
    {
        ThrowIfInsideLifecycle(nameof(Load));
        _gate.Wait();
        _holdsGate.Value = true;
        try
        {
            try
            {
                var snapshot = ReadLayers(resolveUserFile: true);
                UpdateLoadedFromPath(snapshot);
                var sections = Sections.Values;
                ApplySnapshot(sections, snapshot, updateMetadata: true);
                ApplyValueSources(sections);
                ClearDirtyFlags(sections);
                _loadState = StateLoaded;
                RunAfterLoadHooks(sections);
                NotifyFileResult(snapshot);
            }
            catch (Exception ex)
            {
                NotifyError("Load", ex);
                _initialLoad.TrySetException(ex);
                throw;
            }
        }
        finally
        {
            _holdsGate.Value = false;
            _gate.Release();
        }

        RunPostLoadSetupOnce();
        _initialLoad.TrySetResult(true);
        return this;
    }

    /// <summary>
    /// Asynchronously reads all registered default, user, and constant INI files and applies
    /// external value sources to every registered section — exactly once, in a single pass.
    /// </summary>
    /// <remarks>
    /// Async lifecycle hooks (<see cref="IAfterLoadAsync"/>) are preferred; when a section
    /// implements only the synchronous <see cref="IAfterLoad"/> hook, that is called instead.
    /// Async value sources (<see cref="IValueSourceAsync"/>) are applied after all synchronous
    /// sources.
    /// </remarks>
    /// <param name="cancellationToken">Token to cancel the async operation.</param>
    /// <returns>This <see cref="IniConfig"/> instance for fluent chaining.</returns>
    /// <exception cref="InvalidOperationException">Thrown when called from a lifecycle hook or listener of a running operation.</exception>
    public async Task<IniConfig> LoadAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfInsideLifecycle(nameof(LoadAsync));
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        _holdsGate.Value = true;
        try
        {
            try
            {
                var snapshot = await ReadLayersAsync(resolveUserFile: true, cancellationToken).ConfigureAwait(false);
                UpdateLoadedFromPath(snapshot);
                var sections = Sections.Values;
                ApplySnapshot(sections, snapshot, updateMetadata: true);
                await ApplyValueSourcesAsync(sections, cancellationToken).ConfigureAwait(false);
                ClearDirtyFlags(sections);
                _loadState = StateLoaded;
                await RunAfterLoadHooksAsync(sections, cancellationToken).ConfigureAwait(false);
                NotifyFileResult(snapshot);
            }
            catch (Exception ex)
            {
                NotifyError("Load", ex);
                _initialLoad.TrySetException(ex);
                throw;
            }
        }
        finally
        {
            _holdsGate.Value = false;
            _gate.Release();
        }

        RunPostLoadSetupOnce();
        _initialLoad.TrySetResult(true);
        return this;
    }

    private void UpdateLoadedFromPath(LayerSnapshot snapshot)
    {
        if (snapshot.UserFilePath != null)
        {
            LoadedFromPath = snapshot.UserFilePath;
        }
        else if (WritablePath != null)
        {
            LoadedFromPath = WritablePath;
        }
        else
        {
            var firstWritable = SearchPaths.FirstOrDefault(Directory.Exists);
            if (firstWritable != null)
                LoadedFromPath = Path.Combine(firstWritable, FileName);
        }
    }

    private void NotifyFileResult(LayerSnapshot snapshot)
    {
        if (snapshot.UserFilePath != null)
            NotifyListeners(l => l.OnFileLoaded(snapshot.UserFilePath));
        else
            NotifyListeners(l => l.OnFileNotFound(FileName));
    }

    /// <summary>File lock, file monitor, save-on-exit and auto-save: set up once, after the first successful load.</summary>
    private void RunPostLoadSetupOnce()
    {
        if (Interlocked.Exchange(ref _postLoadSetupDone, 1) != 0)
            return;

        if (ShouldLockFile)
            AcquireFileLock();
        if (ShouldMonitorFile)
            StartMonitoring(PendingMonitorCallback);
        if (ShouldSaveOnExit)
            EnableSaveOnExit();
        if (ConfiguredAutoSaveInterval.HasValue)
            StartAutoSave(ConfiguredAutoSaveInterval.Value);
    }

    // ── Reading the layers ────────────────────────────────────────────────────

    /// <summary>
    /// Reads and parses the defaults files, the user file and the constants files.
    /// This is the only place where the configuration files are read.
    /// </summary>
    /// <param name="resolveUserFile">
    /// <c>true</c> for a load: locate the user file through the search paths.
    /// <c>false</c> for a reload: re-read <see cref="LoadedFromPath"/>.
    /// </param>
    private LayerSnapshot ReadLayers(bool resolveUserFile)
    {
        var defaults = new List<IniFile>();
        foreach (var path in DefaultFilePaths)
        {
            var resolved = ResolveAuxiliaryFilePath(path);
            if (resolved != null)
                defaults.Add(IniFileParser.ParseFile(resolved, Encoding, ParserOptions));
        }

        var userPath = GetUserFilePathToRead(resolveUserFile);
        var user = userPath != null ? IniFileParser.ParseFile(userPath, Encoding, ParserOptions) : null;

        var constants = new List<IniFile>();
        foreach (var path in ConstantFilePaths)
        {
            var resolved = ResolveAuxiliaryFilePath(path);
            if (resolved != null)
                constants.Add(IniFileParser.ParseFile(resolved, Encoding, ParserOptions));
        }

        return new LayerSnapshot(defaults, userPath, user, constants);
    }

    /// <summary>Asynchronous variant of <see cref="ReadLayers"/>.</summary>
    private async Task<LayerSnapshot> ReadLayersAsync(bool resolveUserFile, CancellationToken cancellationToken)
    {
        var defaults = new List<IniFile>();
        foreach (var path in DefaultFilePaths)
        {
            var resolved = ResolveAuxiliaryFilePath(path);
            if (resolved != null)
                defaults.Add(await IniFileParser.ParseFileAsync(resolved, Encoding, ParserOptions, cancellationToken).ConfigureAwait(false));
        }

        var userPath = GetUserFilePathToRead(resolveUserFile);
        var user = userPath != null
            ? await IniFileParser.ParseFileAsync(userPath, Encoding, ParserOptions, cancellationToken).ConfigureAwait(false)
            : null;

        var constants = new List<IniFile>();
        foreach (var path in ConstantFilePaths)
        {
            var resolved = ResolveAuxiliaryFilePath(path);
            if (resolved != null)
                constants.Add(await IniFileParser.ParseFileAsync(resolved, Encoding, ParserOptions, cancellationToken).ConfigureAwait(false));
        }

        return new LayerSnapshot(defaults, userPath, user, constants);
    }

    private string? GetUserFilePathToRead(bool resolveUserFile)
    {
        if (resolveUserFile)
            return ResolveFilePath();
        return !string.IsNullOrEmpty(LoadedFromPath) && File.Exists(LoadedFromPath) ? LoadedFromPath : null;
    }

    // ── Applying the layers ───────────────────────────────────────────────────

    /// <summary>
    /// Resets <paramref name="sections"/> to their compiled defaults and applies the defaults files,
    /// the user file and the constants files from <paramref name="snapshot"/>, without any file I/O.
    /// </summary>
    private void ApplySnapshot(IReadOnlyList<IIniSection> sections, LayerSnapshot snapshot, bool updateMetadata)
    {
        foreach (var section in sections)
            ResetSection(section);

        foreach (var defaults in snapshot.Defaults)
            ApplyIniFile(defaults, sections, isDefault: true);

        if (snapshot.User != null)
        {
            if (updateMetadata)
                Metadata = ReadMetadata(snapshot.User);
            ApplyIniFile(snapshot.User, sections);
        }

        foreach (var constants in snapshot.Constants)
            ApplyIniFile(constants, sections, isConstant: true);
    }

    private void ResetSection(IIniSection section)
    {
        if (section is IniSectionBase sectionBase)
        {
            // GlobalEmptyWhenNull must be set BEFORE ResetToDefaults() because the generated
            // ResetToDefaults() reads it. ClearConstants() and ClearRawValues() make sure that
            // protections and keys removed from the files since the last load do not linger.
            sectionBase.GlobalEmptyWhenNull = GlobalEmptyWhenNull;
            sectionBase.ClearConstants();
            sectionBase.ClearRawValues();
        }
        section.ResetToDefaults();
    }

    private static IniMetadata? ReadMetadata(IniFile userFile)
    {
        var metaIniSection = userFile.GetSection(MetadataSectionName);
        if (metaIniSection == null)
            return null;

        return new IniMetadata
        {
            Version         = metaIniSection.GetValue("Version"),
            ApplicationName = metaIniSection.GetValue("CreatedBy"),
            SavedOn         = metaIniSection.GetValue("SavedOn"),
            CommitHash      = metaIniSection.GetValue("CommitHash"),
        };
    }

    private static void RunAfterLoadHooks(IReadOnlyList<IIniSection> sections)
    {
        foreach (var section in sections)
        {
            if (section is IAfterLoad afterLoad)
                afterLoad.OnAfterLoad();
        }
    }

    private static async Task RunAfterLoadHooksAsync(IReadOnlyList<IIniSection> sections, CancellationToken cancellationToken)
    {
        foreach (var section in sections)
        {
            if (section is IAfterLoadAsync afterLoadAsync)
                await afterLoadAsync.OnAfterLoadAsync(cancellationToken).ConfigureAwait(false);
            else if (section is IAfterLoad afterLoad)
                afterLoad.OnAfterLoad();
        }
    }

    // ── Value sources ─────────────────────────────────────────────────────────

    internal void ApplyValueSources() => ApplyValueSources(Sections.Values);

    private void ApplyValueSources(IReadOnlyList<IIniSection> sections)
    {
        foreach (var source in ValueSources)
        {
            foreach (var section in sections)
            {
                foreach (var key in section.GetKeys())
                {
                    if (source.TryGetValue(section.SectionName, key, out var value))
                        section.SetRawValue(key, value);
                }
            }
        }
    }

    internal Task ApplyValueSourcesAsync(CancellationToken cancellationToken = default)
        => ApplyValueSourcesAsync(Sections.Values, cancellationToken);

    private async Task ApplyValueSourcesAsync(IReadOnlyList<IIniSection> sections, CancellationToken cancellationToken)
    {
        // Apply synchronous sources first
        ApplyValueSources(sections);

        foreach (var source in ValueSourcesAsync)
        {
            foreach (var section in sections)
            {
                foreach (var key in section.GetKeys())
                {
                    var (found, value) = await source.TryGetValueAsync(
                        section.SectionName, key, cancellationToken).ConfigureAwait(false);
                    if (found)
                        section.SetRawValue(key, value);
                }
            }
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Notifies all registered listeners by invoking <paramref name="notify"/> on each one.
    /// This is a no-op when no listeners are registered (zero overhead path).
    /// </summary>
    private void NotifyListeners(Action<IIniConfigListener> notify)
    {
        if (Listeners.Count == 0) return;
        foreach (var listener in Listeners)
            notify(listener);
    }

    // Key stored in Exception.Data once an exception has been reported to the listeners,
    // so that background wrappers do not report the same exception twice.
    private const string ReportedKey = "Dapplo.Ini.ReportedToListeners";

    /// <summary>Reports <paramref name="exception"/> to all listeners via <see cref="IIniConfigListener.OnError"/>.</summary>
    private void NotifyError(string operation, Exception exception)
    {
        try { exception.Data[ReportedKey] = true; }
        catch { /* some exceptions have a read-only Data dictionary */ }
        NotifyListeners(l => l.OnError(operation, exception));
    }

    /// <summary>
    /// Runs <paramref name="action"/> in a background context (timer, file watcher, process exit),
    /// where an unhandled exception would terminate the process. Exceptions are reported to the
    /// listeners instead of being re-thrown.
    /// </summary>
    private void RunInBackground(string operation, Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            if (ex.Data.Contains(ReportedKey)) return;
            try { NotifyError(operation, ex); }
            catch { /* a failing listener must not crash the process */ }
        }
    }

    // The name of the special metadata section prepended to the INI file when opted in.
    internal const string MetadataSectionName = "__metadata__";

    internal IniFile BuildIniFile() => BuildIniFile(Sections.Values);

    private IniFile BuildIniFile(IReadOnlyList<IIniSection> sections)
    {
        var iniFile = new Parsing.IniFile();
        iniFile.AssignmentSeparator = WriterOptions.AssignmentSeparator;
        foreach (var section in sections)
        {
            if (section is IniSectionBase sectionBase)
            {
                var sectionDesc = sectionBase.GetSectionDescription();
                var sectionComments = sectionDesc != null
                    ? (IReadOnlyList<string>)new[] { sectionDesc }
                    : Array.Empty<string>();
                var iniSection = new Parsing.IniSection(section.SectionName, sectionComments)
                {
                    WriterOptionsOverride = sectionBase.GetSectionWriterOptions()
                };
                iniFile.AddSection(iniSection);
                var describedSubKeyDictionaries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var rawKvp in sectionBase.GetAllRawValues())
                {
                    var propDesc = sectionBase.GetPropertyDescription(rawKvp.Key);
                    if (propDesc == null)
                    {
                        var keySeparatorIndex = rawKvp.Key.IndexOf('.');
                        if (keySeparatorIndex > 0)
                        {
                            var prefixKey = rawKvp.Key.Substring(0, keySeparatorIndex);
                            propDesc = sectionBase.GetPropertyDescription(prefixKey);
                            if (propDesc != null && !describedSubKeyDictionaries.Add(prefixKey))
                                propDesc = null;
                        }
                    }
                    var propComments = propDesc != null
                        ? (IReadOnlyList<string>)new[] { propDesc }
                        : Array.Empty<string>();
                    iniSection.SetEntry(new Parsing.IniEntry(rawKvp.Key, rawKvp.Value, propComments)
                    {
                        WriterOptionsOverride = sectionBase.GetPropertyWriterOptions(rawKvp.Key)
                    });
                }
            }
            else
            {
                // Non-generated sections: add an empty section placeholder to the file.
                iniFile.AddSection(new Parsing.IniSection(section.SectionName, Array.Empty<string>()));
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
            if (!string.IsNullOrEmpty(MetadataConfig.CommitHash))
                metaSection.SetValue("CommitHash", MetadataConfig.CommitHash);
            iniFile.PrependSection(metaSection);
        }

        return iniFile;
    }

    private void ApplyIniFile(IniFile iniFile, IReadOnlyList<IIniSection> sections, bool isConstant = false, bool isDefault = false)
    {
        foreach (var section in sections)
        {
            var iniSection = iniFile.GetSection(section.SectionName);
            if (iniSection == null) continue;

            var sectionBase = section as IniSectionBase;

            // Check section-level skip flags before processing any entries.
            if (isDefault && sectionBase?.SectionIgnoresDefaults == true) continue;
            if (isConstant && sectionBase?.SectionIgnoresConstants == true) continue;

            // Wire the conversion-failed callback so IniSectionBase can report to listeners.
            if (sectionBase != null && Listeners.Count > 0)
            {
                sectionBase.ConversionFailedCallback = (sName, key, raw, ex) =>
                    NotifyListeners(l => l.OnValueConversionFailed(sName, key, raw, ex));
            }

            try
            {
                foreach (var entry in iniSection.Entries)
                {
                    // Check property-level skip flags.
                    if (isDefault && sectionBase?.IsIgnoreDefaultsKey(entry.Key) == true) continue;
                    if (isConstant && sectionBase?.IsIgnoreConstantsKey(entry.Key) == true) continue;

                    section.SetRawValue(entry.Key, entry.Value);

                    // When applying a constants file, protect this key from further changes.
                    if (isConstant && sectionBase != null)
                        sectionBase.MarkKeyAsConstant(entry.Key);

                    // Notify when the key is not recognised by the section's interface.
                    if (sectionBase != null && !sectionBase.IsKnownKey(entry.Key))
                    {
                        if (section is IUnknownKey unknownKeyHandler)
                            unknownKeyHandler.OnUnknownKey(entry.Key, entry.Value);
                        UnknownKeyHandler?.Invoke(section.SectionName, entry.Key, entry.Value);
                        NotifyListeners(l => l.OnUnknownKey(section.SectionName, entry.Key, entry.Value));
                    }
                }
            }
            finally
            {
                // Always clear the callback so it doesn't hold references beyond this file apply.
                if (sectionBase != null)
                    sectionBase.ConversionFailedCallback = null;
            }
        }
    }

    /// <summary>Resolves the file path by searching <see cref="SearchPaths"/>.</summary>
    private string? ResolveFilePath()
    {
        foreach (var dir in SearchPaths)
        {
            var candidate = Path.Combine(dir, FileName);
            if (File.Exists(candidate))
                return candidate;
        }
        return null;
    }

    /// <summary>
    /// Resolves an auxiliary file path (used for default and constant files).
    /// </summary>
    /// <remarks>
    /// When <paramref name="filePath"/> contains a directory component it is used as-is.
    /// When it is a bare filename, every directory in <see cref="SearchPaths"/> is tried in
    /// order — the first match wins.
    /// </remarks>
    private string? ResolveAuxiliaryFilePath(string filePath)
    {
        if (!string.IsNullOrEmpty(Path.GetDirectoryName(filePath)))
            return File.Exists(filePath) ? filePath : null;

        foreach (var dir in SearchPaths)
        {
            var candidate = Path.Combine(dir, filePath);
            if (File.Exists(candidate))
                return candidate;
        }
        return null;
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

        // Let an operation that is already running (e.g. an auto-save) finish before the lock is released,
        // unless Dispose is called from inside that operation.
        if (!_holdsGate.Value && _gate.Wait(TimeSpan.FromSeconds(5)))
            _gate.Release();

        ReleaseFileLock();
        // The gate is intentionally not disposed: an operation that is still running on another
        // thread must be able to release it. SemaphoreSlim holds no unmanaged resources unless
        // AvailableWaitHandle is used, which it is not.
    }
}
