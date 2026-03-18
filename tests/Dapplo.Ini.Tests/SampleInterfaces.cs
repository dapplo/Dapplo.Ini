// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Dapplo.Ini.Attributes;
using Dapplo.Ini.Interfaces;

namespace Dapplo.Ini.Tests;

// ── Sample interfaces used by all tests ───────────────────────────────────────

/// <summary>Basic section with common value types.</summary>
[IniSection("General", Description = "General application settings")]
public interface IGeneralSettings : IIniSection
{
    [IniValue(DefaultValue = "MyApp", Description = "Application name", NotifyPropertyChanged = true)]
    string? AppName { get; set; }

    [IniValue(DefaultValue = "42")]
    int MaxRetries { get; set; }

    [IniValue(DefaultValue = "True")]
    bool EnableLogging { get; set; }

    [IniValue(DefaultValue = "3.14")]
    double Threshold { get; set; }
}

/// <summary>Section with transactional properties.</summary>
[IniSection]
public interface IUserSettings : IIniSection, ITransactional
{
    [IniValue(DefaultValue = "admin", Transactional = true)]
    string? Username { get; set; }

    [IniValue(DefaultValue = "password", Transactional = true)]
    string? Password { get; set; }

    [IniValue(DefaultValue = "0")]
    int LoginCount { get; set; }
}

/// <summary>
/// Section that hooks into save/load lifecycle using the old non-generic pattern.
/// The consumer implements the hook methods in a separate partial class file.
/// This pattern is kept for backward compatibility.
/// </summary>
[IniSection("LegacyLifecycle")]
public interface ILegacyLifecycleSettings : IIniSection, IAfterLoad, IBeforeSave, IAfterSave
{
    string? Value { get; set; }
}

/// <summary>
/// Section that cancels saves via <see cref="IBeforeSave{TSelf}.OnBeforeSave"/>.
/// Used to test that returning false from the generic hook aborts the save.
/// </summary>
[IniSection("CancelSave")]
public interface ICancelSaveSettings : IIniSection, IBeforeSave<ICancelSaveSettings>
{
    string? Value { get; set; }

    // Always cancel the save
    static new bool OnBeforeSave(ICancelSaveSettings self) => false;
}
[IniSection("LifecycleSettings")]
public interface ILifecycleSettings
    : IIniSection,
      IAfterLoad<ILifecycleSettings>,
      IBeforeSave<ILifecycleSettings>,
      IAfterSave<ILifecycleSettings>
{
    string? Value { get; set; }

    /// <summary>Tracks whether <see cref="OnAfterLoad"/> was invoked (used in tests).</summary>
    bool AfterLoadCalled { get; set; }

    /// <summary>Tracks whether <see cref="OnBeforeSave"/> was invoked (used in tests).</summary>
    bool BeforeSaveCalled { get; set; }

    /// <summary>Tracks whether <see cref="OnAfterSave"/> was invoked (used in tests).</summary>
    bool AfterSaveCalled { get; set; }

    // ── Static-virtual lifecycle hook implementations ─────────────────────────
    // These override the no-op defaults from IAfterLoad<TSelf>, IBeforeSave<TSelf>
    // and IAfterSave<TSelf>. The source generator emits a bridge so the framework
    // can call them through the non-generic dispatch interfaces.

    static new void OnAfterLoad(ILifecycleSettings self) => self.AfterLoadCalled = true;

    static new bool OnBeforeSave(ILifecycleSettings self) { self.BeforeSaveCalled = true; return true; }

    static new void OnAfterSave(ILifecycleSettings self) => self.AfterSaveCalled = true;
}

// ── Validation sample interfaces ──────────────────────────────────────────────

/// <summary>
/// Section that uses <see cref="IDataValidation{TSelf}"/> to validate properties via
/// <c>INotifyDataErrorInfo</c> (WPF/Avalonia binding support).
/// </summary>
[IniSection("ServerConfig")]
public interface IServerConfigSettings : IIniSection, IDataValidation<IServerConfigSettings>
{
    [IniValue(DefaultValue = "8080", NotifyPropertyChanged = true)]
    int Port { get; set; }

