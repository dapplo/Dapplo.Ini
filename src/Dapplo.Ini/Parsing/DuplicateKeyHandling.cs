// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace Dapplo.Ini.Parsing;

/// <summary>
/// Specifies how the <see cref="IniFileParser"/> handles duplicate keys within the same section.
/// </summary>
public enum DuplicateKeyHandling
{
    /// <summary>
    /// When a key is defined multiple times, the last definition wins.
    /// This is the default and most common behaviour.
    /// </summary>
    LastWins,

    /// <summary>
    /// When a key is defined multiple times, only the first definition is kept;
    /// subsequent definitions are silently ignored.
    /// </summary>
    FirstWins,

    /// <summary>
    /// When a key is defined more than once in the same section,
    /// an <see cref="InvalidOperationException"/> is thrown.
    /// </summary>
    ThrowError,
}
