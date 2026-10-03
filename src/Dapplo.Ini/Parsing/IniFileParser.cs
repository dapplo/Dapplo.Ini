// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;

namespace Dapplo.Ini.Parsing;

/// <summary>
/// Parses INI file content using <see cref="ReadOnlySpan{T}"/> to minimise allocations.
/// Supports:
/// <list type="bullet">
///   <item>Sections: <c>[SectionName]</c></item>
///   <item>Key-value pairs: <c>key = value</c> or <c>key=value</c></item>
///   <item>Comments: lines starting with <c>;</c> or <c>#</c></item>
///   <item>Blank lines (ignored between entries; preserved as section/key comment context)</item>
/// </list>
/// Behaviour for duplicate keys, quoted values, escape sequences, line continuation, and
/// case sensitivity can all be configured via <see cref="IniParserOptions"/>.
/// </summary>
public static class IniFileParser
{
    /// <summary>
    /// Parses the content of an INI file from <paramref name="content"/> and returns an <see cref="IniFile"/>.
    /// </summary>
    /// <param name="content">The full text of the INI file.</param>
    /// <param name="options">
    /// Parser options controlling duplicate-key handling, quoted values, escape sequences,
    /// line continuation, and case sensitivity.
    /// When <c>null</c>, <see cref="IniParserOptions.Default"/> is used.
    /// </param>
    public static IniFile Parse(string content, IniParserOptions? options = null)
    {
        if (content is null) throw new ArgumentNullException(nameof(content));
        return Parse(content.AsSpan(), options);
    }

