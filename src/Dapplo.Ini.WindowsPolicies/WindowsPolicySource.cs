// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Globalization;
using System.Security;
using Dapplo.Ini.Interfaces;
using Dapplo.Ini.WindowsPolicies.Registry;
using Dapplo.Ini.WindowsPolicies.Watching;
using Microsoft.Win32;

namespace Dapplo.Ini.WindowsPolicies;

/// <summary>
/// A value source that reads Windows policies of one hive and one <see cref="PolicyKind"/>:
/// <c>&lt;Root&gt;\&lt;Section&gt;\&lt;Key&gt;</c> (mandatory) or <c>&lt;Root&gt;\Recommended\&lt;Section&gt;\&lt;Key&gt;</c>
/// (recommended), where the root is by default <c>SOFTWARE\Policies\&lt;Vendor&gt;\&lt;Product&gt;</c>.
/// </summary>
/// <remarks>
/// Values are mapped by the property type (see <see cref="PolicyValueType"/>) to the raw INI text; a value of the
/// wrong type is reported via <see cref="IIniConfigListener.OnValueConversionFailed"/> and skipped. While someone
/// listens to <see cref="ValueChanged"/> the policy keys are watched and every (debounced) change is reported as a
/// change of all values. Usually registered with <see cref="WindowsPolicyBuilderExtensions.AddWindowsPolicies"/>.
/// </remarks>
public sealed class WindowsPolicySource : IValueSource, ISectionValueSource, INamedValueSource, IDisposable
{
    private readonly IRegistryRoot _registry;
    private readonly Func<IPolicyChangeWatcher>? _watcherFactory;
    private readonly string _sectionsPath;
    private readonly object _lock = new();
    private EventHandler<ValueChangedEventArgs>? _valueChanged;
    private IPolicyChangeWatcher? _watcher;
    private ChangeDebouncer? _debouncer;
    private bool _disposed;

    /// <summary>Creates a source for the policies of <paramref name="hive"/> below <paramref name="rootPath"/>.</summary>
    /// <param name="hive"><see cref="RegistryHive.LocalMachine"/> or <see cref="RegistryHive.CurrentUser"/>.</param>
    /// <param name="kind">Mandatory or recommended policies.</param>
    /// <param name="rootPath">The policy root below the hive, e.g. <c>SOFTWARE\Policies\MyCompany\MyProduct</c>.</param>
    /// <param name="view">The registry view; default the 64-bit view.</param>
    /// <param name="watchForChanges">Watch the keys while someone listens to <see cref="ValueChanged"/>.</param>
    /// <param name="changeDebounce">How long to wait for further changes before reporting them; default 250 ms.</param>
    public WindowsPolicySource(RegistryHive hive, PolicyKind kind, string rootPath, RegistryView view = RegistryView.Registry64,
        bool watchForChanges = true, TimeSpan? changeDebounce = null)
        : this(hive, kind, rootPath, new WindowsRegistryRoot(hive, view),
            watchForChanges ? () => RegistryChangeWatcher.Acquire(hive, view, rootPath) : null, changeDebounce)
    {
    }

    internal WindowsPolicySource(RegistryHive hive, PolicyKind kind, string rootPath, IRegistryRoot registry,
        Func<IPolicyChangeWatcher>? watcherFactory, TimeSpan? changeDebounce = null)
    {
        if (hive is not (RegistryHive.LocalMachine or RegistryHive.CurrentUser))
            throw new ArgumentOutOfRangeException(nameof(hive), hive, "Policies live in HKEY_LOCAL_MACHINE or HKEY_CURRENT_USER.");
        if (string.IsNullOrWhiteSpace(rootPath))
            throw new ArgumentException("A policy root path is needed.", nameof(rootPath));
        Hive = hive;
        Kind = kind;
        RootPath = rootPath.Trim('\\');
        _registry = registry;
        _watcherFactory = watcherFactory;
        ChangeDebounce = changeDebounce ?? TimeSpan.FromMilliseconds(250);
        _sectionsPath = kind == PolicyKind.Recommended ? RootPath + "\\" + WindowsPolicyOptions.RecommendedKeyName : RootPath;
        Name = $"Windows policy ({(hive == RegistryHive.LocalMachine ? "HKLM" : "HKCU")}, {(kind == PolicyKind.Mandatory ? "mandatory" : "recommended")})";
    }

    /// <summary>The hive.</summary>
    public RegistryHive Hive { get; }

