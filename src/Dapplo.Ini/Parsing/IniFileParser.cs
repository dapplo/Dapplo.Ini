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

        // A byte order mark left in the string (e.g. from Encoding.GetString) is not whitespace.
        var span = content;
        if (!span.IsEmpty && span[0] == '\uFEFF')
            span = span.Slice(1);

        // Pre-pass (no allocations beyond a pooled array): the number of entries per section header, so every
        // section's dictionary and list are created with the right size instead of growing (re-allocating) while parsing.
        var sizes = CountEntriesPerSection(span, options.AssignmentDelimiters, out var headerCount);
        try
        {
            var parser = new LineParser(options, sizes, headerCount, byteOrderMarkHandled: true);
            while (!span.IsEmpty)
                parser.ProcessLine(ReadLine(ref span));
            return parser.Complete();
        }
        finally
        {
            System.Buffers.ArrayPool<int>.Shared.Return(sizes);
        }
    }

    /// <summary>
    /// Parses the INI content of <paramref name="reader"/>. Content of more than a few KB is parsed line by line
    /// while it is read, so it is never held as a whole: only one small pooled buffer is used, however large it is.
    /// </summary>
    /// <param name="reader">The content; read to the end but not disposed.</param>
    /// <param name="options">Parser options; when <c>null</c>, <see cref="IniParserOptions.Default"/> is used.</param>
    public static IniFile Parse(TextReader reader, IniParserOptions? options = null)
        => Parse(reader, options, PooledLineReader.DefaultBufferSize);

    // With a small buffer size even short content is streamed (used by the tests).
    internal static IniFile Parse(TextReader reader, IniParserOptions? options, int bufferSize)
    {
        if (reader is null) throw new ArgumentNullException(nameof(reader));
        using var lines = new PooledLineReader(reader, bufferSize, leaveOpen: true);
        return Parse(lines, options);
    }

    private static IniFile Parse(PooledLineReader lines, IniParserOptions? options)
    {
        // Content that fits into the buffer (most configuration files) is parsed as a whole, with the pre-pass
        // that sizes the sections; only larger content is parsed while it is read.
        if (lines.TryFillToEnd())
            return Parse(lines.Buffered, options);
        return ParseStreaming(lines, options);
    }

    private static IniFile ParseStreaming(PooledLineReader lines, IniParserOptions? options)
    {
        var parser = new LineParser(options ?? IniParserOptions.Default, null, 0, byteOrderMarkHandled: false);
        while (true)
        {
            while (lines.TryReadLine(out var line))
                parser.ProcessLine(line);
            if (lines.IsCompleted)
                return parser.Complete();
            lines.Fill();
        }
    }

    /// <summary>
    /// Parses an INI file from the file system using the specified <paramref name="encoding"/>
    /// (defaults to UTF-8 when <c>null</c>).
    /// The file is opened with <see cref="FileAccess.Read"/> and <see cref="FileShare.ReadWrite"/>
    /// so that it is never held open or locked for writing after parsing.
    /// </summary>
    /// <remarks>
    /// The file is decoded into one small pooled buffer: a file that fits is parsed from it as a whole, a larger
    /// file is parsed line by line while it is read. Neither a string nor a buffer for the whole content is
    /// allocated, so a large file never lands on the large object heap.
    /// </remarks>
    /// <param name="filePath">Path to the INI file to parse.</param>
    /// <param name="encoding">Character encoding; defaults to UTF-8.</param>
    /// <param name="options">
    /// Parser options; when <c>null</c>, <see cref="IniParserOptions.Default"/> is used.
    /// </param>
    public static IniFile ParseFile(string filePath, Encoding? encoding = null, IniParserOptions? options = null)
    {
        using var reader = PooledLineReader.OpenFile(filePath, encoding);
        var iniFile = Parse(reader, options);
        iniFile.FilePath = filePath;
        return iniFile;
    }

    /// <summary>
    /// Asynchronously parses an INI file from the file system using the specified
    /// <paramref name="encoding"/> (defaults to UTF-8 when <c>null</c>).
    /// The file is opened with <see cref="FileAccess.Read"/> and <see cref="FileShare.ReadWrite"/>
    /// so that it is never held open or locked for writing after parsing.
    /// </summary>
    /// <remarks>
    /// The file is streamed like <see cref="ParseFile"/>: every block is parsed as soon as it is read.
    /// </remarks>
    /// <param name="filePath">Path to the INI file to parse.</param>
    /// <param name="encoding">Character encoding; defaults to UTF-8.</param>
    /// <param name="options">
    /// Parser options; when <c>null</c>, <see cref="IniParserOptions.Default"/> is used.
    /// </param>
    /// <param name="cancellationToken">Token to cancel the async operation.</param>
    public static async Task<IniFile> ParseFileAsync(string filePath, Encoding? encoding = null, IniParserOptions? options = null, CancellationToken cancellationToken = default)
    {
        using var reader = PooledLineReader.OpenFile(filePath, encoding, asynchronous: true);
        IniFile iniFile;
        if (await reader.TryFillToEndAsync(cancellationToken).ConfigureAwait(false))
        {
            iniFile = Parse(reader.Buffered, options);
        }
        else
        {
            var parser = new LineParser(options ?? IniParserOptions.Default, null, 0, byteOrderMarkHandled: false);
            while (!ProcessBufferedLines(reader, parser))
                await reader.FillAsync(cancellationToken).ConfigureAwait(false);
            iniFile = parser.Complete();
        }
        iniFile.FilePath = filePath;
        return iniFile;
    }

    // Parses the complete lines in the reader's buffer (spans can't live across an await); true at the end.
    private static bool ProcessBufferedLines(PooledLineReader reader, LineParser parser)
    {
        while (reader.TryReadLine(out var line))
            parser.ProcessLine(line);
        return reader.IsCompleted;
    }

    /// <summary>
    /// The parser itself: gets the content one line at a time (from a span or a streaming reader), so lines
    /// never have to be in memory together. A line continuation is collected until the value is complete.
    /// </summary>
    private sealed class LineParser
    {
        private readonly IniParserOptions _options;
        private readonly IniFile _iniFile;
        private readonly StringComparer _keyComparer;
        private readonly int[]? _sizes;
        private readonly int _headerCount;
        private int _headerOrdinal;
        private bool _firstLine;

        private IniSection? _currentSection;
        private readonly List<string> _pendingComments = new();
        // Raw blank/comment/unparseable lines, only recorded with PreserveTrivia.
        private readonly List<string>? _pendingTrivia;

        // Streaming (no pre-pass to size the sections): the entries of the section being parsed are collected here,
        // in buffers reused for every section, and handed to the section once it is complete, so its dictionary and
        // list are sized once instead of growing.
        private readonly bool _collectSections;
        private IniSection? _openSection;
        private readonly List<IniEntry>? _openEntries;
        private readonly Dictionary<string, IniEntry>? _openKeys;

        // A value continued on the next line(s): the key, the value as written on the first line and the joined value.
        private string? _continuationKey;
        private string? _continuationFirst;
        private StringBuilder? _continuation;

        public LineParser(IniParserOptions options, int[]? sizes, int headerCount, bool byteOrderMarkHandled)
        {
            _firstLine = !byteOrderMarkHandled;
            _options = options;
            var sectionComparer = options.CaseSensitiveSections ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;
            _keyComparer = options.CaseSensitiveKeys ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;
            _iniFile = new IniFile(sectionComparer, _keyComparer);
            _pendingTrivia = options.PreserveTrivia ? new List<string>() : null;
            _sizes = sizes;
            _headerCount = headerCount;
            _collectSections = sizes == null;
            if (_collectSections)
            {
                _openEntries = new List<IniEntry>();
                _openKeys = new Dictionary<string, IniEntry>(_keyComparer);
            }
        }

        private void OpenSection(IniSection section)
        {
            CloseSection();
            if (_collectSections)
                _openSection = section;
        }

        private void CloseSection()
        {
            if (_openSection == null)
                return;
            _openSection.InitializeEntries(_openEntries!);
            _openEntries!.Clear();
            _openKeys!.Clear();
            _openSection = null;
        }

        private IReadOnlyList<string>? TakeTrivia()
        {
            if (_pendingTrivia == null) return null;
            var trivia = _pendingTrivia.ToArray();
            _pendingTrivia.Clear();
            return trivia;
        }

        public void ProcessLine(ReadOnlySpan<char> lineSpan)
        {
            if (_firstLine)
            {
                _firstLine = false;
                // A byte order mark left in the content is not whitespace.
                if (!lineSpan.IsEmpty && lineSpan[0] == '\uFEFF')
                    lineSpan = lineSpan.Slice(1);
            }

            // Trim whitespace for classification
            var trimmed = lineSpan.Trim();

            if (_continuationKey != null && ContinueValue(trimmed))
                return;

            if (trimmed.IsEmpty)
            {
                // Blank line: reset pending comments (don't carry over to next key)
                _pendingComments.Clear();
                _pendingTrivia?.Add(string.Empty);
                return;
            }

            var first = trimmed[0];

            if (first == ';' || first == '#')
            {
                // Comment line – strip the leading ; or # and optional space
                var commentContent = trimmed.Slice(1);
                if (!commentContent.IsEmpty && commentContent[0] == ' ')
                    commentContent = commentContent.Slice(1);
                _pendingComments.Add(commentContent.ToString());
                _pendingTrivia?.Add(lineSpan.TrimEnd().ToString());
                return;
            }

            if (first == '[')
            {
                ProcessSectionHeader(lineSpan, trimmed);
                return;
            }

            // Key=value pair (assignment delimiter is configurable, defaults to '=' and ':')
            var assignmentIndex = FindAssignmentIndex(trimmed, _options.AssignmentDelimiters);
            if (assignmentIndex > 0)
            {
                var key = trimmed.Slice(0, assignmentIndex).TrimEnd().ToString();
                var rawValue = trimmed.Slice(assignmentIndex + 1).TrimStart();

                // Line continuation: if value ends with '\', the next line(s) are joined (the only case with an extra string)
                if (_options.LineContinuation && EndsWithContinuation(rawValue))
                {
                    _continuationKey = key;
                    _continuationFirst = rawValue.ToString();
                    return;
                }

                // Quotes and escape sequences are processed on the span: one string per value
                AddEntry(key, CreateValue(rawValue, _options, null));
            }
            else
            {
                // Lines that don't match any pattern are ignored (but kept as trivia when requested)
                _pendingTrivia?.Add(lineSpan.TrimEnd().ToString());
            }
        }

        private void ProcessSectionHeader(ReadOnlySpan<char> lineSpan, ReadOnlySpan<char> trimmed)
        {
            // Section header [SectionName]
            var closeBracket = trimmed.IndexOf(']');
            if (closeBracket > 0)
            {
                var sectionName = trimmed.Slice(1, closeBracket - 1).Trim().ToString();
                // A repeated header continues the existing section instead of replacing it,
                // so that earlier keys are not lost and duplicate-key handling still applies.
                var existingSection = _iniFile.GetSection(sectionName);
                if (existingSection != null)
                {
                    // The lines above the repeated header stay pending for the next entry.
                    CloseSection();
                    _currentSection = existingSection;
                }
                else
                {
                    IReadOnlyList<string> comments = _pendingComments.Count > 0
                        ? _pendingComments.ToArray()
                        : Array.Empty<string>();
                    var capacity = _sizes != null && _headerOrdinal < _headerCount ? _sizes[_headerOrdinal + 1] : 0;
                    _currentSection = new IniSection(sectionName, comments, _keyComparer, capacity)
                    {
                        LeadingTrivia = TakeTrivia()
                    };
                    _iniFile.AddSection(_currentSection);
                    OpenSection(_currentSection);
                }
                _headerOrdinal++;
            }
            else
            {
                _pendingTrivia?.Add(lineSpan.TrimEnd().ToString()); // "[Unclosed": keep the line as it is
            }
            _pendingComments.Clear();
        }

        /// <summary>
        /// Handles the line after a value that ends with a backslash: its trimmed content replaces the backslash.
        /// Returns <c>true</c> when the line was used up by the value, <c>false</c> when it must be parsed as usual.
        /// </summary>
        private bool ContinueValue(ReadOnlySpan<char> trimmed)
        {
            if (_continuation == null)
            {
                // Nothing to join (a blank line or a section header): keep the value as written,
                // e.g. "Dir = C:\Temp\" directly followed by "[Next]".
                if (trimmed.IsEmpty || trimmed[0] == '[')
                {
                    CompleteContinuation();
                    return false;
                }
                var firstPart = _continuationFirst!;
                _continuation = new StringBuilder(firstPart.Length + 64);
                _continuation.Append(firstPart, 0, firstPart.Length - 1);   // without the trailing backslash
            }
            else if (!trimmed.IsEmpty && trimmed[0] == '[')
            {
                // Never swallow a section header: it is parsed as usual.
                CompleteContinuation();
                return false;
            }
            else if (trimmed.IsEmpty)
            {
                // An empty continuation line ends the value (and is used up)
                CompleteContinuation();
                return true;
            }

            if (EndsWithContinuation(trimmed))
            {
                // This line also continues — append without trailing backslash
                Append(_continuation, trimmed.Slice(0, trimmed.Length - 1));
                return true;
            }
            Append(_continuation, trimmed);
            CompleteContinuation();
            return true;
        }

        private void CompleteContinuation()
        {
            var key = _continuationKey!;
            var raw = _continuation != null ? _continuation.ToString() : _continuationFirst!;
            _continuationKey = null;
            _continuationFirst = null;
            _continuation = null;
            AddEntry(key, CreateValue(raw.AsSpan(), _options, raw));
        }

        private void AddEntry(string key, string value)
        {
            // Ensure there is a section (global / no-section entries go into a synthetic "" section)
            var section = _currentSection;
            if (section == null)
            {
                section = _currentSection = _iniFile.GetOrAddSection(string.Empty);
                OpenSection(section);
            }
            var collecting = ReferenceEquals(section, _openSection);
            IniEntry? existing = null;
            var exists = collecting ? _openKeys!.TryGetValue(key, out existing) : section.ContainsKey(key);

            // Duplicate key handling
            if (_options.DuplicateKeyHandling != DuplicateKeyHandling.LastWins && exists)
            {
                switch (_options.DuplicateKeyHandling)
                {
                    case DuplicateKeyHandling.FirstWins:
                        // Skip – keep the first value
                        _pendingComments.Clear();
                        return;
                    case DuplicateKeyHandling.ThrowError:
                        throw new InvalidOperationException(
                            $"Duplicate key '{key}' found in section '{section.Name}'.");
                }
            }

            IReadOnlyList<string> entryComments = _pendingComments.Count > 0
                ? _pendingComments.ToArray()
                : Array.Empty<string>();
            var entry = new IniEntry(key, value, entryComments) { LeadingTrivia = TakeTrivia() };
            if (!collecting)
                section.SetEntry(entry);
            else if (existing != null)
                existing.Value = value;   // what SetEntry does for a repeated key
            else
            {
                _openKeys![key] = entry;
                _openEntries!.Add(entry);
            }
            _pendingComments.Clear();
        }

        /// <summary>Finishes the content (a value continued at the very end) and returns the parsed file.</summary>
        public IniFile Complete()
        {
            if (_continuationKey != null)
                CompleteContinuation();
            CloseSection();
            if (_pendingTrivia is { Count: > 0 })
                _iniFile.TrailingTrivia = _pendingTrivia.ToArray();
            return _iniFile;
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

        // A delimiter at the start of the line is no assignment (an empty key), and its later occurrences are
        // not considered either: the rare case keeps the original per-delimiter search.
        if (delimiters.IndexOf(line[0]) < 0)
            return line.IndexOfAny(delimiters.AsSpan());

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
