// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using Dapplo.IniConfig.Interfaces;
using Dapplo.IniConfig.Parsing;

namespace Dapplo.IniConfig.Configuration;

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
    internal readonly Dictionary<Type, IIniSection> Sections = new();

    // ── Encoding ──────────────────────────────────────────────────────────────

    /// <summary>Encoding used when reading and writing the INI file. Defaults to UTF-8.</summary>
    internal Encoding Encoding = Encoding.UTF8;

    // ── File lock ─────────────────────────────────────────────────────────────

    // Held when the caller requested file locking via IniConfigBuilder.LockFile().
    private FileStream? _lockStream;
    private readonly object _lockStreamSyncRoot = new();

    // ── File monitoring ───────────────────────────────────────────────────────

    private FileSystemWatcher? _watcher;
    private FileChangedCallback? _fileChangedCallback;

    // Tracks whether a postponed reload is pending.
    private volatile bool _postponedReloadPending;

    // ── Save re-entrance guard ────────────────────────────────────────────────

    // 0 = idle, 1 = save in progress.  Manipulated via Interlocked to allow concurrent callers
    // (e.g. the auto-save timer and a manual Save() call) to detect overlap and bail out.
    private int _isSaving;

    // ── Auto-save pause ───────────────────────────────────────────────────────

    // Nestable pause counter. When > 0 the auto-save timer skips its save.
    // PauseAutoSave increments; ResumeAutoSave decrements.
    private int _autoSavePauseCount;

    // ── Reload sync ───────────────────────────────────────────────────────────

    private readonly object _reloadLock = new();

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
        lock (_reloadLock)
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
                Reload();
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

    /// <summary>Returns all known keys for a section (from GetAllRawValues if available).</summary>
    private static IEnumerable<string> GetSectionKeys(IIniSection section)
    {
        if (section is IniSectionBase sectionBase)
            return sectionBase.GetAllRawValues().Select(kvp => kvp.Key);
        return Array.Empty<string>();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    internal IniFile BuildIniFile()
    {
        var iniFile = new Parsing.IniFile();
        foreach (var section in Sections.Values)
        {
            var iniSection = iniFile.GetOrAddSection(section.SectionName);
            if (section is IniSectionBase sectionBase)
            {
                foreach (var kvp in sectionBase.GetAllRawValues())
                    iniSection.SetValue(kvp.Key, kvp.Value);
            }
        }
        return iniFile;
    }

    private void ApplyIniFile(IniFile iniFile)
    {
        foreach (var section in Sections.Values)
        {
            var iniSection = iniFile.GetSection(section.SectionName);
            if (iniSection == null) continue;

            foreach (var entry in iniSection.Entries)
                section.SetRawValue(entry.Key, entry.Value);
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

        _autoSaveTimer?.Dispose();
        _autoSaveTimer = null;

        if (_processExitHandler != null)
        {
            AppDomain.CurrentDomain.ProcessExit -= _processExitHandler;
            _processExitHandler = null;
        }

        ReleaseFileLock();
    }
}
