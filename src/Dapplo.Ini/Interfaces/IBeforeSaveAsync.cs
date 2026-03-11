// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Threading.Tasks;

namespace Dapplo.Ini.Interfaces;

/// <summary>
/// Async dispatch interface used by the framework to call the before-save hook.
/// Consumer code should prefer the generic <see cref="IBeforeSaveAsync{TSelf}"/> overload
/// which allows the implementation to live directly in the section interface via static virtuals.
/// </summary>
public interface IBeforeSaveAsync
{
    /// <summary>
    /// Called asynchronously before the INI file is saved.
    /// Return <c>false</c> to abort the save operation.
    /// </summary>
    Task<bool> OnBeforeSaveAsync(CancellationToken cancellationToken = default);
}

#if NET7_0_OR_GREATER
/// <summary>
/// Async lifecycle hook called immediately before the INI file is written to disk.
/// Implement this interface on your section interface and provide the hook logic as a
/// <c>static</c> override of <see cref="OnBeforeSaveAsync"/> — no separate partial class file is needed.
/// </summary>
/// <typeparam name="TSelf">The section interface itself (CRTP / curiously-recurring template pattern).</typeparam>
/// <example>
/// <code>
/// [IniSection]
/// public interface IMySettings : IIniSection, IBeforeSaveAsync&lt;IMySettings&gt;
/// {
///     int Port { get; set; }
///
///     static async ValueTask&lt;bool&gt; OnBeforeSaveAsync(IMySettings self, CancellationToken cancellationToken = default)
///     {
///         // Asynchronously validate before writing to disk — return false to cancel the save.
///         return self.Port is >= 1024 and &lt;= 65535;
///     }
/// }
/// </code>
/// </example>
public interface IBeforeSaveAsync<TSelf> where TSelf : IBeforeSaveAsync<TSelf>
{
    /// <summary>
    /// Called asynchronously before the INI file is saved. Override this static method in your section interface
    /// to perform pre-save validation or transformation. Return <c>false</c> to cancel the save.
    /// The source generator emits a bridge that connects this static virtual method to the
    /// <see cref="IBeforeSaveAsync"/> dispatch interface used by the framework.
    /// </summary>
    static virtual ValueTask<bool> OnBeforeSaveAsync(TSelf self, CancellationToken cancellationToken = default)
        => new ValueTask<bool>(true);
}
#endif
