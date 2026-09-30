// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Dapplo.Ini.Parsing;

namespace Dapplo.Ini.Configuration;

/// <summary>
/// The parsed content of every file layer that feeds an <see cref="IniConfig"/>: defaults files,
/// the user file and constants files, as read by one load or reload.
/// </summary>
/// <remarks>
/// A snapshot is never modified after it has been created, so it can be shared between threads
/// and re-applied to sections that are registered later without reading the files again.
/// </remarks>
internal sealed class LayerSnapshot
{
    public LayerSnapshot(IReadOnlyList<IniFile> defaults, string? userFilePath, IniFile? user, IReadOnlyList<IniFile> constants)
    {
        Defaults = defaults;
        UserFilePath = userFilePath;
        User = user;
        Constants = constants;
    }

    /// <summary>Parsed defaults files, in registration order.</summary>
    public IReadOnlyList<IniFile> Defaults { get; }

    /// <summary>The path the user file was read from, or <c>null</c> when it did not exist.</summary>
    public string? UserFilePath { get; }

    /// <summary>The parsed user file, or <c>null</c> when it did not exist.</summary>
    public IniFile? User { get; }

    /// <summary>Parsed constants files, in registration order.</summary>
    public IReadOnlyList<IniFile> Constants { get; }
}
