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
        options ??= IniParserOptions.Default;

        var sectionComparer = options.CaseSensitiveSections ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;
        var keyComparer     = options.CaseSensitiveKeys     ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;

        var iniFile = new IniFile(sectionComparer, keyComparer);
        // A byte order mark left in the string (e.g. from Encoding.GetString) is not whitespace.
        var span = content.AsSpan();
        if (!span.IsEmpty && span[0] == '\uFEFF')
            span = span.Slice(1);

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
                        currentSection = new IniSection(sectionName, comments, keyComparer)
                        {
                            LeadingTrivia = TakeTrivia()
                        };
                        iniFile.AddSection(currentSection);
                    }
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
                var key   = trimmed.Slice(0, assignmentIndex).TrimEnd().ToString();
                var value = trimmed.Slice(assignmentIndex + 1).TrimStart().ToString();

                // Line continuation: if value ends with '\', join the next line(s)
                if (options.LineContinuation)
                    value = ApplyLineContinuation(value, ref span);

                // Quoted values: strip surrounding quotes if present
                if (options.QuotedValues)
                    value = StripQuotes(value, unescapeQuotes: !options.EscapeSequences);

                // Escape sequences: decode \n, \t, \\, etc.
                if (options.EscapeSequences)
                    value = DecodeEscapeSequences(value);

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
        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream, encoding ?? Encoding.UTF8);
        var content = reader.ReadToEnd();
        return Parse(content, options);
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
        string content;
#if NET
        // FileOptions.Asynchronous enables true async I/O on platforms that support it.
        var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite,
            bufferSize: 4096, FileOptions.Asynchronous);
        await using (fileStream.ConfigureAwait(false))
        {
            using var reader = new StreamReader(fileStream, encoding ?? Encoding.UTF8);
            content = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        }
#else
        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream, encoding ?? Encoding.UTF8);
        content = await reader.ReadToEndAsync().ConfigureAwait(false);
#endif
        return Parse(content, options);
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
    private static string ApplyLineContinuation(string value, ref ReadOnlySpan<char> remaining)
    {
        if (!EndsWithContinuation(value.AsSpan()))
            return value;

        // Nothing to join (end of file, blank line or a section header): keep the value as written,
        // e.g. "Dir = C:\Temp\" directly followed by "[Next]".
        var lookAhead = remaining;
        var following = lookAhead.IsEmpty ? ReadOnlySpan<char>.Empty : ReadLine(ref lookAhead).Trim();
        if (following.IsEmpty || following[0] == '[')
            return value;

        // Strip the trailing backslash from the initial segment.
        var sb = new StringBuilder(value, 0, value.Length - 1, value.Length + 64);
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
                sb.Append(nextLine.Slice(0, nextLine.Length - 1).ToString());
            }
            else
            {
                sb.Append(nextLine.ToString());
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

    /// <summary>
    /// Strips matching surrounding double-quote or single-quote characters from
    /// <paramref name="value"/> when they are present.
    /// </summary>
    /// <param name="value">The raw value.</param>
    /// <param name="unescapeQuotes">
    /// When <c>true</c>, a quote character inside the value that is preceded by an odd number of
    /// backslashes loses one backslash. This is the inverse of how <see cref="IniFileWriter"/> escapes
    /// quotes when it quotes a value, so values round-trip even without escape-sequence decoding.
    /// </param>
    private static string StripQuotes(string value, bool unescapeQuotes)
    {
        if (value.Length >= 2)
        {
            var first = value[0];
            var last  = value[value.Length - 1];
            if ((first == '"'  && last == '"') ||
                (first == '\'' && last == '\''))
            {
                var inner = value.Substring(1, value.Length - 2);
                return unescapeQuotes ? UnescapeQuote(inner, first) : inner;
            }
        }
        return value;
    }

    /// <summary>
    /// Inverse of the writer's quote escaping without escape sequences. The writer turns n backslashes before
    /// a quote into 2n+1 and n backslashes at the end into 2n, so: an odd run before a quote becomes (run-1)/2
    /// backslashes and the quote, an even run at the end is halved. Anything else was not written by the
    /// writer (e.g. a hand-written <c>"C:\Temp\"</c>) and is kept as it is.
    /// </summary>
    private static string UnescapeQuote(string value, char quoteChar)
    {
        if (value.IndexOf('\\') < 0)
            return value;

        var sb = new StringBuilder(value.Length);
        var i = 0;
        while (i < value.Length)
        {
            if (value[i] != '\\')
            {
                sb.Append(value[i++]);
                continue;
            }

            var start = i;
            while (i < value.Length && value[i] == '\\')
                i++;
            var run = i - start;
            if (i < value.Length && value[i] == quoteChar && run % 2 == 1)
                sb.Append('\\', (run - 1) / 2);
            else if (i == value.Length && run % 2 == 0)
                sb.Append('\\', run / 2);
            else
                sb.Append('\\', run);
        }
        return sb.ToString();
    }

    /// <summary>
    /// Decodes standard C-style escape sequences in <paramref name="value"/>.
    /// Unrecognised sequences are left unchanged (the backslash is preserved).
    /// </summary>
    private static string DecodeEscapeSequences(string value)
    {
        if (!value.Contains('\\'))
            return value;

        var sb = new StringBuilder(value.Length);
        var i  = 0;
        while (i < value.Length)
        {
            var c = value[i];
            if (c != '\\' || i + 1 >= value.Length)
            {
                sb.Append(c);
                i++;
                continue;
            }

            var next = value[i + 1];
            switch (next)
            {
                case '\\': sb.Append('\\');  i += 2; break;
                case 'n':  sb.Append('\n');  i += 2; break;
                case 'r':  sb.Append('\r');  i += 2; break;
                case 't':  sb.Append('\t');  i += 2; break;
                case '0':  sb.Append('\0');  i += 2; break;
                case '"':  sb.Append('"');   i += 2; break;
                case '\'': sb.Append('\'');  i += 2; break;
                case 'a':  sb.Append('\a');  i += 2; break;
                case 'b':  sb.Append('\b');  i += 2; break;
                case 'x' when i + 3 < value.Length &&
                              IsHexDigit(value[i + 2]) && IsHexDigit(value[i + 3]):
                    sb.Append((char)Convert.ToByte(value.Substring(i + 2, 2), 16));
                    i += 4;
                    break;
                default:
                    // Unknown escape: keep as-is
                    sb.Append('\\');
                    sb.Append(next);
                    i += 2;
                    break;
            }
        }
        return sb.ToString();
    }

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
