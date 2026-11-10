// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Buffers;
using System.Text;

namespace Dapplo.Ini.Parsing;

/// <summary>
/// A <see cref="TextWriter"/> that encodes into a <see cref="Stream"/> in blocks, using pooled buffers only: unlike
/// <see cref="StreamWriter"/> it allocates no buffers per use, and unlike writing a string first the content is
/// never held as a whole (no large object heap allocations for large files).
/// </summary>
/// <remarks>
/// <para>
/// Synchronous use: write, then call <see cref="Complete"/>; a block is written as soon as
/// <see cref="BlockSize"/> chars are buffered. Asynchronous use (<c>synchronousFlush: false</c>): write, call
/// <see cref="FlushBlockAsync"/> when <see cref="IsBlockFull"/>, and <see cref="CompleteAsync"/> at the end. The
/// stream itself is not flushed or disposed.
/// </para>
/// <para>The encoding's preamble (byte order mark) is written first, like <see cref="StreamWriter"/> does for a new file.</para>
/// </remarks>
internal sealed class PooledStreamWriter : TextWriter
{
    /// <summary>The number of buffered chars that are encoded and written as one block.</summary>
    public const int BlockSize = 8192;

    private readonly Stream _stream;
    private readonly Encoding _encoding;
    private readonly Encoder _encoder;
    private readonly bool _synchronousFlush;
    private char[] _chars;
    private byte[] _bytes;
    private int _length;
    private bool _preambleWritten;

    public PooledStreamWriter(Stream stream, Encoding encoding, bool synchronousFlush)
    {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
        _encoding = encoding ?? throw new ArgumentNullException(nameof(encoding));
        _encoder = encoding.GetEncoder();
        _synchronousFlush = synchronousFlush;
        // Room for a block plus the line that completes it, so a block rarely needs a larger buffer.
        _chars = ArrayPool<char>.Shared.Rent(BlockSize * 2);
        _bytes = ArrayPool<byte>.Shared.Rent(BlockSize * 2);
    }

    /// <inheritdoc/>
    public override Encoding Encoding => _encoding;

    /// <summary><c>true</c> when a block is buffered and should be written (asynchronous use).</summary>
    public bool IsBlockFull => _length >= BlockSize;

    public override void Write(char value)
    {
        if (_length == _chars.Length)
            Grow(1);
        _chars[_length++] = value;
        AfterWrite();
    }

    public override void Write(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return;
        var count = value!.Length;
        if (_chars.Length - _length < count)
            Grow(count);
        value.CopyTo(0, _chars, _length, count);
        _length += count;
        AfterWrite();
    }

    public override void Write(char[] buffer, int index, int count)
    {
        if (buffer is null) throw new ArgumentNullException(nameof(buffer));
        if (count <= 0)
            return;
        if (_chars.Length - _length < count)
            Grow(count);
        Array.Copy(buffer, index, _chars, _length, count);
        _length += count;
        AfterWrite();
    }

#if NET
    public override void Write(ReadOnlySpan<char> buffer)
    {
        if (buffer.IsEmpty)
            return;
        if (_chars.Length - _length < buffer.Length)
            Grow(buffer.Length);
        buffer.CopyTo(_chars.AsSpan(_length));
        _length += buffer.Length;
        AfterWrite();
    }
#endif

    public override void WriteLine() => Write(CoreNewLine, 0, CoreNewLine.Length);

    public override void WriteLine(string? value)
    {
        // Without AfterWrite in between: the line and its line break end up in the same block.
        if (!string.IsNullOrEmpty(value))
        {
            var count = value!.Length;
            if (_chars.Length - _length < count)
                Grow(count);
            value.CopyTo(0, _chars, _length, count);
            _length += count;
        }
        Write(CoreNewLine, 0, CoreNewLine.Length);
    }

    private void AfterWrite()
    {
        if (_synchronousFlush && _length >= BlockSize)
            WriteBlock(final: false);
    }

    private void Grow(int additional)
    {
        var larger = ArrayPool<char>.Shared.Rent(Math.Max(_chars.Length * 2, _length + additional));
        Array.Copy(_chars, larger, _length);
        ArrayPool<char>.Shared.Return(_chars);
        _chars = larger;
    }

    /// <summary>Encodes and writes everything that is buffered and ends the encoding (synchronous use).</summary>
    public void Complete() => WriteBlock(final: true);

    /// <summary>Encodes and writes the buffered chars asynchronously.</summary>
    public Task FlushBlockAsync(CancellationToken cancellationToken) => WriteBlockAsync(final: false, cancellationToken);

    /// <summary>Encodes and writes everything that is buffered and ends the encoding (asynchronous use).</summary>
    public Task CompleteAsync(CancellationToken cancellationToken) => WriteBlockAsync(final: true, cancellationToken);

    private void WriteBlock(bool final)
    {
        var byteStart = TakePreamble();
        var charIndex = 0;
        while (true)
        {
            var more = Encode(ref charIndex, final, byteStart, out var byteCount);
            byteStart = 0;
            if (byteCount > 0)
                _stream.Write(_bytes, 0, byteCount);
            if (!more)
                break;
        }
        _length = 0;
    }

    private async Task WriteBlockAsync(bool final, CancellationToken cancellationToken)
    {
        var byteStart = TakePreamble();
        var charIndex = 0;
        while (true)
        {
            var more = Encode(ref charIndex, final, byteStart, out var byteCount);
            byteStart = 0;
            if (byteCount > 0)
            {
#if NET
                await _stream.WriteAsync(_bytes.AsMemory(0, byteCount), cancellationToken).ConfigureAwait(false);
#else
                await _stream.WriteAsync(_bytes, 0, byteCount, cancellationToken).ConfigureAwait(false);
#endif
            }
            if (!more)
                break;
        }
        _length = 0;
    }

    // Encodes the next part of the buffered chars into _bytes after byteStart bytes; byteCount includes those.
    // Returns true while there is more to encode.
    private bool Encode(ref int charIndex, bool final, int byteStart, out int byteCount)
    {
        _encoder.Convert(_chars, charIndex, _length - charIndex, _bytes, byteStart, _bytes.Length - byteStart, final,
            out var charsUsed, out var bytesUsed, out var completed);
        charIndex += charsUsed;
        byteCount = byteStart + bytesUsed;
        return charIndex < _length || (final && !completed);
    }

    // The first block starts with the preamble: copies it to the start of _bytes and returns its length.
    private int TakePreamble()
    {
        if (_preambleWritten)
            return 0;
        _preambleWritten = true;
        var preamble = _encoding.GetPreamble();
        Array.Copy(preamble, _bytes, preamble.Length);
        return preamble.Length;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && _chars.Length > 0)
        {
            ArrayPool<char>.Shared.Return(_chars);
            ArrayPool<byte>.Shared.Return(_bytes);
            _chars = Array.Empty<char>();
            _bytes = Array.Empty<byte>();
            _length = 0;
        }
        base.Dispose(disposing);
    }
}
