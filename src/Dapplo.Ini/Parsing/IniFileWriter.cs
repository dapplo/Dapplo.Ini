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
        var tempPath = CreateTempPath(filePath);
        try
        {
            using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                using (var writer = new StreamWriter(stream, encoding ?? Encoding.UTF8, 4096, leaveOpen: true))
                {
                    Write(writer, iniFile, options);
                }
                stream.Flush(flushToDisk: true);
            }
            ReplaceFile(tempPath, filePath);
        }
        catch
        {
            TryDelete(tempPath);
            throw;
        }
    }

    /// <summary>Asynchronously writes <paramref name="iniFile"/> to the file at <paramref name="filePath"/> using the
    /// specified <paramref name="encoding"/> (defaults to UTF-8 when <c>null</c>).</summary>
    /// <remarks>Uses the same write-to-temp-then-replace strategy as <see cref="WriteFile"/>.</remarks>
    public static async Task WriteFileAsync(string filePath, IniFile iniFile, Encoding? encoding = null, IniWriterOptions? options = null, CancellationToken cancellationToken = default)
    {
        var content = WriteToString(iniFile, options);
        var bytes = (encoding ?? Encoding.UTF8).GetPreamble().Concat((encoding ?? Encoding.UTF8).GetBytes(content)).ToArray();
        var tempPath = CreateTempPath(filePath);
        try
        {
            using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous))
            {
                await stream.WriteAsync(bytes, 0, bytes.Length, cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }
            ReplaceFile(tempPath, filePath);
        }
        catch
        {
            TryDelete(tempPath);
            throw;
        }
    }

    private static string CreateTempPath(string filePath)
    {
        var fullPath = Path.GetFullPath(filePath);
        var directory = Path.GetDirectoryName(fullPath) ?? ".";
        return Path.Combine(directory, $"{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
    }

    private static void ReplaceFile(string tempPath, string filePath)
    {
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
            File.Replace(tempPath, filePath, destinationBackupFileName: null, ignoreMetadataErrors: true);
        }
        catch (Exception ex) when (ex is IOException or PlatformNotSupportedException or UnauthorizedAccessException && File.Exists(tempPath))
        {
            // Some file systems (e.g. certain network shares) do not support File.Replace.
            // Fall back to a copy, which is not atomic but still never truncates before the data is complete.
            File.Copy(tempPath, filePath, overwrite: true);
            TryDelete(tempPath);
        }
    }

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
        var writerOptions = (options ?? IniWriterOptions.Default).Clone();
        writerOptions.AssignmentSeparator = iniFile.AssignmentSeparator;

        bool firstSection = true;
        foreach (var section in iniFile.Sections)
        {
            var sectionOptions = writerOptions.Apply(section.WriterOptionsOverride);

            if (!firstSection)
                writer.WriteLine();
            firstSection = false;

            // Section comments
            if (sectionOptions.WriteComments)
            {
                foreach (var comment in section.Comments)
                {
                    writer.Write("; ");
                    writer.WriteLine(comment);
                }
            }

            // Only write header for named sections
            if (!string.IsNullOrEmpty(section.Name))
            {
                writer.Write('[');
                writer.Write(section.Name);
                writer.WriteLine(']');
            }

            // Entries
            foreach (var entry in section.Entries)
            {
                var entryOptions = sectionOptions.Apply(entry.WriterOptionsOverride);

                if (entryOptions.WriteComments)
                {
                    foreach (var comment in entry.Comments)
                    {
                        writer.Write("; ");
                        writer.WriteLine(comment);
                    }
                }

                writer.Write(entry.Key);
                writer.Write(entryOptions.AssignmentSeparator);
                writer.WriteLine(FormatValue(entry.Value, entryOptions));
            }
        }
    }

    internal static string FormatValue(string? value, IniWriterOptions options)
    {
        var result = value ?? string.Empty;
        if (options.EscapeSequences)
            result = EncodeEscapeSequences(result);
        return ApplyQuoting(result, options);
    }

    private static string ApplyQuoting(string value, IniWriterOptions options)
    {
        return options.QuoteStyle switch
        {
            IniValueQuoteStyle.Single => $"'{EscapeUnescapedQuote(value, '\'')}'",
            IniValueQuoteStyle.Double => $"\"{EscapeUnescapedQuote(value, '\"')}\"",
            IniValueQuoteStyle.Auto when NeedsQuoting(value, options.AssignmentSeparator) => $"\"{EscapeUnescapedQuote(value, '\"')}\"",
            _ => value
        };
    }

    private static string EscapeUnescapedQuote(string value, char quoteChar)
    {
        if (string.IsNullOrEmpty(value))
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

        if (value != value.Trim())
            return true;

        if (value.StartsWith(";") || value.StartsWith("#"))
            return true;

        foreach (var c in assignmentSeparator)
        {
            if (char.IsWhiteSpace(c)) continue;
            if (value.Contains(c)) return true;
        }
        return false;
    }

    private static string EncodeEscapeSequences(string value)
    {
        if (string.IsNullOrEmpty(value))
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
