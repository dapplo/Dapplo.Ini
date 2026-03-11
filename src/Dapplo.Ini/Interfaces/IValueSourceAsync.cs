// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Dapplo.Ini.Interfaces;

/// <summary>
/// Asynchronous variant of <see cref="IValueSource"/> for external configuration sources that
/// perform I/O — for example a REST API endpoint, a remote configuration service, or any
/// other inherently asynchronous store.
/// </summary>
/// <remarks>
/// Implementations are registered with
/// <see cref="Configuration.IniConfigBuilder.AddValueSource(IValueSourceAsync)"/> and are
/// consulted during <see cref="Configuration.IniConfigBuilder.BuildAsync"/> and
/// <see cref="Configuration.IniConfig.ReloadAsync"/>.
/// <para>
/// Sources are applied after defaults, the user INI file, constant files, and synchronous
/// value sources, in the order they are registered.
/// </para>
/// <para>
/// If the source only needs synchronous access (e.g. environment variables, in-memory
/// dictionaries), implement <see cref="IValueSource"/> instead.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// public class RemoteConfigSource : IValueSourceAsync
/// {
///     private readonly HttpClient _http;
///
///     public event EventHandler&lt;ValueChangedEventArgs&gt;? ValueChanged;
///
///     public RemoteConfigSource(HttpClient http) => _http = http;
///
///     public async Task&lt;(bool Found, string? Value)&gt; TryGetValueAsync(
///         string sectionName, string key, CancellationToken cancellationToken = default)
///     {
///         var response = await _http.GetAsync(
///             $"/config/{sectionName}/{key}", cancellationToken);
///         if (!response.IsSuccessStatusCode)
///             return (false, null);
///         return (true, await response.Content.ReadAsStringAsync(cancellationToken));
///     }
/// }
/// </code>
/// </example>
public interface IValueSourceAsync
{
    /// <summary>
    /// Asynchronously attempts to retrieve the raw string value for the key
    /// <paramref name="key"/> inside INI section <paramref name="sectionName"/>.
    /// </summary>
    /// <param name="sectionName">The INI section name (e.g. <c>"General"</c>).</param>
    /// <param name="key">The key name within that section (e.g. <c>"AppName"</c>).</param>
    /// <param name="cancellationToken">Token to cancel the async operation.</param>
    /// <returns>
    /// A tuple where <c>Found</c> is <c>true</c> when this source supplies a value, and
    /// <c>Value</c> contains the raw string value in that case.
    /// </returns>
    Task<(bool Found, string? Value)> TryGetValueAsync(
        string sectionName, string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Raised when one or more values provided by this source have changed.
    /// The library can use this signal to trigger a reload of affected sections.
    /// </summary>
    event EventHandler<ValueChangedEventArgs>? ValueChanged;
}
