// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Threading.Tasks;

namespace Dapplo.Ini.Interfaces;

/// <summary>
/// Async dispatch interface used by the framework to call the after-save hook.
/// Consumer code should prefer the generic <see cref="IAfterSaveAsync{TSelf}"/> overload
/// which allows the implementation to live directly in the section interface via static virtuals.
/// </summary>
public interface IAfterSaveAsync
{
    /// <summary>Called asynchronously after the INI file has been saved successfully.</summary>
    Task OnAfterSaveAsync(CancellationToken cancellationToken = default);
}

#if NET7_0_OR_GREATER
/// <summary>
/// Async lifecycle hook called immediately after the INI file has been written to disk.
/// Implement this interface on your section interface and provide the hook logic as a
/// <c>static</c> override of <see cref="OnAfterSaveAsync"/> — no separate partial class file is needed.
/// </summary>
/// <typeparam name="TSelf">The section interface itself (CRTP / curiously-recurring template pattern).</typeparam>
/// <example>
/// <code>
/// [IniSection]
/// public interface IMySettings : IIniSection, IAfterSaveAsync&lt;IMySettings&gt;
/// {
///     string? BackupPath { get; set; }
///
///     static ValueTask OnAfterSaveAsync(IMySettings self, CancellationToken cancellationToken = default)
///     {
///         // React asynchronously to a successful save inside the interface — no partial class required.
///         Console.WriteLine($"Settings saved; backup will go to {self.BackupPath}");
///         return ValueTask.CompletedTask;
///     }
/// }
/// </code>
/// </example>
public interface IAfterSaveAsync<TSelf> where TSelf : IAfterSaveAsync<TSelf>
{
    /// <summary>
    /// Called asynchronously after the INI file has been saved successfully. Override this static method in
    /// your section interface to react to a successful save.
    /// The source generator emits a bridge that connects this static virtual method to the
    /// <see cref="IAfterSaveAsync"/> dispatch interface used by the framework.
    /// </summary>
    static virtual ValueTask OnAfterSaveAsync(TSelf self, CancellationToken cancellationToken = default)
        => ValueTask.CompletedTask;
}
#endif
