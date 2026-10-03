// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Buffers;
using System.Text;

namespace Dapplo.Ini.Parsing;

/// <summary>
/// Reads lines from a <see cref="TextReader"/> into a pooled buffer and hands them out as spans, so reading a
/// file allocates neither a string for the whole content nor one per line.
/// </summary>
/// <remarks>
/// <para>
/// A line returned by <see cref="TryReadLine"/> is only valid until the next call to <see cref="TryReadLine"/>,
/// <see cref="Fill"/> or <see cref="FillAsync"/>. Line breaks are <c>\r\n</c>, <c>\n</c> or <c>\r</c>.
/// </para>
/// <para>
/// Usage, the same loop works synchronously (<see cref="Fill"/>) and asynchronously (<see cref="FillAsync"/>):
/// <code>
/// while (true)
/// {
///     while (reader.TryReadLine(out var line)) Process(line);
///     if (reader.IsCompleted) break;
///     reader.Fill();
/// }
/// </code>
/// </para>
/// </remarks>
internal sealed class PooledLineReader : IDisposable
{
    private const int DefaultBufferSize = 4096;

    private readonly TextReader? _reader;
    // Reading a stream directly: bytes are decoded into the char buffer here, so no StreamReader (with its own
    // byte and char buffers) is needed. Both buffers are pooled.
    private readonly Stream? _stream;
    private readonly Encoding? _encoding;
    private Decoder? _decoder;
    private byte[]? _bytes;
    private int _byteStart;
    private int _byteEnd;
    private bool _streamEnded;
    private bool _decoderFlushed;
    private char[] _buffer;
    private int _start;       // first unread char
    private int _end;         // end of the valid data
    private bool _endOfInput; // the reader returned 0
    private bool _skipLineFeed; // the last line ended with '\r' at the end of the buffer

    public PooledLineReader(TextReader reader, int bufferSize = DefaultBufferSize)
    {
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        _buffer = ArrayPool<char>.Shared.Rent(bufferSize);
    }

    /// <summary>
    /// Reads <paramref name="stream"/> (owned, disposed with this reader) decoding it with <paramref name="encoding"/>;
    /// a UTF-8 or UTF-16 byte order mark overrides the encoding and is skipped, like <see cref="StreamReader"/> does.
    /// </summary>
    public PooledLineReader(Stream stream, Encoding? encoding = null, int bufferSize = DefaultBufferSize)
    {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
        _encoding = encoding ?? Encoding.UTF8;
        _bytes = ArrayPool<byte>.Shared.Rent(bufferSize);
        _buffer = ArrayPool<char>.Shared.Rent(bufferSize);
    }

