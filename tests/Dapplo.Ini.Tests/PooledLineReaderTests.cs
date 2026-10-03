// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using System.Threading.Tasks;
using Dapplo.Ini.Parsing;

namespace Dapplo.Ini.Tests;

/// <summary>
/// <see cref="PooledLineReader"/> hands out lines from a pooled buffer; these tests use a tiny buffer so that line
/// breaks, long lines and multi-byte characters cross buffer boundaries.
/// </summary>
public sealed class PooledLineReaderTests
{
    private static List<string> ReadAll(PooledLineReader reader)
    {
        var lines = new List<string>();
        while (true)
        {
            while (reader.TryReadLine(out var line))
                lines.Add(line.ToString());
            if (reader.IsCompleted) break;
            reader.Fill();
        }
        return lines;
    }

    private static async Task<List<string>> ReadAllAsync(PooledLineReader reader)
    {
        var lines = new List<string>();
        while (true)
        {
            Collect(reader, lines);
            if (reader.IsCompleted) break;
            await reader.FillAsync();
        }
        return lines;
    }

    private static void Collect(PooledLineReader reader, List<string> lines)
    {
        while (reader.TryReadLine(out var line))
            lines.Add(line.ToString());
    }

    /// <summary>A stream that returns at most <paramref name="chunk"/> bytes per read, to split characters and line breaks.</summary>
    private sealed class ChunkedStream : MemoryStream
    {
        private readonly int _chunk;
        public ChunkedStream(byte[] data, int chunk) : base(data) => _chunk = chunk;
        public override int Read(byte[] buffer, int offset, int count) => base.Read(buffer, offset, Math.Min(count, _chunk));
        public override int Read(Span<byte> buffer) => base.Read(buffer.Slice(0, Math.Min(buffer.Length, _chunk)));
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, System.Threading.CancellationToken cancellationToken = default)
            => base.ReadAsync(buffer.Slice(0, Math.Min(buffer.Length, _chunk)), cancellationToken);
    }

    public static IEnumerable<object[]> Texts()
    {
        yield return new object[] { "one\r\ntwo\r\nthree", new[] { "one", "two", "three" } };
        yield return new object[] { "one\ntwo\n", new[] { "one", "two" } };
        yield return new object[] { "one\rtwo\r\r\nthree\n\nfour", new[] { "one", "two", "", "three", "", "four" } };
        yield return new object[] { "", Array.Empty<string>() };
        yield return new object[] { "\r\n", new[] { "" } };
        yield return new object[] { "only", new[] { "only" } };
        yield return new object[] { new string('x', 100) + "\r\n" + new string('y', 37), new[] { new string('x', 100), new string('y', 37) } };
        yield return new object[] { "Grüß Gott 😀\r\nÜbersetzt 中文\r\n", new[] { "Grüß Gott 😀", "Übersetzt 中文" } };
    }

    [Theory]
    [MemberData(nameof(Texts))]
    public void TextReader_SmallBuffer_SplitsLinesCorrectly(string text, string[] expected)
    {
        using var reader = new PooledLineReader(new StringReader(text), bufferSize: 16);
        Assert.Equal(expected, ReadAll(reader));
    }

    [Theory]
    [MemberData(nameof(Texts))]
    public void Stream_ChunkedReads_SplitLinesAndCharactersCorrectly(string text, string[] expected)
    {
        for (var chunk = 1; chunk <= 7; chunk++)
        {
            using var reader = new PooledLineReader(new ChunkedStream(Encoding.UTF8.GetBytes(text), chunk), bufferSize: 16);
            Assert.Equal(expected, ReadAll(reader));
        }
    }

    [Theory]
    [MemberData(nameof(Texts))]
    public async Task Stream_Async_SplitsLinesCorrectly(string text, string[] expected)
    {
        using var reader = new PooledLineReader(new ChunkedStream(Encoding.UTF8.GetBytes(text), 3), bufferSize: 16);
        Assert.Equal(expected, await ReadAllAsync(reader));
    }

    [Fact]
    public void ByteOrderMarks_SelectTheEncodingAndAreSkipped()
    {
        const string text = "[Section]\r\nKey=Grüß 😀";
        var expected = new[] { "[Section]", "Key=Grüß 😀" };
        foreach (var encoding in new Encoding[] { new UTF8Encoding(true), new UnicodeEncoding(false, true), new UnicodeEncoding(true, true) })
        {
            var bytes = encoding.GetPreamble().Concat(encoding.GetBytes(text)).ToArray();
            using var reader = new PooledLineReader(new ChunkedStream(bytes, 5), Encoding.UTF8, bufferSize: 16);
            Assert.Equal(expected, ReadAll(reader));
        }
    }

    [Fact]
    public void ReadToEnd_ReturnsTheWholeContent()
    {
        var text = string.Join("\n", Enumerable.Range(0, 500).Select(i => $"Key{i}=Value {i} äöü"));
        using var reader = new PooledLineReader(new ChunkedStream(Encoding.UTF8.GetBytes(text), 100), bufferSize: 16);
        Assert.Equal(text, reader.ReadToEnd().ToString());
    }

    [Fact]
    public void IniFileParser_ParseFile_ReadsUtf8AndUtf16Files()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".ini");
        try
        {
            foreach (var encoding in new Encoding[] { new UTF8Encoding(false), new UTF8Encoding(true), new UnicodeEncoding(false, true) })
            {
                File.WriteAllText(path, "[Main]\r\nName = Grüß 😀\r\nCount = 3", encoding);
                var ini = IniFileParser.ParseFile(path);
                Assert.Equal("Grüß 😀", ini.GetSection("Main")!.GetValue("Name"));
                Assert.Equal("3", ini.GetSection("Main")!.GetValue("Count"));
            }
        }
        finally
        {
            File.Delete(path);
        }
    }
}
