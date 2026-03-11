// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace Dapplo.Ini.Interfaces;

/// <summary>
/// Dispatch interface used by the framework to call the before-save hook.
/// Consumer code should prefer the generic <see cref="IBeforeSave{TSelf}"/> overload
/// which allows the implementation to live directly in the section interface via static virtuals.
/// </summary>
public interface IBeforeSave
{
    /// <summary>
    /// Called before the INI file is saved. Return <c>false</c> to abort the save operation.
    /// </summary>
    bool OnBeforeSave();
}

#if NET7_0_OR_GREATER
/// <summary>
/// Lifecycle hook called immediately before the INI file is written to disk.
/// Implement this interface on your section interface and provide the hook logic as a
/// <c>static</c> override of <see cref="OnBeforeSave"/> — no separate partial class file is needed.
/// </summary>
/// <typeparam name="TSelf">The section interface itself (CRTP / curiously-recurring template pattern).</typeparam>
/// <example>
/// <code>
/// [IniSection]
/// public interface IMySettings : IIniSection, IBeforeSave&lt;IMySettings&gt;
/// {
///     int Port { get; set; }
///
///     static bool OnBeforeSave(IMySettings self)
///     {
///         // Validate before writing to disk — return false to cancel the save.
///         return self.Port is >= 1024 and &lt;= 65535;
///     }
/// }
/// </code>
/// </example>
public interface IBeforeSave<TSelf> where TSelf : IBeforeSave<TSelf>
{
    /// <summary>
    /// Called before the INI file is saved. Override this static method in your section interface
    /// to perform pre-save validation or transformation. Return <c>false</c> to cancel the save.
    /// The source generator emits a bridge that connects this static virtual method to the
    /// <see cref="IBeforeSave"/> dispatch interface used by the framework.
    /// </summary>
    static virtual bool OnBeforeSave(TSelf self) => true;
}
#endif