    /// <summary>Opens <paramref name="path"/> for reading (sharing read/write) with BOM detection; UTF-8 by default.</summary>
    public static PooledLineReader OpenFile(string path, Encoding? encoding = null, bool asynchronous = false)
    {
        // bufferSize 1: no FileStream buffer, the reader has its own (pooled) byte buffer.
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, bufferSize: 1,
            asynchronous ? FileOptions.Asynchronous | FileOptions.SequentialScan : FileOptions.SequentialScan);
        return new PooledLineReader(stream, encoding);
    }

    /// <summary><c>true</c> when all lines have been returned.</summary>
    public bool IsCompleted => _endOfInput && _start >= _end;

    /// <summary>
    /// Returns the next complete line from the buffer. Returns <c>false</c> when more data must be read first
    /// (<see cref="Fill"/> / <see cref="FillAsync"/>) or when all lines were returned (<see cref="IsCompleted"/>).
    /// </summary>
    public bool TryReadLine(out ReadOnlySpan<char> line)
    {
        if (_skipLineFeed)
        {
            if (_start < _end)
            {
                if (_buffer[_start] == '\n') _start++;
                _skipLineFeed = false;
            }
            else if (_endOfInput)
            {
                _skipLineFeed = false;
            }
            else
            {
                line = default;
                return false;
            }
        }

        var data = new ReadOnlySpan<char>(_buffer, _start, _end - _start);
        var lineBreak = data.IndexOfAny('\r', '\n');
        if (lineBreak >= 0)
        {
            line = data.Slice(0, lineBreak);
            _start += lineBreak + 1;
            if (data[lineBreak] == '\r')
            {
                if (_start < _end)
                {
                    if (_buffer[_start] == '\n') _start++;
                }
                else
                {
                    _skipLineFeed = true;
                }
            }
            return true;
        }

        if (_endOfInput && !data.IsEmpty)
        {
            // The last line, without line break
            line = data;
            _start = _end;
            return true;
        }

        line = default;
        return false;
    }

    /// <summary>Reads more data. A line longer than the buffer grows it.</summary>
    public void Fill()
    {
        if (!PrepareFill()) return;
        if (_reader != null)
        {
            Filled(_reader.Read(_buffer, _end, _buffer.Length - _end));
            return;
        }

        while (true)
        {
            if (TryDecode()) return;
            Received(_stream!.Read(_bytes!, 0, _bytes!.Length));
        }
    }

    /// <summary>Asynchronously reads more data.</summary>
    public async Task FillAsync(CancellationToken cancellationToken = default)
    {
        if (!PrepareFill()) return;
        if (_reader != null)
        {
#if NET
            var read = await _reader.ReadAsync(_buffer.AsMemory(_end, _buffer.Length - _end), cancellationToken).ConfigureAwait(false);
#else
            var read = await _reader.ReadAsync(_buffer, _end, _buffer.Length - _end).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
#endif
            Filled(read);
            return;
        }

        while (true)
        {
            if (TryDecode()) return;
#if NET
            var received = await _stream!.ReadAsync(_bytes.AsMemory(0, _bytes!.Length), cancellationToken).ConfigureAwait(false);
#else
            var received = await _stream!.ReadAsync(_bytes!, 0, _bytes!.Length, cancellationToken).ConfigureAwait(false);
#endif
            Received(received);
        }
    }

    /// <summary>
    /// Decodes buffered bytes into the char buffer. Returns <c>true</c> when chars were produced or the input ended,
    /// <c>false</c> when more bytes must be read.
    /// </summary>
    private bool TryDecode()
    {
        if (_byteStart < _byteEnd)
        {
            _decoder ??= _encoding!.GetDecoder();
            _decoder.Convert(_bytes!, _byteStart, _byteEnd - _byteStart, _buffer, _end, _buffer.Length - _end,
                flush: false, out var bytesUsed, out var charsUsed, out _);
            _byteStart += bytesUsed;
            if (charsUsed > 0)
            {
                _end += charsUsed;
                return true;
            }
            if (_byteStart < _byteEnd) return false;   // an incomplete character: read more
        }

        if (!_streamEnded) return false;

        if (!_decoderFlushed && _decoder != null)
        {
            _decoderFlushed = true;
            _decoder.Convert(_bytes!, 0, 0, _buffer, _end, _buffer.Length - _end, flush: true, out _, out var flushed, out _);
            if (flushed > 0)
            {
                _end += flushed;
                return true;
            }
        }
        _endOfInput = true;
        return true;
    }

    private void Received(int count)
    {
        if (count <= 0)
        {
            _streamEnded = true;
            return;
        }

        _byteStart = 0;
        _byteEnd = count;
        if (_decoder == null)
            DetectByteOrderMark();
    }

    // First block: a byte order mark selects the encoding (as StreamReader does) and is skipped.
    private void DetectByteOrderMark()
    {
        var bytes = _bytes!;
        Encoding encoding = _encoding!;
        if (_byteEnd >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            encoding = Encoding.UTF8;
            _byteStart = 3;
        }
        else if (_byteEnd >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            encoding = Encoding.Unicode;
            _byteStart = 2;
        }
        else if (_byteEnd >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            encoding = Encoding.BigEndianUnicode;
            _byteStart = 2;
        }
        _decoder = encoding.GetDecoder();
    }

    /// <summary>
    /// Reads everything that is left and returns it as one span (valid until the reader is used or disposed), for
    /// parsers that need the whole content. Uses the pooled buffer instead of a string.
    /// </summary>
    public ReadOnlySpan<char> ReadToEnd()
    {
        while (!_endOfInput)
            Fill();
        return new ReadOnlySpan<char>(_buffer, _start, _end - _start);
    }

    /// <summary>Asynchronously reads everything that is left; then call <see cref="ReadToEnd"/> to get it.</summary>
    public async Task ReadToEndAsync(CancellationToken cancellationToken = default)
    {
        while (!_endOfInput)
            await FillAsync(cancellationToken).ConfigureAwait(false);
    }

    private bool PrepareFill()
    {
        if (_endOfInput) return false;

        // Move the unread rest to the front, grow when a single line fills the whole buffer.
        var remaining = _end - _start;
        if (_start > 0)
        {
            if (remaining > 0)
                Array.Copy(_buffer, _start, _buffer, 0, remaining);
            _start = 0;
            _end = remaining;
        }
        // At least two free chars, so a surrogate pair always fits.
        if (_buffer.Length - _end < 2)
        {
            var larger = ArrayPool<char>.Shared.Rent(_buffer.Length * 2);
            Array.Copy(_buffer, larger, _end);
            ArrayPool<char>.Shared.Return(_buffer);
            _buffer = larger;
        }
        return true;
    }

    private void Filled(int read)
    {
        if (read <= 0)
            _endOfInput = true;
        else
            _end += read;
    }

    public void Dispose()
    {
        _reader?.Dispose();
        _stream?.Dispose();
        if (_bytes != null)
        {
            ArrayPool<byte>.Shared.Return(_bytes);
            _bytes = null;
        }
        var buffer = _buffer;
        _buffer = Array.Empty<char>();
        _start = _end = 0;
        if (buffer.Length > 0)
            ArrayPool<char>.Shared.Return(buffer);
    }
}