    /// <summary>
    /// Parses INI content from a span (e.g. a pooled buffer). Only the keys, values, section names and comments
    /// that end up in the <see cref="IniFile"/> are allocated.
    /// </summary>
    internal static IniFile Parse(ReadOnlySpan<char> content, IniParserOptions? options = null)
    {
        options ??= IniParserOptions.Default;

        var sectionComparer = options.CaseSensitiveSections ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;
        var keyComparer     = options.CaseSensitiveKeys     ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;

        var iniFile = new IniFile(sectionComparer, keyComparer);
        // A byte order mark left in the string (e.g. from Encoding.GetString) is not whitespace.
        var span = content;
        if (!span.IsEmpty && span[0] == '\uFEFF')
            span = span.Slice(1);

        // Pre-pass (no allocations beyond a pooled array): the number of entries per section header, so every
        // section's dictionary and list are created with the right size instead of growing (re-allocating) while parsing.
        var sizes = CountEntriesPerSection(span, options.AssignmentDelimiters, out var headerCount);
        var headerOrdinal = 0;
        try
        {

        IniSection? currentSection = null;
        var pendingComments = new List<string>();
        // Raw blank/comment/unparseable lines, only recorded with PreserveTrivia.
        var pendingTrivia = options.PreserveTrivia ? new List<string>() : null;
        IReadOnlyList<string>? TakeTrivia()
        {
            if (pendingTrivia == null) return null;
            var trivia = pendingTrivia.ToArray();
            pendingTrivia.Clear();
            return trivia;
        }

        while (!span.IsEmpty)
        {
            // Read one line
            var lineSpan = ReadLine(ref span);

            // Trim whitespace for classification
            var trimmed = lineSpan.Trim();

            if (trimmed.IsEmpty)
            {
                // Blank line: reset pending comments (don't carry over to next key)
                pendingComments.Clear();
                pendingTrivia?.Add(string.Empty);
                continue;
            }

            var first = trimmed[0];

            if (first == ';' || first == '#')
            {
                // Comment line – strip the leading ; or # and optional space
                var commentContent = trimmed.Slice(1);
                if (!commentContent.IsEmpty && commentContent[0] == ' ')
                    commentContent = commentContent.Slice(1);
                pendingComments.Add(commentContent.ToString());
                pendingTrivia?.Add(lineSpan.TrimEnd().ToString());
                continue;
            }

            if (first == '[')
            {
                // Section header [SectionName]
                var closeBracket = trimmed.IndexOf(']');
                if (closeBracket > 0)
                {
                    var sectionName = trimmed.Slice(1, closeBracket - 1).Trim().ToString();
                    IReadOnlyList<string> comments = pendingComments.Count > 0
                        ? pendingComments.ToArray()
                        : (IReadOnlyList<string>)Array.Empty<string>();
                    // A repeated header continues the existing section instead of replacing it,
                    // so that earlier keys are not lost and duplicate-key handling still applies.
                    var existingSection = iniFile.GetSection(sectionName);
                    if (existingSection != null)
                    {
                        // The lines above the repeated header stay pending for the next entry.
                        currentSection = existingSection;
                    }
                    else
                    {
                        currentSection = new IniSection(sectionName, comments, keyComparer,
                            headerOrdinal < headerCount ? sizes[headerOrdinal + 1] : 0)
                        {
                            LeadingTrivia = TakeTrivia()
                        };
                        iniFile.AddSection(currentSection);
                    }
                    headerOrdinal++;
                }
                else
                {
                    pendingTrivia?.Add(lineSpan.TrimEnd().ToString()); // "[Unclosed": keep the line as it is
                }
                pendingComments.Clear();
                continue;
            }

            // Key=value pair (assignment delimiter is configurable, defaults to '=' and ':')
            var assignmentIndex = FindAssignmentIndex(trimmed, options.AssignmentDelimiters);
            if (assignmentIndex > 0)
            {
                var key = trimmed.Slice(0, assignmentIndex).TrimEnd().ToString();
                var rawValue = trimmed.Slice(assignmentIndex + 1).TrimStart();

                // Line continuation: if value ends with '\', join the next line(s) (the only case with an extra string)
                string? joined = null;
                if (options.LineContinuation && EndsWithContinuation(rawValue))
                {
                    joined = ApplyLineContinuation(rawValue, ref span);
                    rawValue = joined.AsSpan();
                }

                // Quotes and escape sequences are processed on the span: one string per value
                var value = CreateValue(rawValue, options, joined);

                // Ensure there is a section (global / no-section entries go into a synthetic "" section)
                currentSection ??= iniFile.GetOrAddSection(string.Empty);

                // Duplicate key handling
                if (options.DuplicateKeyHandling != DuplicateKeyHandling.LastWins
                    && currentSection.ContainsKey(key))
                {
                    switch (options.DuplicateKeyHandling)
                    {
                        case DuplicateKeyHandling.FirstWins:
                            // Skip – keep the first value
                            pendingComments.Clear();
                            continue;
                        case DuplicateKeyHandling.ThrowError:
                            throw new InvalidOperationException(
                                $"Duplicate key '{key}' found in section '{currentSection.Name}'.");
                    }
                }

                IReadOnlyList<string> entryComments = pendingComments.Count > 0
                    ? pendingComments.ToArray()
                    : (IReadOnlyList<string>)Array.Empty<string>();
                var entry = new IniEntry(key, value, entryComments) { LeadingTrivia = TakeTrivia() };
                currentSection.SetEntry(entry);
                pendingComments.Clear();
            }
            else
            {
                // Lines that don't match any pattern are ignored (but kept as trivia when requested)
                pendingTrivia?.Add(lineSpan.TrimEnd().ToString());
            }
        }

        if (pendingTrivia is { Count: > 0 })
            iniFile.TrailingTrivia = pendingTrivia.ToArray();

        return iniFile;
        }
        finally
        {
            System.Buffers.ArrayPool<int>.Shared.Return(sizes);
        }
    }

