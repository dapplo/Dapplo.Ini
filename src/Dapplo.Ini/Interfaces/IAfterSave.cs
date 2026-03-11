// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace Dapplo.Ini.Interfaces;

/// <summary>
/// Dispatch interface used by the framework to call the after-save hook.
/// Consumer code should prefer the generic <see cref="IAfterSave{TSelf}"/> overload
/// which allows the implementation to live directly in the section interface via static virtuals.
/// </summary>
public interface IAfterSave
{
    /// <summary>Called after the INI file has been saved successfully.</summary>
    void OnAfterSave();
}

#if NET7_0_OR_GREATER
/// <summary>
/// Lifecycle hook called immediately after the INI file has been written to disk.
/// Implement this interface on your section interface and provide the hook logic as a
/// <c>static</c> override of <see cref="OnAfterSave"/> — no separate partial class file is needed.
/// </summary>
/// <typeparam name="TSelf">The section interface itself (CRTP / curiously-recurring template pattern).</typeparam>
/// <example>
/// <code>
/// [IniSection]
/// public interface IMySettings : IIniSection, IAfterSave&lt;IMySettings&gt;
/// {
///     string? BackupPath { get; set; }
///
///     static void OnAfterSave(IMySettings self)
///     {
///         // React to a successful save inside the interface — no partial class required.
///         Console.WriteLine($"Settings saved; backup will go to {self.BackupPath}");
///     }
/// }
/// </code>
/// </example>
public interface IAfterSave<TSelf> where TSelf : IAfterSave<TSelf>
{
    /// <summary>
    /// Called after the INI file has been saved successfully. Override this static method in
    /// your section interface to react to a successful save.
    /// The source generator emits a bridge that connects this static virtual method to the
    /// <see cref="IAfterSave"/> dispatch interface used by the framework.
    /// </summary>
    static virtual void OnAfterSave(TSelf self) { }
}
#endif
