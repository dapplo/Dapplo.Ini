// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Dapplo.IniConfig.Attributes;
using Dapplo.IniConfig.Interfaces;

namespace Dapplo.IniConfig.Tests;

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
