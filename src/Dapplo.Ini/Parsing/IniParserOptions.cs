// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace Dapplo.Ini.Parsing;

/// <summary>
/// Controls how the <see cref="IniFileParser"/> interprets INI file content.
/// All options default to the most common/lenient behaviour so that existing code
/// continues to work unchanged.
/// </summary>
public sealed class IniParserOptions
{
    /// <summary>
    /// The default parser options: case-insensitive lookups, last-value-wins for duplicates,
    /// no escape sequences, no quoted-value stripping, no line continuation.
    /// </summary>
    public static readonly IniParserOptions Default = new IniParserOptions();

    /// <summary>
    /// Determines how duplicate keys within the same section are handled.
    /// Defaults to <see cref="DuplicateKeyHandling.LastWins"/>.
    /// </summary>
    public DuplicateKeyHandling DuplicateKeyHandling { get; set; } = DuplicateKeyHandling.LastWins;

    /// <summary>
    /// When <c>true</c>, values enclosed in double or single quotes have their surrounding
    /// quotes stripped during parsing, preserving any interior whitespace.
    /// <para>
    /// Example: <c>key = "  hello  "</c> is read as <c>  hello  </c> instead of <c>"  hello  "</c>.
    /// </para>
    /// </summary>
    public bool QuotedValues { get; set; } = false;

    /// <summary>
    /// When <c>true</c>, a backslash (<c>\</c>) at the very end of a value line causes the
    /// parser to join the trimmed content of the following line, enabling multi-line values.
    /// <para>
    /// Example:
    /// <code>
    /// key = first line \
    ///       second line
    /// </code>
    /// is read as <c>first line second line</c>.
    /// </para>
    /// </summary>
    public bool LineContinuation { get; set; } = false;

    /// <summary>
    /// When <c>true</c>, standard C-style escape sequences in values are decoded:
    /// <list type="table">
    ///   <listheader><term>Sequence</term><description>Result</description></listheader>
    ///   <item><term><c>\\</c></term><description>Literal backslash</description></item>
    ///   <item><term><c>\n</c></term><description>Newline (LF)</description></item>
    ///   <item><term><c>\r</c></term><description>Carriage return</description></item>
    ///   <item><term><c>\t</c></term><description>Horizontal tab</description></item>
    ///   <item><term><c>\0</c></term><description>Null character</description></item>
    ///   <item><term><c>\"</c></term><description>Double quote</description></item>
    ///   <item><term><c>\'</c></term><description>Single quote</description></item>
    ///   <item><term><c>\a</c></term><description>Bell / alert</description></item>
    ///   <item><term><c>\b</c></term><description>Backspace</description></item>
    ///   <item><term><c>\xHH</c></term><description>Hex character (two hex digits)</description></item>
    /// </list>
    /// Unrecognised escape sequences are left unchanged (the backslash is preserved).
    /// </summary>
    public bool EscapeSequences { get; set; } = false;

    /// <summary>
    /// When <c>true</c>, key names within a section are compared using an ordinal
    /// case-sensitive comparison.
    /// When <c>false</c> (the default), keys are case-insensitive so that
    /// <c>AppName</c> and <c>appname</c> refer to the same entry.
    /// </summary>
    public bool CaseSensitiveKeys { get; set; } = false;

    /// <summary>
    /// When <c>true</c>, section names are compared using an ordinal case-sensitive comparison.
    /// When <c>false</c> (the default), section names are case-insensitive.
    /// </summary>
    public bool CaseSensitiveSections { get; set; } = false;
}
