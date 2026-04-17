// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Runtime.Serialization;
using Dapplo.Ini.Attributes;
using Dapplo.Ini.Interfaces;
using Dapplo.Ini.Parsing;

namespace Dapplo.Ini.Tests;

// ── Sample interfaces used by all tests ───────────────────────────────────────

/// <summary>Basic section with common value types.</summary>
[IniSection("General", Description = "General application settings")]
public interface IGeneralSettings : IIniSection, INotifyPropertyChanged
{
    [IniValue(DefaultValue = "MyApp", Description = "Application name")]
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
    [IniValue(DefaultValue = "8080")]
    int Port { get; set; }

    [IniValue(DefaultValue = "localhost")]
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

    /// <summary>Dictionary mapping string keys to integer values. Stored as sub-keys in the INI file:
    /// <c>StringIntDictionary.x = 10</c>, <c>StringIntDictionary.y = 20</c>.
    /// The <see cref="Attributes.IniValueAttribute.DefaultValue"/> uses the inline format <c>key=value,...</c>.</summary>
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

// ── Standard .NET attribute support sample interfaces ────────────────────────

/// <summary>
/// Section that uses standard .NET attributes for its properties.
/// [DataMember(Name=...)] sets the key name, [DefaultValue] sets the default,
/// [IgnoreDataMember] excludes the property from INI read/write.
/// Note: [DataContract] cannot be applied to interface declarations in .NET.
/// [IniSection] is used here to give the section an explicit name; it can be
/// omitted when the default name (interface name without leading 'I') is acceptable.
/// </summary>
[IniSection("StandardSection")]
[Description("A section using standard .NET attributes")]
public interface IStandardAttributeSettings : IIniSection
{
    /// <summary>Property whose key name comes from [DataMember(Name="...")] .</summary>
    [DataMember(Name = "display_name")]
    [DefaultValue("World")]
    [Description("The display name")]
    string? DisplayName { get; set; }

    /// <summary>Property with a numeric default supplied via [DefaultValue].</summary>
    [DefaultValue(10)]
    int RetryCount { get; set; }

    /// <summary>Property excluded from the INI file via [IgnoreDataMember].</summary>
    [IgnoreDataMember]
    string? Transient { get; set; }

    /// <summary>Property where [IniValue] takes precedence over [DataMember].</summary>
    [IniValue(KeyName = "ini_key", DefaultValue = "IniWins")]
    [DataMember(Name = "data_member_key")]
    string? Precedence { get; set; }
}

/// <summary>
/// Section that uses DataAnnotations validation attributes ([Required], [Range], [MaxLength],
/// [RegularExpression]).
/// Validation errors are surfaced via INotifyDataErrorInfo without any exception being thrown.
/// </summary>
[IniSection("AnnotatedSection")]
public interface IAnnotatedSettings : IIniSection
{
    /// <summary>Required string — must not be null or empty.</summary>
    [Required(ErrorMessage = "Name is required.")]
    string? Name { get; set; }

    /// <summary>Integer value that must be between 1 and 100.</summary>
    [Range(1, 100, ErrorMessage = "Score must be between 1 and 100.")]
    int Score { get; set; }

    /// <summary>String whose length must not exceed 20 characters.</summary>
    [MaxLength(20, ErrorMessage = "Tag must not exceed 20 characters.")]
    string? Tag { get; set; }

    /// <summary>String that must match a simple alphanumeric pattern.</summary>
    [RegularExpression(@"^[a-zA-Z0-9]+$", ErrorMessage = "Code must be alphanumeric.")]
    string? Code { get; set; }
}

/// <summary>
/// Section that combines DataAnnotations attributes with IDataValidation&lt;TSelf&gt; so that
/// both the generated attribute rules and the custom consumer rules are enforced.
/// </summary>
[IniSection("CombinedValidation")]
public interface ICombinedValidationSettings : IIniSection, IDataValidation<ICombinedValidationSettings>
{
    [Required(ErrorMessage = "Host is required.")]
    string? Host { get; set; }