    /// <summary>
    /// Counts the lines that look like entries per section, without allocating anything but a pooled array:
    /// index 0 holds the entries before the first header, index n the entries after the n-th header. Returns
    /// the (rented) array; <paramref name="headerCount"/> is the number of headers. An over-estimate (e.g. a
    /// continuation line) only reserves a little more room.
    /// </summary>
    private static int[] CountEntriesPerSection(ReadOnlySpan<char> content, string delimiters, out int headerCount)
    {
        var sizes = System.Buffers.ArrayPool<int>.Shared.Rent(16);
        sizes[0] = 0;
        headerCount = 0;
        while (!content.IsEmpty)
        {
            var line = ReadLine(ref content).TrimStart();
            if (line.IsEmpty) continue;
            var first = line[0];
            if (first == ';' || first == '#') continue;
            if (first == '[')
            {
                if (line.IndexOf(']') <= 0) continue;   // "[Unclosed" is no section for the parser either
                headerCount++;
                if (headerCount >= sizes.Length)
                {
                    var larger = System.Buffers.ArrayPool<int>.Shared.Rent(sizes.Length * 2);
                    Array.Copy(sizes, larger, headerCount);
                    System.Buffers.ArrayPool<int>.Shared.Return(sizes);
                    sizes = larger;
                }
                sizes[headerCount] = 0;
                continue;
            }
            if (FindAssignmentIndex(line, delimiters) > 0)
                sizes[headerCount]++;
        }
        return sizes;
    }

    /// <summary>
    /// Parses an INI file from the file system using the specified <paramref name="encoding"/>
    /// (defaults to UTF-8 when <c>null</c>).
    /// The file is opened with <see cref="FileAccess.Read"/> and <see cref="FileShare.ReadWrite"/>
    /// so that it is never held open or locked for writing after parsing.
    /// </summary>
    /// <param name="filePath">Path to the INI file to parse.</param>
    /// <param name="encoding">Character encoding; defaults to UTF-8.</param>
    /// <param name="options">
    /// Parser options; when <c>null</c>, <see cref="IniParserOptions.Default"/> is used.
    /// </param>
    public static IniFile ParseFile(string filePath, Encoding? encoding = null, IniParserOptions? options = null)
    {
        // Read into a pooled buffer and parse from it: no string for the whole content.
        using var reader = PooledLineReader.OpenFile(filePath, encoding);
        return Parse(reader.ReadToEnd(), options);
    }

