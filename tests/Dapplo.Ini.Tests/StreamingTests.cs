// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using Dapplo.Ini.Attributes;
using Dapplo.Ini.Converters;
using Dapplo.Ini.Interfaces;
using Dapplo.Ini.Parsing;

namespace Dapplo.Ini.Tests;

/// <summary>A section with list properties that use a custom delimiter.</summary>
public interface IDelimitedListSettings : IIniSection
{
    [IniValue(DefaultValue = "1;2;3", ListDelimiter = ';')]
    List<int>? Numbers { get; set; }

    [IniValue(DefaultValue = "a|b", ListDelimiter = '|')]
    string[]? Names { get; set; }
}

/// <summary>
/// Files are parsed and written as streams (line by line, block by block) with pooled buffers. The result must be
/// exactly the same as parsing or writing the content as a whole.
/// </summary>
public sealed class StreamingTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "MyProductStreamingTests", Guid.NewGuid().ToString("N"));

    public StreamingTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); } catch { /* best effort */ }
    }

    private static readonly string[] Contents =
    {
        "",
        "Key = Value",
        "\uFEFF[General]\r\nName = MyProduct\r\n",
        "; comment\n\n[A]\nk1 = v1\r\n; about k2\r\nk2: v2\r[B]\nx=1\n\n; trailing\n",
        "Global = 1\n[A]\nk = 1\n[B]\nk = 2\n[A]\nk2 = 3\n",
        // Line continuations: joined lines, a header and a blank line end them, a continuation at the very end
        "[C]\nPath = C:\\Temp\\\n[Next]\nLong = one \\\n  two \\\n three\nAfter = x\nBlank = a\\\n\nEnd = b\\",
        "[C]\nEscaped = C:\\Temp\\\\\nQuoted = \"a \\\"b\\\"\"\nTabbed = \"x\\ty\"\n=novalue\n:alsonone\nk==v\n",
        "[Unclosed\nk = v\n  indented = yes  \n#hash comment\nnot an entry\n",
    };

    private static IniParserOptions[] AllOptions() => new[]
    {
        IniParserOptions.Default,
        new IniParserOptions { LineContinuation = true, PreserveTrivia = true },
        new IniParserOptions { QuotedValues = true, EscapeSequences = true, LineContinuation = true },
        new IniParserOptions { DuplicateKeyHandling = DuplicateKeyHandling.FirstWins, QuotedValues = true, AssignmentDelimiters = "=" },
    };

    private static string Describe(IniFile file)
    {
        var sb = new StringBuilder();
        foreach (var section in file.Sections)
        {
            sb.Append('[').Append(section.Name).Append("] c=").Append(string.Join("|", section.Comments))
              .Append(" t=").Append(section.LeadingTrivia == null ? "-" : string.Join("|", section.LeadingTrivia)).AppendLine();
            foreach (var entry in section.Entries)
                sb.Append(entry.Key).Append('=').Append(entry.Value).Append(" c=").Append(string.Join("|", entry.Comments))
                  .Append(" t=").Append(entry.LeadingTrivia == null ? "-" : string.Join("|", entry.LeadingTrivia)).AppendLine();
        }
        sb.Append("trailing=").Append(file.TrailingTrivia == null ? "-" : string.Join("|", file.TrailingTrivia));
        return sb.ToString();
    }

    // Large content: many blocks, lines and continuations cross the block boundaries.
    private static string LargeContent()
    {
        var sb = new StringBuilder();
        for (var s = 0; s < 60; s++)
        {
            sb.Append("; section ").Append(s).Append("\r\n[Section").Append(s).Append("]\n");
            for (var k = 0; k < 40; k++)
            {
                sb.Append("Key").Append(k).Append(" = value ").Append(new string('x', (s * 7 + k * 13) % 90)).Append(" äöü €");
                if (k % 9 == 0)
                    sb.Append(" \\\n   continued ").Append(k);
                sb.Append(k % 2 == 0 ? "\r\n" : "\n");
            }
        }
        return sb.ToString();
    }

    public static IEnumerable<object[]> Cases()
    {
        var contents = Contents.Concat(new[] { LargeContent() }).ToArray();
        var options = AllOptions();
        for (var c = 0; c < contents.Length; c++)
            for (var o = 0; o < options.Length; o++)
                yield return new object[] { c, o };
    }

    private static string ContentAt(int index) => index < Contents.Length ? Contents[index] : LargeContent();

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task StreamingParse_GivesTheSameResult_AsParsingTheString(int contentIndex, int optionsIndex)
    {
        var content = ContentAt(contentIndex);
        var options = AllOptions()[optionsIndex];
        var expected = Describe(IniFileParser.Parse(content, options));

        var path = Path.Combine(_directory, $"parse{contentIndex}_{optionsIndex}.ini");
        File.WriteAllText(path, content, new UTF8Encoding(false));

        Assert.Equal(expected, Describe(IniFileParser.Parse(new StringReader(content), options)));
        // Small buffers: the content is parsed line by line while it is read, lines cross the buffer boundaries
        Assert.Equal(expected, Describe(IniFileParser.Parse(new StringReader(content), options, bufferSize: 16)));
        Assert.Equal(expected, Describe(IniFileParser.Parse(new StringReader(content), options, bufferSize: 64)));
        Assert.Equal(expected, Describe(IniFileParser.ParseFile(path, options: options)));
        Assert.Equal(expected, Describe(await IniFileParser.ParseFileAsync(path, options: options)));
    }

    [Fact]
    public void StreamingParse_ThrowsForDuplicates_LikeTheStringParser()
    {
        var options = new IniParserOptions { DuplicateKeyHandling = DuplicateKeyHandling.ThrowError };
        Assert.Throws<InvalidOperationException>(() => IniFileParser.Parse(new StringReader("[A]\nk=1\nk=2\n"), options, bufferSize: 16));
        Assert.Throws<InvalidOperationException>(() => IniFileParser.Parse(new StringReader("[A]\nk=1\n[B]\nx=1\n[A]\nk=2\n"), options, bufferSize: 16));
    }

    [Fact]
    public void ParseTextReader_LeavesTheReaderOpen()
    {
        using var reader = new StringReader("[A]\nk = v\n");
        var file = IniFileParser.Parse(reader);
        Assert.Equal("v", file.GetSection("A")!.GetValue("k"));
        Assert.Equal(-1, reader.Peek());   // a disposed StringReader would throw
    }

    [Fact]
    public void ParseFile_OfALargeFile_DoesNotBufferTheWholeContent()
    {
        var content = LargeContent();
        while (content.Length < 400_000)
            content += content;
        var path = Path.Combine(_directory, "large.ini");
        File.WriteAllText(path, content, Encoding.UTF8);
        var parsedOnly = IniFileParser.Parse(content);

        IniFileParser.ParseFile(path);   // warm up
        var before = GC.GetAllocatedBytesForCurrentThread();
        var parsed = IniFileParser.ParseFile(path);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(Describe(parsedOnly), Describe(parsed));
        // The keys, values and entries themselves need about as much as parsing the string. A buffer for the
        // whole content would be twice the content length (chars) on top of that; streaming only adds the growth
        // of the section dictionaries, which can't be sized in advance.
        var before2 = GC.GetAllocatedBytesForCurrentThread();
        IniFileParser.Parse(content);
        var parseOnlyAllocated = GC.GetAllocatedBytesForCurrentThread() - before2;
        Assert.True(allocated - parseOnlyAllocated < content.Length, $"ParseFile allocated {allocated} bytes, parsing the string {parseOnlyAllocated}");
    }

    // ── Writing ───────────────────────────────────────────────────────────────

    private static IniFile LargeFile()
    {
        var file = IniFileParser.Parse(LargeContent(), new IniParserOptions { LineContinuation = true });
        // Surrogate pairs and multi-byte chars at many positions, so they also end up at block boundaries
        var section = file.GetOrAddSection("Unicode");
        for (var i = 0; i < 2000; i++)
            section.SetValue("Emoji" + i, new string('a', i % 7) + "😀€ä" + i);
        return file;
    }

    public static IEnumerable<object[]> Encodings() => new[]
    {
        new object[] { "utf-8" },
        new object[] { "utf-8-nobom" },
        new object[] { "utf-16" },
    };

    private static Encoding EncodingFor(string name) => name switch
    {
        "utf-8-nobom" => new UTF8Encoding(false),
        "utf-16" => Encoding.Unicode,
        _ => Encoding.UTF8
    };

    [Theory]
    [MemberData(nameof(Encodings))]
    public async Task WriteFile_Sync_And_Async_WriteExactlyTheContentOfWriteToString(string encodingName)
    {
        var encoding = EncodingFor(encodingName);
        var file = LargeFile();
        var options = new IniWriterOptions { QuoteStyle = IniValueQuoteStyle.Auto };
        var expected = encoding.GetPreamble().Concat(encoding.GetBytes(IniFileWriter.WriteToString(file, options))).ToArray();

        var syncPath = Path.Combine(_directory, "sync.ini");
        IniFileWriter.WriteFile(syncPath, file, encoding, options);
        Assert.Equal(expected, File.ReadAllBytes(syncPath));

        var asyncPath = Path.Combine(_directory, "async.ini");
        await IniFileWriter.WriteFileAsync(asyncPath, file, encoding, options);
        Assert.Equal(expected, File.ReadAllBytes(asyncPath));

        // And it reads back
        Assert.Equal("😀€ä1999".PadLeft("😀€ä1999".Length + 1999 % 7, 'a'), IniFileParser.ParseFile(asyncPath, encoding).GetSection("Unicode")!.GetValue("Emoji1999"));
    }

    [Fact]
    public void WriteFile_OfALargeFile_DoesNotCreateTheWholeContent()
    {
        var file = LargeFile();
        var path = Path.Combine(_directory, "large-write.ini");
        IniFileWriter.WriteFile(path, file);   // warm up
        var size = new FileInfo(path).Length;

        var before = GC.GetAllocatedBytesForCurrentThread();
        IniFileWriter.WriteFile(path, file);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        // Without streaming the content is a string (2 bytes per char) or byte array of the whole file;
        // the blocks are pooled, so only the file handling itself allocates.
        Assert.True(allocated < 16 * 1024, $"Writing {size} bytes allocated {allocated} bytes");
    }

    [Fact]
    public void MultiLineComments_AreWrittenAsOneCommentLinePerLine()
    {
        var file = new IniFile();
        var section = new IniSection("A", new[] { "first\r\nsecond\nthird\rfourth\n" });
        section.SetEntry(new IniEntry("k", "v", new[] { "", "one\n\ntwo" }));
        file.AddSection(section);

        var written = IniFileWriter.WriteToString(file).Replace("\r\n", "\n");

        Assert.Equal("; first\n; second\n; third\n; fourth\n; \n[A]\n; \n; one\n; \n; two\nk = v\n", written);
    }

    // ── Converters ────────────────────────────────────────────────────────────

    [Fact]
    public void ListsWithACustomDelimiter_RoundTrip_AndUseRegisteredElementConverters()
    {
        var section = new DelimitedListSettingsImpl();
        section.ResetToDefaults();
        Assert.Equal(new List<int> { 1, 2, 3 }, section.Numbers);
        Assert.Equal(new[] { "a", "b" }, section.Names);

        section.SetRawValue("Numbers", "4; 5 ;6");
        section.SetRawValue("Names", "x | \"y|z\"");
        Assert.Equal(new List<int> { 4, 5, 6 }, section.Numbers);
        Assert.Equal(new[] { "x", "y|z" }, section.Names);

        var raw = section.GetAllRawValues().ToDictionary(k => k.Key, k => k.Value);
        Assert.Equal("4;5;6", raw["Numbers"]);
        Assert.Equal("x|\"y|z\"", raw["Names"]);

        // Registering an element converter replaces the cached list converters
        var original = ValueConverterRegistry.GetConverter(typeof(string))!;
        try
        {
            ValueConverterRegistry.Register(new UpperCaseStringConverter());
            section.SetRawValue("Names", "x|y");
            Assert.Equal(new[] { "X", "Y" }, section.Names);
        }
        finally
        {
            ValueConverterRegistry.Register(original);
        }
        section.SetRawValue("Names", "x|y");
        Assert.Equal(new[] { "x", "y" }, section.Names);
    }

    [Fact]
    public void ConvertingListsWithACustomDelimiter_DoesNotCreateConverters()
    {
        var section = new DelimitedListSettingsImpl();
        section.ResetToDefaults();
        for (var i = 0; i < 5; i++) section.SetRawValue("Numbers", "7;8");   // warm up

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 100; i++)
            section.SetRawValue("Numbers", "7;8");
        var perCall = (GC.GetAllocatedBytesForCurrentThread() - before) / 100;

        // The list, its array, the split parts and the key state; a reflection-created converter adds far more.
        Assert.True(perCall < 400, $"{perCall} bytes per conversion");
        Assert.Equal(new List<int> { 7, 8 }, section.Numbers);
    }

    private sealed class UpperCaseStringConverter : ValueConverterBase<string>
    {
        public override string? ConvertFromString(string? raw, string? defaultValue = default)
            => raw?.ToUpperInvariant() ?? defaultValue;
    }
}
