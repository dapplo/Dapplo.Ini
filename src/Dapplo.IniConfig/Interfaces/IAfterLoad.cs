// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace Dapplo.IniConfig.Interfaces;

/// <summary>
/// Dispatch interface used by the framework to call the after-load hook.
/// Consumer code should prefer the generic <see cref="IAfterLoad{TSelf}"/> overload
/// which allows the implementation to live directly in the section interface via static virtuals.
/// </summary>
public interface IAfterLoad
{
    /// <summary>Called after the section's values have been populated from the INI file.</summary>
    void OnAfterLoad();
}

#if NET7_0_OR_GREATER
/// <summary>
/// Lifecycle hook called immediately after the section's values have been loaded from the INI file.
/// Implement this interface on your section interface and provide the hook logic as a
/// <c>static</c> override of <see cref="OnAfterLoad"/> — no separate partial class file is needed.
/// </summary>
/// <typeparam name="TSelf">The section interface itself (CRTP / curiously-recurring template pattern).</typeparam>
/// <example>
/// <code>
/// [IniSection]
/// public interface IMySettings : IIniSection, IAfterLoad&lt;IMySettings&gt;
/// {
///     string? ConnectionString { get; set; }
///
///     static void OnAfterLoad(IMySettings self)
///     {
///         // Normalise loaded value inside the interface — no partial class required.
///         self.ConnectionString ??= "Server=localhost";
///     }
/// }
/// </code>
/// </example>
public interface IAfterLoad<TSelf> where TSelf : IAfterLoad<TSelf>
{
    /// <summary>
    /// Called after the section's values have been loaded. Override this static method in your
    /// section interface to provide custom post-load logic.
    /// The source generator emits a bridge that connects this static virtual method to the
    /// <see cref="IAfterLoad"/> dispatch interface used by the framework.
    /// </summary>
    static virtual void OnAfterLoad(TSelf self) { }
}
#endif