    /// <summary>
    /// Asynchronously parses an INI file from the file system using the specified
    /// <paramref name="encoding"/> (defaults to UTF-8 when <c>null</c>).
    /// The file is opened with <see cref="FileAccess.Read"/> and <see cref="FileShare.ReadWrite"/>
    /// so that it is never held open or locked for writing after parsing.
    /// </summary>
    /// <param name="filePath">Path to the INI file to parse.</param>
    /// <param name="encoding">Character encoding; defaults to UTF-8.</param>
    /// <param name="options">
    /// Parser options; when <c>null</c>, <see cref="IniParserOptions.Default"/> is used.
    /// </param>
    /// <param name="cancellationToken">Token to cancel the async operation.</param>
    public static async Task<IniFile> ParseFileAsync(string filePath, Encoding? encoding = null, IniParserOptions? options = null, CancellationToken cancellationToken = default)
    {
        using var reader = PooledLineReader.OpenFile(filePath, encoding, asynchronous: true);
        await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        return Parse(reader.ReadToEnd(), options);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    /// <summary>Reads one line from <paramref name="remaining"/> and advances the span past the newline.</summary>
    private static ReadOnlySpan<char> ReadLine(ref ReadOnlySpan<char> remaining)
    {
        var newLine = remaining.IndexOfAny('\r', '\n');
        if (newLine < 0)
        {
            var line = remaining;
            remaining = ReadOnlySpan<char>.Empty;
            return line;
        }

        var result = remaining.Slice(0, newLine);
        var wasCarriageReturn = remaining[newLine] == '\r';
        remaining = remaining.Slice(newLine + 1);

        // Handle \r\n (but a second \n after a \n is an empty line, not part of the line break)
        if (wasCarriageReturn && !remaining.IsEmpty && remaining[0] == '\n')
            remaining = remaining.Slice(1);

        return result;
    }

    /// <summary>
    /// Handles line continuation: if <paramref name="value"/> ends with a backslash,
    /// the backslash is replaced by the trimmed content of the next line(s) from
    /// <paramref name="remaining"/>.
    /// </summary>
    private static string ApplyLineContinuation(scoped ReadOnlySpan<char> value, ref ReadOnlySpan<char> remaining)
    {

        // Nothing to join (end of file, blank line or a section header): keep the value as written,
        // e.g. "Dir = C:\Temp\" directly followed by "[Next]".
        var lookAhead = remaining;
        var following = lookAhead.IsEmpty ? ReadOnlySpan<char>.Empty : ReadLine(ref lookAhead).Trim();
        if (following.IsEmpty || following[0] == '[')
            return value.ToString();

        // Strip the trailing backslash from the initial segment.
        var sb = new StringBuilder(value.Length + 64);
        Append(sb, value.Slice(0, value.Length - 1));
        while (!remaining.IsEmpty)
        {
            // Never swallow a section header: stop before it without consuming it.
            var peek = remaining;
            var peekedLine = ReadLine(ref peek).Trim();
            if (!peekedLine.IsEmpty && peekedLine[0] == '[')
                break;

            var nextLine = ReadLine(ref remaining).Trim();
            if (nextLine.IsEmpty)
            {
                // Empty continuation line: stop
                break;
            }

            if (EndsWithContinuation(nextLine))
            {
                // This line also continues — append without trailing backslash
                Append(sb, nextLine.Slice(0, nextLine.Length - 1));
            }
            else
            {
                Append(sb, nextLine);
                break;
            }
        }
        return sb.ToString();
    }

    /// <summary>
    /// A value continues on the next line when it ends with an odd number of backslashes:
    /// <c>C:\Temp\\</c> (an escaped backslash) does not continue, <c>abc\</c> does.
    /// </summary>
    private static bool EndsWithContinuation(ReadOnlySpan<char> value)
    {
        var backslashes = 0;
        for (var i = value.Length - 1; i >= 0 && value[i] == '\\'; i--)
            backslashes++;
        return backslashes % 2 == 1;
    }

    private static void Append(StringBuilder sb, ReadOnlySpan<char> text)
    {
#if NET
        sb.Append(text);
#else
        foreach (var c in text) sb.Append(c);
#endif
    }

    private const int MaxStackValueLength = 512;

    /// <summary>
    /// Creates the value string from the raw value: strips matching surrounding quotes (<see cref="IniParserOptions.QuotedValues"/>)
    /// and decodes escape sequences (<see cref="IniParserOptions.EscapeSequences"/>) on the span, so only the final string
    /// is allocated (or none, when <paramref name="rawString"/> already is the result).
    /// </summary>
    private static string CreateValue(ReadOnlySpan<char> raw, IniParserOptions options, string? rawString)
    {
        var value = raw;
        var quoteChar = '\0';
        if (options.QuotedValues && value.Length >= 2)
        {
            var first = value[0];
            if ((first == '"' || first == '\'') && value[value.Length - 1] == first)
            {
                quoteChar = first;
                value = value.Slice(1, value.Length - 2);
            }
        }

        var decodeEscapes = options.EscapeSequences;
        var unescapeQuotes = quoteChar != '\0' && !decodeEscapes;
        if ((!decodeEscapes && !unescapeQuotes) || value.IndexOf('\\') < 0)
            return rawString != null && value.Length == rawString.Length ? rawString : value.ToString();

        char[]? pooled = null;
        try
        {
            var buffer = value.Length <= MaxStackValueLength
                ? stackalloc char[value.Length]
                : (pooled = System.Buffers.ArrayPool<char>.Shared.Rent(value.Length));
            var length = decodeEscapes ? DecodeEscapeSequences(value, buffer) : UnescapeQuote(value, quoteChar, buffer);
            return buffer.Slice(0, length).ToString();
        }
        finally
        {
            if (pooled != null) System.Buffers.ArrayPool<char>.Shared.Return(pooled);
        }
    }

    /// <summary>
    /// Inverse of the writer's quote escaping without escape sequences. The writer turns n backslashes before
    /// a quote into 2n+1 and n backslashes at the end into 2n, so: an odd run before a quote becomes (run-1)/2
    /// backslashes and the quote, an even run at the end is halved. Anything else was not written by the
    /// writer (e.g. a hand-written <c>"C:\Temp\"</c>) and is kept as it is. Writes into <paramref name="destination"/>
    /// (at least as long as <paramref name="value"/>) and returns the length.
    /// </summary>
    private static int UnescapeQuote(ReadOnlySpan<char> value, char quoteChar, Span<char> destination)
    {
        var length = 0;
        var i = 0;
        while (i < value.Length)
        {
            if (value[i] != '\\')
            {
                destination[length++] = value[i++];
                continue;
            }

            var start = i;
            while (i < value.Length && value[i] == '\\')
                i++;
            var run = i - start;
            int keep;
            if (i < value.Length && value[i] == quoteChar && run % 2 == 1)
                keep = (run - 1) / 2;
            else if (i == value.Length && run % 2 == 0)
                keep = run / 2;
            else
                keep = run;
            destination.Slice(length, keep).Fill('\\');
            length += keep;
        }
        return length;
    }

    /// <summary>
    /// Decodes standard C-style escape sequences from <paramref name="value"/> into <paramref name="destination"/>
    /// (at least as long as <paramref name="value"/>) and returns the length.
    /// Unrecognised sequences are left unchanged (the backslash is preserved).
    /// </summary>
    private static int DecodeEscapeSequences(ReadOnlySpan<char> value, Span<char> destination)
    {
        var length = 0;
        var i  = 0;
        while (i < value.Length)
        {
            var c = value[i];
            if (c != '\\' || i + 1 >= value.Length)
            {
                destination[length++] = c;
                i++;
                continue;
            }

            var next = value[i + 1];
            switch (next)
            {
                case '\\': destination[length++] = '\\'; i += 2; break;
                case 'n':  destination[length++] = '\n'; i += 2; break;
                case 'r':  destination[length++] = '\r'; i += 2; break;
                case 't':  destination[length++] = '\t'; i += 2; break;
                case '0':  destination[length++] = '\0'; i += 2; break;
                case '"':  destination[length++] = '"';  i += 2; break;
                case '\'': destination[length++] = '\''; i += 2; break;
                case 'a':  destination[length++] = '\a'; i += 2; break;
                case 'b':  destination[length++] = '\b'; i += 2; break;
                case 'x' when i + 3 < value.Length &&
                              IsHexDigit(value[i + 2]) && IsHexDigit(value[i + 3]):
                    destination[length++] = (char)(HexValue(value[i + 2]) * 16 + HexValue(value[i + 3]));
                    i += 4;
                    break;
                default:
                    // Unknown escape: keep as-is
                    destination[length++] = '\\';
                    destination[length++] = next;
                    i += 2;
                    break;
            }
        }
        return length;
    }

    private static int HexValue(char c)
        => c <= '9' ? c - '0' : (c | 0x20) - 'a' + 10;

    private static bool IsHexDigit(char c)
        => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');

    private static int FindAssignmentIndex(ReadOnlySpan<char> line, string delimiters)
    {
        if (string.IsNullOrEmpty(delimiters))
            delimiters = "=:";

        var result = -1;
        foreach (var delimiter in delimiters)
        {
            var index = line.IndexOf(delimiter);
            if (index > 0 && (result < 0 || index < result))
                result = index;
        }
        return result;
    }
}