    /// <summary>Mandatory or recommended.</summary>
    public PolicyKind Kind { get; }

    /// <summary>The policy root below the hive.</summary>
    public string RootPath { get; }

    /// <summary>The debounce of the change notifications.</summary>
    public TimeSpan ChangeDebounce { get; }

    /// <summary>For example <c>Windows policy (HKLM, mandatory)</c>.</summary>
    public string Name { get; }

    /// <summary>The key that holds the value of <paramref name="sectionName"/>, below the hive.</summary>
    public string GetSectionPath(string sectionName) => _sectionsPath + "\\" + sectionName;

    /// <inheritdoc/>
    public bool TryGetValue(IIniSection section, string key, out string? value)
    {
        value = null;
        var info = PolicyMetadata.GetKey(section, key);
        if (info is { IsNotAdministrable: true } or { IsRuntimeOnly: true })
            return false;
        return Read(section.SectionName, sectionKey =>
        {
            if (info != null)
            {
                var found = PolicyValueMapper.TryRead(sectionKey, info, out var raw);
                return (found, raw);
            }
            return ReadUntyped(sectionKey, key);
        }, out value);
    }

    /// <inheritdoc/>
    /// <remarks>Without the section the value is passed on as text (numbers in decimal, multi strings joined with commas).</remarks>
    public bool TryGetValue(string sectionName, string key, out string? value)
        => Read(sectionName, sectionKey => ReadUntyped(sectionKey, key), out value);

    private bool Read(string sectionName, Func<IRegistryKeyReader, (bool Found, string? Raw)> read, out string? value)
    {
        value = null;
        if (_disposed)
            return false;
        try
        {
            using var sectionKey = _registry.OpenKey(GetSectionPath(sectionName));
            if (sectionKey == null)
                return false;
            var (found, raw) = read(sectionKey);
            value = raw;
            return found;
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException or PlatformNotSupportedException)
        {
            // No access to the policies (or no registry): there are none for this application
            return false;
        }
    }

    private static (bool Found, string? Raw) ReadUntyped(IRegistryKeyReader sectionKey, string key)
    {
        if (!sectionKey.TryGetValue(key, out var value, out var kind))
            return (false, null);
        return kind switch
        {
            RegistryValueKind.String or RegistryValueKind.ExpandString => (true, value as string),
            RegistryValueKind.DWord when value is int i => (true, i.ToString(CultureInfo.InvariantCulture)),
            RegistryValueKind.QWord when value is long l => (true, l.ToString(CultureInfo.InvariantCulture)),
            RegistryValueKind.MultiString when value is string[] items => (true, string.Join(",", items.Select(s => PolicyValueMapper.Quote(s, ',')))),
            _ => throw new InvalidSourceValueException($"Policy value '{key}': {kind} values are not supported.")
        };
    }

    /// <summary>
    /// Raised (debounced, with no section or key: everything may have changed) when a policy below the root changes.
    /// The first subscriber starts the watching, the last one to leave stops it.
    /// </summary>
    public event EventHandler<ValueChangedEventArgs>? ValueChanged
    {
        add
        {
            lock (_lock)
            {
                _valueChanged += value;
                if (_valueChanged != null && _watcher == null && _watcherFactory != null && !_disposed)
                {
                    _debouncer = new ChangeDebouncer(ChangeDebounce, RaiseValueChanged);
                    _watcher = _watcherFactory();
                    _watcher.Changed += OnRegistryChanged;
                }
            }
        }
        remove
        {
            lock (_lock)
            {
                _valueChanged -= value;
                if (_valueChanged == null)
                    StopWatching();
            }
        }
    }

    /// <summary><c>true</c> while the policy keys are watched.</summary>
    internal bool IsWatching
    {
        get
        {
            lock (_lock)
                return _watcher != null;
        }
    }

    private void OnRegistryChanged(object? sender, EventArgs e) => _debouncer?.Trigger();

    private void RaiseValueChanged() => _valueChanged?.Invoke(this, new ValueChangedEventArgs());

    private void StopWatching()
    {
        if (_watcher != null)
        {
            _watcher.Changed -= OnRegistryChanged;
            _watcher.Dispose();
            _watcher = null;
        }
        _debouncer?.Dispose();
        _debouncer = null;
    }

    /// <summary>Stops the watching.</summary>
    public void Dispose()
    {
        lock (_lock)
        {
            _disposed = true;
            _valueChanged = null;
            StopWatching();
        }
    }

    /// <inheritdoc/>
    public override string ToString() => Name;
}