    [IniValue(DefaultValue = "localhost", NotifyPropertyChanged = true)]
    string? Host { get; set; }

    // Validation: Port must be in 1-65535; Host must not be empty.
    static new IEnumerable<string> ValidateProperty(IServerConfigSettings self, string propertyName)
    {
        return propertyName switch
        {
            nameof(Port) when self.Port is < 1 or > 65535
                => new[] { "Port must be between 1 and 65535." },
            nameof(Host) when string.IsNullOrWhiteSpace(self.Host)
                => new[] { "Host must not be empty." },
            _ => Array.Empty<string>()
        };
    }
}

// ── Reload / monitoring / external-sources sample interfaces ──────────────────

/// <summary>Section used by reload and monitoring tests.</summary>
[IniSection("ReloadSection")]
public interface IReloadSettings : IIniSection
{
    [IniValue(DefaultValue = "initial")]
    string? Value { get; set; }
}

/// <summary>Simple external value source backed by an in-memory dictionary.</summary>
public sealed class DictionaryValueSource : IValueSource
{
    private readonly Dictionary<string, Dictionary<string, string?>> _data =
        new(StringComparer.OrdinalIgnoreCase);

    public event EventHandler<ValueChangedEventArgs>? ValueChanged;

    public void SetValue(string section, string key, string? value)
    {
        if (!_data.TryGetValue(section, out var sectionDict))
        {
            sectionDict = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            _data[section] = sectionDict;
        }
        sectionDict[key] = value;
    }

    public bool TryGetValue(string sectionName, string key, out string? value)
    {
        value = null;
        return _data.TryGetValue(sectionName, out var sect) && sect.TryGetValue(key, out value);
    }

    public void RaiseChanged(string? section = null, string? key = null)
        => ValueChanged?.Invoke(this, new ValueChangedEventArgs(section, key));
}

// ── Async lifecycle sample interfaces ─────────────────────────────────────────

/// <summary>
/// Section that hooks into load/save lifecycle using async hooks (non-generic pattern).
/// The consumer implements the async hook methods in a separate partial class file.
/// </summary>
[IniSection("AsyncLifecycle")]
public interface IAsyncLifecycleSettings : IIniSection, IAfterLoadAsync, IBeforeSaveAsync, IAfterSaveAsync
{
    string? Value { get; set; }
}

/// <summary>
/// Section that cancels saves asynchronously via <see cref="IBeforeSaveAsync"/>.
/// </summary>
[IniSection("AsyncCancelSave")]
public interface IAsyncCancelSaveSettings : IIniSection, IBeforeSaveAsync
{
    string? Value { get; set; }
}

// ── Read-only (getter-only) sample interface ──────────────────────────────────

/// <summary>
/// Section that demonstrates getter-only interface properties.
/// Properties declared with only a getter (<c>{ get; }</c>) are treated as read-only:
/// they are loaded from the INI file and have their defaults applied, but are never
/// written back to disk.  The generated implementation class still provides a public
/// setter so the framework and code that references the concrete class can assign values.
/// </summary>
[IniSection("ReadOnly")]
public interface IReadOnlySettings : IIniSection
{
    /// <summary>Getter-only — loaded from INI, never written back.</summary>
    [IniValue(DefaultValue = "1.0.0")]
    string? Version { get; }

    /// <summary>Regular read-write property included to verify mixing works.</summary>
    [IniValue(DefaultValue = "App")]
    string? Name { get; set; }
}

/// <summary>Simple async external value source backed by an in-memory dictionary.</summary>
public sealed class AsyncDictionaryValueSource : IValueSourceAsync
{
    private readonly Dictionary<string, Dictionary<string, string?>> _data =
        new(StringComparer.OrdinalIgnoreCase);

