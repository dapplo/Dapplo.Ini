// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;

namespace Dapplo.Ini.Parsing;

/// <summary>
/// Writes an <see cref="IniFile"/> back to disk (or a <see cref="TextWriter"/>),
/// preserving comments and section order.
/// </summary>
public static class IniFileWriter
{
    /// <summary>Writes <paramref name="iniFile"/> to the file at <paramref name="filePath"/> using the specified
    /// <paramref name="encoding"/> (defaults to UTF-8 when <c>null</c>).</summary>
    /// <remarks>
    /// The content is first written to a temporary file next to <paramref name="filePath"/>, flushed to disk,
    /// and then swapped into place. A crash or a full disk during the write therefore never leaves a
    /// truncated or empty INI file behind: either the old or the new content is on disk.
    /// </remarks>
    public static void WriteFile(string filePath, IniFile iniFile, Encoding? encoding = null, IniWriterOptions? options = null)
    {
        encoding ??= Encoding.UTF8;
        if (MustWriteInPlace(filePath, asynchronous: false, out var tempPath, out var tempStream))
        {
            using var inPlace = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.Read, UnbufferedFileStream);
            WriteToStream(inPlace, iniFile, encoding, options);
            return;
        }
        try
        {
            using (var stream = tempStream!)
            {
                WriteToStream(stream, iniFile, encoding, options);
                stream.Flush(flushToDisk: true);
            }
            ReplaceFile(tempPath!, filePath);
        }
        catch
        {
            TryDelete(tempPath!);
            throw;
        }
    }

    /// <summary>Asynchronously writes <paramref name="iniFile"/> to the file at <paramref name="filePath"/> using the
    /// specified <paramref name="encoding"/> (defaults to UTF-8 when <c>null</c>).</summary>
    /// <remarks>Uses the same write-to-temp-then-replace strategy as <see cref="WriteFile"/>.</remarks>
    public static async Task WriteFileAsync(string filePath, IniFile iniFile, Encoding? encoding = null, IniWriterOptions? options = null, CancellationToken cancellationToken = default)
    {
        // Streamed: the content is encoded and written in blocks while it is produced, so neither a string nor a
        // byte array of the whole file is created.
        encoding ??= Encoding.UTF8;
        if (MustWriteInPlace(filePath, asynchronous: true, out var tempPath, out var tempStream))
        {
            using var inPlace = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.Read, UnbufferedFileStream, FileOptions.Asynchronous);
            await WriteToStreamAsync(inPlace, iniFile, encoding, options, cancellationToken).ConfigureAwait(false);
            return;
        }
        try
        {
            using (var stream = tempStream!)
            {
                await WriteToStreamAsync(stream, iniFile, encoding, options, cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }
            ReplaceFile(tempPath!, filePath);
        }
        catch
        {
            TryDelete(tempPath!);
            throw;
        }
    }

    /// <summary>
    /// Decides between the atomic temp-file-and-replace strategy and writing the file in place, and opens
    /// the temp file for the former. The file is written in place when it is a symbolic link (replacing it
    /// would turn the link into a plain file) or when no temp file can be created next to it (for example
    /// when only the file itself, not its folder, is writable).
    /// </summary>
    private static bool MustWriteInPlace(string filePath, bool asynchronous, out string? tempPath, out FileStream? tempStream)
    {
        tempPath = null;
        tempStream = null;
        try
        {
            if (File.Exists(filePath) && (File.GetAttributes(filePath) & FileAttributes.ReparsePoint) != 0)
                return true;
        }
        catch (IOException)
        {
            // Attributes unavailable: try the normal path.
        }

        var candidate = CreateTempPath(filePath);
        try
        {
            tempStream = new FileStream(candidate, FileMode.CreateNew, FileAccess.Write, FileShare.None, UnbufferedFileStream,
                asynchronous ? FileOptions.Asynchronous : FileOptions.None);
        }
        catch (Exception ex) when ((ex is UnauthorizedAccessException || ex is IOException) && ex is not DirectoryNotFoundException && File.Exists(filePath))
        {
            return true;
        }
        tempPath = candidate;
        return false;
    }

    // The PooledStreamWriter writes whole blocks: a FileStream buffer would only copy them once more.
    private const int UnbufferedFileStream = 1;

    private static void WriteToStream(Stream stream, IniFile iniFile, Encoding encoding, IniWriterOptions? options)
    {
        using var writer = new PooledStreamWriter(stream, encoding, synchronousFlush: true);
        Write(writer, iniFile, options);
        writer.Complete();
    }

    private static async Task WriteToStreamAsync(Stream stream, IniFile iniFile, Encoding encoding, IniWriterOptions? options, CancellationToken cancellationToken)
    {
        using var writer = new PooledStreamWriter(stream, encoding, synchronousFlush: false);
        var writerOptions = ResolveOptions(iniFile, options);
        var firstSection = true;
        foreach (var section in iniFile.Sections)
        {
            var sectionOptions = WriteSectionStart(writer, section, writerOptions, firstSection);
            firstSection = false;
            foreach (var entry in section.Entries)
            {
                WriteEntry(writer, entry, sectionOptions);
                if (writer.IsBlockFull)
                    await writer.FlushBlockAsync(cancellationToken).ConfigureAwait(false);
            }
            if (writer.IsBlockFull)
                await writer.FlushBlockAsync(cancellationToken).ConfigureAwait(false);
        }
        if (iniFile.TrailingTrivia != null)
            WriteTrivia(writer, iniFile.TrailingTrivia);
        await writer.CompleteAsync(cancellationToken).ConfigureAwait(false);
    }

    private static string CreateTempPath(string filePath)
    {
        var fullPath = Path.GetFullPath(filePath);
        var directory = Path.GetDirectoryName(fullPath) ?? ".";
        return Path.Combine(directory, $"{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
    }

    private static void ReplaceFile(string tempPath, string filePath)
    {
#if NET
        // Keep the permissions of the existing file (a new file would get the default umask).
        if (!OperatingSystem.IsWindows() && File.Exists(filePath))
        {
            try { File.SetUnixFileMode(tempPath, File.GetUnixFileMode(filePath)); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* best effort */ }
        }
#endif
        if (!File.Exists(filePath))
        {
            try
            {
                File.Move(tempPath, filePath);
                return;
            }
            catch (IOException) when (File.Exists(filePath))
            {
                // Somebody created the target in the meantime; fall through to Replace.
            }
        }

        try
        {
            // A reader (virus scanner, indexer, editor) that has the file open briefly blocks the swap.
            // Retry a few times before falling back.
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    File.Replace(tempPath, filePath, destinationBackupFileName: null, ignoreMetadataErrors: true);
                    return;
                }
                catch (IOException) when (attempt < ReplaceAttempts && File.Exists(tempPath))
                {
                    Thread.Sleep(ReplaceRetryDelayMs * attempt);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or PlatformNotSupportedException or UnauthorizedAccessException && File.Exists(tempPath))
        {
            // Some file systems (e.g. certain network shares) do not support File.Replace.
            // Fall back to a copy, which is not atomic but still never truncates before the data is complete.
            File.Copy(tempPath, filePath, overwrite: true);
            TryDelete(tempPath);
        }
    }

    private const int ReplaceAttempts = 5;
    private const int ReplaceRetryDelayMs = 20;

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
            // Best effort cleanup of a temporary file.
        }
    }

    /// <summary>Returns the INI file as a string.</summary>
    public static string WriteToString(IniFile iniFile, IniWriterOptions? options = null)
    {
        var sb = new StringBuilder();
        using var writer = new StringWriter(sb);
        Write(writer, iniFile, options);
        return sb.ToString();
    }

    /// <summary>Writes <paramref name="iniFile"/> to <paramref name="writer"/>.</summary>
    public static void Write(TextWriter writer, IniFile iniFile, IniWriterOptions? options = null)
    {
        var writerOptions = ResolveOptions(iniFile, options);
        var firstSection = true;
        foreach (var section in iniFile.Sections)
        {
            var sectionOptions = WriteSectionStart(writer, section, writerOptions, firstSection);
            firstSection = false;
            foreach (var entry in section.Entries)
                WriteEntry(writer, entry, sectionOptions);
        }

        if (iniFile.TrailingTrivia != null)
            WriteTrivia(writer, iniFile.TrailingTrivia);
    }

    private static IniWriterOptions ResolveOptions(IniFile iniFile, IniWriterOptions? options)
    {
        // Explicitly passed options win; without options the file's own separator is used.
        if (options != null)
            return options;
        if (iniFile.AssignmentSeparator == IniWriterOptions.Default.AssignmentSeparator)
            return IniWriterOptions.Default;
        var writerOptions = IniWriterOptions.Default.Clone();
        writerOptions.AssignmentSeparator = iniFile.AssignmentSeparator;
        return writerOptions;
    }

    /// <summary>Writes the lines above the header and the header itself; returns the options for the entries.</summary>
    private static IniWriterOptions WriteSectionStart(TextWriter writer, IniSection section, IniWriterOptions writerOptions, bool firstSection)
    {
        var sectionOptions = writerOptions.Apply(section.WriterOptionsOverride);

        if (section.LeadingTrivia != null)
        {
            // Recorded layout: exactly the blank lines and comments that were above the header.
            WriteTrivia(writer, section.LeadingTrivia);
        }
        else
        {
            if (!firstSection)
                writer.WriteLine();

            // Section comments
            if (sectionOptions.WriteComments)
                WriteComments(writer, section.Comments);
        }

        // Only write header for named sections
        if (!string.IsNullOrEmpty(section.Name))
        {
            writer.Write('[');
            writer.Write(section.Name);
            writer.WriteLine(']');
        }
        return sectionOptions;
    }

    private static void WriteEntry(TextWriter writer, IniEntry entry, IniWriterOptions sectionOptions)
    {
        var entryOptions = sectionOptions.Apply(entry.WriterOptionsOverride);

        if (entry.LeadingTrivia != null)
            WriteTrivia(writer, entry.LeadingTrivia);
        else if (entryOptions.WriteComments)
            WriteComments(writer, entry.Comments);

        if (entry.IsCommentedOut)
            writer.Write("; ");
        writer.Write(entry.Key);
        writer.Write(entryOptions.AssignmentSeparator);
        writer.WriteLine(FormatValue(entry.Value, entryOptions));
    }

    private static void WriteTrivia(TextWriter writer, IReadOnlyList<string> lines)
    {
        foreach (var line in lines)
            writer.WriteLine(line);
    }

    /// <summary>Writes each comment line prefixed with "; " — also every line of a multi-line comment.</summary>
    private static void WriteComments(TextWriter writer, IReadOnlyList<string> comments)
    {
        foreach (var comment in comments)
        {
            // Every line of a multi-line comment becomes a comment line ("\r\n", "\n" and "\r" end a line).
            var start = 0;
            while (true)
            {
                var lineBreak = comment.IndexOfAny(LineBreakChars, start);
                var end = lineBreak < 0 ? comment.Length : lineBreak;
                writer.Write("; ");
                WritePart(writer, comment, start, end - start);
                writer.WriteLine();
                if (lineBreak < 0)
                    break;
                start = lineBreak + 1;
                if (comment[lineBreak] == '\r' && start < comment.Length && comment[start] == '\n')
                    start++;
            }
        }
    }

    private static readonly char[] LineBreakChars = { '\r', '\n' };

    // Writes a part of a string without creating a substring (on .NET; .NET Framework writers have no span overload).
    private static void WritePart(TextWriter writer, string text, int start, int length)
    {
        if (start == 0 && length == text.Length)
            writer.Write(text);
        else if (length > 0)
        {
#if NET
            writer.Write(text.AsSpan(start, length));
#else
            writer.Write(text.Substring(start, length));
#endif
        }
    }

    internal static string FormatValue(string? value, IniWriterOptions options)
    {
        var result = value ?? string.Empty;
        // A raw line break would end the value and could inject keys or sections into the file,
        // so values containing one are always written with escape sequences.
        // Enable escape sequences on the parser to read them back as line breaks.
        if (options.EscapeSequences || result.IndexOfAny(LineBreakChars) >= 0)
            result = EncodeEscapeSequences(result);
        return ApplyQuoting(result, options, escapeSequencesEncoded: options.EscapeSequences);
    }

    private static string ApplyQuoting(string value, IniWriterOptions options, bool escapeSequencesEncoded)
    {
        // With escape sequences, backslashes are already doubled, so escaping the quotes is enough and the
        // parser's escape decoding restores the value. Without them, the quote escaping itself must be
        // reversible (see EscapeQuoteReversible), because the parser only undoes the quote escaping.
        Func<string, char, string> escape = escapeSequencesEncoded ? EscapeUnescapedQuote : EscapeQuoteReversible;
        return options.QuoteStyle switch
        {
            IniValueQuoteStyle.Single => $"'{escape(value, '\'')}'",
            IniValueQuoteStyle.Double => $"\"{escape(value, '\"')}\"",
            IniValueQuoteStyle.Auto when NeedsQuoting(value, options.AssignmentSeparator) => $"\"{escape(value, '\"')}\"",
            _ => value
        };
    }

    /// <summary>
    /// Escapes <paramref name="quoteChar"/> inside a value that is written between quotes, without escape
    /// sequences: every run of backslashes directly before a quote (or at the very end, before the closing
    /// quote) is doubled, and the quote gets one more backslash. The parser reverses exactly this, so any
    /// value round-trips.
    /// </summary>
    private static string EscapeQuoteReversible(string value, char quoteChar)
    {
        if (string.IsNullOrEmpty(value) || (value.IndexOf(quoteChar) < 0 && value[value.Length - 1] != '\\'))
            return value;

        var sb = new StringBuilder(value.Length + 8);
        var backslashes = 0;
        foreach (var c in value)
        {
            if (c == '\\')
            {
                backslashes++;
                continue;
            }
            if (c == quoteChar)
            {
                sb.Append('\\', backslashes * 2 + 1);
            }
            else
            {
                sb.Append('\\', backslashes);
            }
            backslashes = 0;
            sb.Append(c);
        }
        sb.Append('\\', backslashes * 2);
        return sb.ToString();
    }

    private static string EscapeUnescapedQuote(string value, char quoteChar)
    {
        if (string.IsNullOrEmpty(value) || value.IndexOf(quoteChar) < 0)
            return value;

        var sb = new StringBuilder(value.Length + 8);
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (c == quoteChar)
            {
                var backslashes = 0;
                for (var j = i - 1; j >= 0 && value[j] == '\\'; j--)
                    backslashes++;

                if (backslashes % 2 == 0)
                    sb.Append('\\');
            }
            sb.Append(c);
        }
        return sb.ToString();
    }

    private static bool NeedsQuoting(string value, string assignmentSeparator)
    {
        if (string.IsNullOrEmpty(value))
            return false;

        // Leading or trailing whitespace (what Trim() would remove) would be lost
        if (char.IsWhiteSpace(value[0]) || char.IsWhiteSpace(value[value.Length - 1]))
            return true;

        if (value[0] == ';' || value[0] == '#')
            return true;

        // A value that already looks quoted would lose its quotes when read with QuotedValues.
        if (value[0] == '"' || value[0] == '\'')
            return true;

        foreach (var c in assignmentSeparator)
        {
            if (char.IsWhiteSpace(c)) continue;
            if (value.IndexOf(c) >= 0) return true;
        }
        return false;
    }

    // The chars EncodeEscapeSequences replaces.
    private static readonly char[] EscapedChars = { '\\', '\n', '\r', '\t', '\0', '\a', '\b' };

    private static string EncodeEscapeSequences(string value)
    {
        if (string.IsNullOrEmpty(value) || value.IndexOfAny(EscapedChars) < 0)
            return value;

        var sb = new StringBuilder(value.Length + 8);
        foreach (var c in value)
        {
            switch (c)
            {
                case '\\': sb.Append(@"\\"); break;
                case '\n': sb.Append(@"\n"); break;
                case '\r': sb.Append(@"\r"); break;
                case '\t': sb.Append(@"\t"); break;
                case '\0': sb.Append(@"\0"); break;
                case '\a': sb.Append(@"\a"); break;
                case '\b': sb.Append(@"\b"); break;
                default: sb.Append(c); break;
            }
        }
        return sb.ToString();
    }
}
