// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace Dapplo.Ini.Interfaces;

/// <summary>
/// A callback invoked for every key in the INI file that has no matching property on the
/// registered section interface.  Register via <see cref="IniConfigBuilder.OnUnknownKey"/>.
/// See the Migration wiki page for usage examples.
/// </summary>
/// <param name="sectionName">INI section name containing the unknown key.</param>
/// <param name="key">Key found in the file that has no matching property.</param>
/// <param name="value">Raw string value associated with the unknown key.</param>
public delegate void UnknownKeyCallback(string sectionName, string key, string? value);

/// <summary>
/// Dispatch interface used by the framework to pass unknown keys to a section instance.
/// Prefer <see cref="IUnknownKey{TSelf}"/> on .NET 7+ (static virtual, no partial class).
/// See the Migration wiki page for usage examples.
/// </summary>
public interface IUnknownKey
{
    /// <summary>Called when a key in the INI file has no matching property on this section.</summary>
    /// <param name="key">The unrecognised key name.</param>
    /// <param name="value">The raw string value for that key.</param>
    void OnUnknownKey(string key, string? value);
}

#if NET7_0_OR_GREATER
/// <summary>
/// Lifecycle interface invoked for every key that has no matching property on the section
/// interface.  Implement on the interface using a <c>static</c> override of
/// <see cref="OnUnknownKey"/> — no separate partial class required.
/// See the Migration wiki page for full examples.
/// </summary>
/// <typeparam name="TSelf">The section interface itself (CRTP pattern).</typeparam>
public interface IUnknownKey<TSelf> where TSelf : IUnknownKey<TSelf>
{
    /// <summary>
    /// Called when a key in the INI file has no matching property on this section.
    /// Override this static method in the section interface to handle renamed or obsolete keys.
    /// </summary>
    /// <param name="self">The section instance being populated.</param>
    /// <param name="key">The unrecognised key name.</param>
    /// <param name="value">The raw string value for that key.</param>
    static virtual void OnUnknownKey(TSelf self, string key, string? value) { }
}
#endif
