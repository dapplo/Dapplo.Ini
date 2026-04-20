// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace Dapplo.Ini.Parsing;

/// <summary>
/// Optional writer-option overrides that can be attached to a section or key.
/// </summary>
public sealed class IniWriterOptionsOverride
{
    /// <summary>
    /// Optional assignment separator override (e.g. <c>"="</c>, <c>" = "</c>, <c>":"</c>).
    /// </summary>
    public string? AssignmentSeparator { get; set; }

    /// <summary>
    /// Optional quoting style override.
    /// </summary>
    public IniValueQuoteStyle QuoteStyle { get; set; } = IniValueQuoteStyle.Default;

    /// <summary>
    /// Optional escape-sequence writing override.
    /// </summary>
    public IniBooleanOption EscapeSequences { get; set; } = IniBooleanOption.Default;

    /// <summary>
    /// Optional comments-writing override.
    /// </summary>
    public IniBooleanOption WriteComments { get; set; } = IniBooleanOption.Default;

    /// <summary>
    /// Returns true when this override changes at least one writer setting.
    /// </summary>
    public bool HasOverrides =>
        AssignmentSeparator != null
        || QuoteStyle != IniValueQuoteStyle.Default
        || EscapeSequences != IniBooleanOption.Default
        || WriteComments != IniBooleanOption.Default;
}
