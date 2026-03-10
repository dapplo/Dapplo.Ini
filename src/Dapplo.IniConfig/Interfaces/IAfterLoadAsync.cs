// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Threading.Tasks;

namespace Dapplo.IniConfig.Interfaces;

/// <summary>
/// Async dispatch interface used by the framework to call the after-load hook.
/// Consumer code should prefer the generic <see cref="IAfterLoadAsync{TSelf}"/> overload
/// which allows the implementation to live directly in the section interface via static virtuals.
/// </summary>
public interface IAfterLoadAsync
{
    /// <summary>Called asynchronously after the section's values have been populated from the INI file.</summary>
    Task OnAfterLoadAsync(CancellationToken cancellationToken = default);
}

#if NET7_0_OR_GREATER
/// <summary>
/// Async lifecycle hook called immediately after the section's values have been loaded from the INI file.
/// Implement this interface on your section interface and provide the hook logic as a
/// <c>static</c> override of <see cref="OnAfterLoadAsync"/> — no separate partial class file is needed.
/// </summary>
/// <typeparam name="TSelf">The section interface itself (CRTP / curiously-recurring template pattern).</typeparam>
/// <example>
/// <code>
/// [IniSection]
/// public interface IMySettings : IIniSection, IAfterLoadAsync&lt;IMySettings&gt;
/// {
///     string? ConnectionString { get; set; }
///
///     static ValueTask OnAfterLoadAsync(IMySettings self, CancellationToken cancellationToken = default)
///     {
///         // Asynchronously normalise loaded value inside the interface — no partial class required.
///         self.ConnectionString ??= "Server=localhost";
///         return ValueTask.CompletedTask;
///     }
/// }
/// </code>
/// </example>
public interface IAfterLoadAsync<TSelf> where TSelf : IAfterLoadAsync<TSelf>
{
    /// <summary>
    /// Called asynchronously after the section's values have been loaded. Override this static method in your
    /// section interface to provide custom post-load logic.
    /// The source generator emits a bridge that connects this static virtual method to the
    /// <see cref="IAfterLoadAsync"/> dispatch interface used by the framework.
    /// </summary>
    static virtual ValueTask OnAfterLoadAsync(TSelf self, CancellationToken cancellationToken = default)
        => ValueTask.CompletedTask;
}
#endif