    [Range(1, 65535, ErrorMessage = "Port must be between 1 and 65535.")]
    [IniValue(DefaultValue = "8080")]
    int Port { get; set; }

    // Custom rule: Host must not equal "banned"
    static new IEnumerable<string> ValidateProperty(ICombinedValidationSettings self, string propertyName)
    {
        if (propertyName == nameof(Host) && string.Equals(self.Host, "banned", StringComparison.OrdinalIgnoreCase))
            yield return "Host value 'banned' is not allowed.";
    }
}

// ── RuntimeOnly sample interface ──────────────────────────────────────────────

/// <summary>
/// Section that exercises <c>[IniValue(RuntimeOnly = true)]</c> properties.
/// Runtime-only properties have a default, can be changed at runtime, but are never
/// loaded from or saved to the INI file.
/// </summary>
[IniSection("RuntimeOnly")]
public interface IRuntimeOnlySettings : IIniSection
{
    /// <summary>Regular read-write property — saved and loaded normally.</summary>
    [IniValue(DefaultValue = "saved")]
    string? Persisted { get; set; }

    /// <summary>Runtime-only — has a default, never persisted.</summary>
    [IniValue(DefaultValue = "runtime-default", RuntimeOnly = true)]
    string? Session { get; set; }

    /// <summary>Runtime-only integer with a default.</summary>
    [IniValue(DefaultValue = "99", RuntimeOnly = true)]
    int SessionCount { get; set; }
}

// ── Attribute-free sample interface (no [IniSection]) ────────────────────────

/// <summary>
/// Section declared without <c>[IniSection]</c> — the source generator detects it
/// because it extends <see cref="IIniSection"/>.
/// The section name defaults to "NoAttributeSettings" (leading 'I' stripped from
/// the interface name), and the description comes from <c>[Description]</c>.
/// </summary>
[Description("A section defined without [IniSection]")]
public interface INoAttributeSettings : IIniSection
{
    /// <summary>String with a default value supplied via [DefaultValue].</summary>
    [DefaultValue("no-attr-default")]
    string? Value { get; set; }

    /// <summary>Integer with a default value.</summary>
    [DefaultValue(42)]
    int Count { get; set; }
}

/// <summary>
/// Section used to test constants-file protection.
/// </summary>
[IniSection("ConstantsTest")]
public interface IConstantsSettings : IIniSection
{
    [IniValue(DefaultValue = "user-default")]
    string? UserValue { get; set; }

    [IniValue(DefaultValue = "admin-default")]
    string? AdminValue { get; set; }
}

// ── EmptyWhenNull sample interface ────────────────────────────────────────────

/// <summary>
/// Section that exercises <c>[IniValue(EmptyWhenNull = true)]</c>.
/// When a property's key is absent from the INI file (or has a null raw value),
/// the property receives an "empty" representation instead of <c>null</c>:
/// <list type="bullet">
///   <item><c>string</c> → <see cref="string.Empty"/></item>
///   <item><c>List&lt;T&gt;</c> / <c>IList&lt;T&gt;</c> → empty list</item>
///   <item><c>T[]</c> → empty array</item>
/// </list>
/// </summary>
[IniSection("EmptyWhenNull")]
public interface IEmptyWhenNullSettings : IIniSection
{
    /// <summary>String that returns string.Empty rather than null when not set.</summary>
    [IniValue(EmptyWhenNull = true)]
    string? Description { get; set; }

    /// <summary>String with both a default value and EmptyWhenNull (default takes precedence for reset).</summary>
    [IniValue(DefaultValue = "hello", EmptyWhenNull = true)]
    string? Greeting { get; set; }

    /// <summary>List that returns an empty list rather than null when not set.</summary>
    [IniValue(EmptyWhenNull = true)]
    List<string>? Tags { get; set; }

