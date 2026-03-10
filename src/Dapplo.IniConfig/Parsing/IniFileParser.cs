// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;

namespace Dapplo.IniConfig.Parsing;

/// <summary>
/// Parses INI file content using <see cref="ReadOnlySpan{T}"/> to minimise allocations.
/// Supports:
/// <list type="bullet">
///   <item>Sections: <c>[SectionName]</c></item>
///   <item>Key-value pairs: <c>key = value</c> or <c>key=value</c></item>
///   <item>Comments: lines starting with <c>;</c> or <c>#</c></item>
///   <item>Blank lines (ignored between entries; preserved as section/key comment context)</item>
/// </list>
/// </summary>
public static class IniFileParser
{
    /// <summary>
    /// Parses the content of an INI file from <paramref name="content"/> and returns an <see cref="IniFile"/>.
    /// </summary>
    public static IniFile Parse(string content)
    {
        var iniFile = new IniFile();
        var span = content.AsSpan();

        IniSection? currentSection = null;
        var pendingComments = new List<string>();

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
                    currentSection = new IniSection(sectionName, comments);
                    iniFile.AddSection(currentSection);
                }
                pendingComments.Clear();
                continue;
            }

            // Key=value pair
            var equalsIndex = trimmed.IndexOf('=');
            if (equalsIndex > 0)
            {
                var key = trimmed.Slice(0, equalsIndex).TrimEnd().ToString();
                var value = trimmed.Slice(equalsIndex + 1).TrimStart().ToString();

                // Ensure there is a section (global / no-section entries go into a synthetic "" section)
                currentSection ??= iniFile.GetOrAddSection(string.Empty);

                IReadOnlyList<string> entryComments = pendingComments.Count > 0
                    ? pendingComments.ToArray()
                    : (IReadOnlyList<string>)Array.Empty<string>();
                var entry = new IniEntry(key, value, entryComments);
                currentSection.SetEntry(entry);
                pendingComments.Clear();
            }
            // Lines that don't match any pattern are silently ignored
        }

        return iniFile;
    }

    /// <summary>
    /// Parses an INI file from the file system.
    /// </summary>
    public static IniFile ParseFile(string filePath)
    {
        var content = File.ReadAllText(filePath, Encoding.UTF8);
        return Parse(content);
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
        remaining = remaining.Slice(newLine + 1);

        // Handle \r\n
        if (!remaining.IsEmpty && remaining[0] == '\n')
            remaining = remaining.Slice(1);

        return result;
    }
}
