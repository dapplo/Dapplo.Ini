// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace Dapplo.Ini.Parsing;

/// <summary>
/// Represents a single key=value pair inside an INI file, together with optional
/// comment lines that immediately precede it.
/// </summary>
public sealed class IniEntry
{
    /// <summary>The key (left-hand side of '=').</summary>
    public string Key { get; }

    /// <summary>The raw value string (right-hand side of '='), or <c>null</c> when absent.</summary>
    public string? Value { get; set; }

    /// <summary>Comment lines (without the leading ';' or '#') that appeared above this entry.</summary>
    public IReadOnlyList<string> Comments { get; }

    /// <summary>
    /// Raw lines (blank lines and comments, exactly as written) that preceded this entry, or <c>null</c>
    /// when they were not recorded. When set, the writer writes these lines instead of <see cref="Comments"/>.
    /// See <see cref="IniParserOptions.PreserveTrivia"/>.
    /// </summary>
    public IReadOnlyList<string>? LeadingTrivia { get; set; }

    /// <summary>
    /// Optional writer overrides for this specific key.
    /// </summary>
    public IniWriterOptionsOverride? WriterOptionsOverride { get; set; }

    public IniEntry(string key, string? value, IReadOnlyList<string> comments)
    {
        Key = key;
        Value = value;
        Comments = comments;
    }
}