    public event EventHandler<ValueChangedEventArgs>? ValueChanged;

    public void SetValue(string section, string key, string? value)
    {
        if (!_data.TryGetValue(section, out var sectionDict))
        {
            sectionDict = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            _data[section] = sectionDict;
        }
        sectionDict[key] = value;
    }

    public Task<(bool Found, string? Value)> TryGetValueAsync(
        string sectionName, string key, CancellationToken cancellationToken = default)
    {
        if (_data.TryGetValue(sectionName, out var sect) && sect.TryGetValue(key, out var value))
            return Task.FromResult((true, value));
        return Task.FromResult<(bool, string?)>((false, null));
    }

    public void RaiseChanged(string? section = null, string? key = null)
        => ValueChanged?.Invoke(this, new ValueChangedEventArgs(section, key));
}


// ── Collection (list, array, dictionary) sample interfaces ───────────────────

/// <summary>
/// Section that exercises list, array, and dictionary property types.
/// Verifies that comma-separated values in INI files round-trip correctly.
/// </summary>
[IniSection("Collections")]
public interface ICollectionSettings : IIniSection
{
    /// <summary>Comma-separated list of strings, e.g. <c>Feature1,Feature2,Feature3</c>.</summary>
    [IniValue(DefaultValue = "A,B,C")]
    List<string>? StringList { get; set; }

    /// <summary>Comma-separated list of integers, e.g. <c>1,2,3</c>.</summary>
    [IniValue(DefaultValue = "1,2,3")]
    List<int>? IntList { get; set; }

    /// <summary>Property typed as <see cref="IList{T}"/> to verify interface-typed properties work.</summary>
    [IniValue(DefaultValue = "X,Y,Z")]
    IList<string>? StringIList { get; set; }

    /// <summary>Array of strings, e.g. <c>red,green,blue</c>.</summary>
    [IniValue(DefaultValue = "red,green,blue")]
    string[]? StringArray { get; set; }

    /// <summary>Dictionary mapping string keys to integer values, e.g. <c>a=1,b=2</c>.</summary>
    [IniValue(DefaultValue = "x=10,y=20")]
    Dictionary<string, int>? StringIntDictionary { get; set; }
}


/// <summary>
/// Section that uses the generic IUnknownKey&lt;TSelf&gt; pattern to handle a renamed key.
/// "OldName" was renamed to "DisplayName" — the migration hook copies the value across.
/// </summary>
[IniSection("Migration")]
public interface IMigrationSettings : IIniSection, IAfterLoad<IMigrationSettings>, IUnknownKey<IMigrationSettings>
{
    [IniValue(DefaultValue = "Default")]
    string? DisplayName { get; set; }

    [IniValue(DefaultValue = "100")]
    int MaxCount { get; set; }

    /// <summary>Tracks whether the AfterLoad hook ran (used in tests).</summary>
    bool AfterLoadCalled { get; set; }

    /// <summary>Tracks whether OnUnknownKey was invoked (used in tests).</summary>
    bool UnknownKeyCalled { get; set; }

    /// <summary>Stores the key that was passed to OnUnknownKey (used in tests).</summary>
    string? LastUnknownKey { get; set; }

    static new void OnAfterLoad(IMigrationSettings self) => self.AfterLoadCalled = true;

    static new void OnUnknownKey(IMigrationSettings self, string key, string? value)
    {
        self.UnknownKeyCalled = true;
        self.LastUnknownKey = key;

        // Rename migration: "OldName" → DisplayName
        if (key.Equals("OldName", StringComparison.OrdinalIgnoreCase))
            self.DisplayName = value;
    }
}

/// <summary>
/// Section that uses the non-generic IUnknownKey pattern via a partial class.
/// </summary>
[IniSection("LegacyMigration")]
public interface ILegacyMigrationSettings : IIniSection, IUnknownKey
{
    [IniValue(DefaultValue = "0")]
    int Value { get; set; }
}
