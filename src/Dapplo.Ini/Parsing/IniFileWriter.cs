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
    public static void WriteFile(string filePath, IniFile iniFile, Encoding? encoding = null)
    {
        using var writer = new StreamWriter(filePath, append: false, encoding ?? Encoding.UTF8);
        Write(writer, iniFile);
    }

    /// <summary>Asynchronously writes <paramref name="iniFile"/> to the file at <paramref name="filePath"/> using the
    /// specified <paramref name="encoding"/> (defaults to UTF-8 when <c>null</c>).</summary>
    public static async Task WriteFileAsync(string filePath, IniFile iniFile, Encoding? encoding = null, CancellationToken cancellationToken = default)
    {
        var content = WriteToString(iniFile);
#if NET
        await File.WriteAllTextAsync(filePath, content, encoding ?? Encoding.UTF8, cancellationToken).ConfigureAwait(false);
#else
        using var writer = new StreamWriter(filePath, append: false, encoding ?? Encoding.UTF8);
        await writer.WriteAsync(content).ConfigureAwait(false);
#endif
    }

    /// <summary>Returns the INI file as a string.</summary>
    public static string WriteToString(IniFile iniFile)
    {
        var sb = new StringBuilder();
        using var writer = new StringWriter(sb);
        Write(writer, iniFile);
        return sb.ToString();
    }

    /// <summary>Writes <paramref name="iniFile"/> to <paramref name="writer"/>.</summary>
    public static void Write(TextWriter writer, IniFile iniFile)
    {
        bool firstSection = true;
        foreach (var section in iniFile.Sections)
        {
            if (!firstSection)
                writer.WriteLine();
            firstSection = false;

            // Section comments
            foreach (var comment in section.Comments)
            {
                writer.Write("; ");
                writer.WriteLine(comment);
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
                foreach (var comment in entry.Comments)
                {
                    writer.Write("; ");
                    writer.WriteLine(comment);
                }

                writer.Write(entry.Key);
                writer.Write(" = ");
                writer.WriteLine(entry.Value ?? string.Empty);
            }
        }
    }
}