    /// <summary>IList that returns an empty list rather than null when not set.</summary>
    [IniValue(EmptyWhenNull = true)]
    IList<int>? Numbers { get; set; }

    /// <summary>Array that returns an empty array rather than null when not set.</summary>
    [IniValue(EmptyWhenNull = true)]
    string[]? Codes { get; set; }

    /// <summary>Regular nullable string property (no EmptyWhenNull) — baseline comparison.</summary>
    string? NullableString { get; set; }
}

// ── Section-level EmptyWhenNull sample interface ──────────────────────────────

/// <summary>
/// Section that exercises <c>[IniSection(EmptyWhenNull = true)]</c>.
/// The section-level attribute propagates to every non-value-type property, equivalent
/// to putting <c>[IniValue(EmptyWhenNull = true)]</c> on each one.
/// </summary>
[IniSection("SectionEmptyWhenNull", EmptyWhenNull = true)]
public interface ISectionEmptyWhenNullSettings : IIniSection
{
    /// <summary>String — gets string.Empty instead of null when not set.</summary>
    string? Label { get; set; }

    /// <summary>List — gets empty list instead of null when not set.</summary>
    List<string>? Items { get; set; }

    /// <summary>String with an explicit DefaultValue — the default wins over EmptyWhenNull for ResetToDefaults.</summary>
    [IniValue(DefaultValue = "hello")]
    string? WithDefault { get; set; }

    /// <summary>Value-type property — section-level EmptyWhenNull does NOT affect value types.</summary>
    int Counter { get; set; }
}

// ── IgnoreDefaults / IgnoreConstants section-level sample interfaces ───────────

/// <summary>
/// Section with <c>[IniSection(IgnoreDefaults = true)]</c>:
/// values in this section are never loaded from defaults files.
/// </summary>
[IniSection("IgnoreDefaultsSection", IgnoreDefaults = true)]
public interface IIgnoreDefaultsSectionSettings : IIniSection
{
    [IniValue(DefaultValue = "compiled-default")]
    string? Value { get; set; }
}

/// <summary>
/// Section with <c>[IniSection(IgnoreConstants = true)]</c>:
/// values in this section are never loaded from constants files
/// and are therefore never locked.
/// </summary>
[IniSection("IgnoreConstantsSection", IgnoreConstants = true)]
public interface IIgnoreConstantsSectionSettings : IIniSection
{
    [IniValue(DefaultValue = "compiled-default")]
    string? Value { get; set; }
}

/// <summary>
/// Section with mixed property-level <c>IgnoreDefaults</c> / <c>IgnoreConstants</c> flags.
/// </summary>
[IniSection("MixedIgnoreSection")]
public interface IMixedIgnoreSettings : IIniSection
{
    /// <summary>This property IS loaded from defaults files normally.</summary>
    [IniValue(DefaultValue = "compiled-a")]
    string? ValueA { get; set; }

    /// <summary>This property is NEVER loaded from defaults files.</summary>
    [IniValue(DefaultValue = "compiled-b", IgnoreDefaults = true)]
    string? ValueB { get; set; }

    /// <summary>This property is NEVER loaded from constants files (and therefore never locked).</summary>
    [IniValue(DefaultValue = "compiled-c", IgnoreConstants = true)]
    string? ValueC { get; set; }
}

// ── Writer behavior sample interface ──────────────────────────────────────────

[IniSection("WriteBehaviorSection", QuoteValues = IniValueQuoteStyle.Double, WriteComments = IniBooleanOption.Disabled)]
public interface IWriteBehaviorSettings : IIniSection
{
    [IniValue(DefaultValue = "plain", Description = "name description")]
    string? Name { get; set; }

    [IniValue(DefaultValue = "C:\\Temp\\App", EscapeSequences = IniBooleanOption.Enabled)]
    string? Path { get; set; }

    [IniValue(DefaultValue = "spaced", QuoteValues = IniValueQuoteStyle.Single)]
    string? SingleQuoted { get; set; }
}
