namespace OpenMedStack.BioSharp.Io;

using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

public class BgzfStream : Stream
{
    private static readonly byte[] FixedHeader = { 31, 139, 8, 4, 0, 0, 0, 0, 0, 255, 6, 0, 66, 67, 2, 0 };

    private static readonly byte[] EofSequence =
    {
        0x1f, 0x8b, 0x08, 0x04, 0x00, 0x00, 0x00, 0x00, 0x00, 0xff, 0x06, 0x00, 0x42, 0x43, 0x02, 0x00, 0x1b, 0x00,
        0x03, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00
    };

    private const int X64K = 1024 * 64;
    private readonly CRC32 _crc = new();
    private readonly Stream _innerStream;
    private readonly CompressionMode _mode = CompressionMode.Compress;
    private readonly CompressionLevel _compressionLevel;
    private readonly bool _leaveOpen;
    private readonly byte[] _writeBuffer = Array.Empty<byte>();
    private byte[] _readBuffer = Array.Empty<byte>();
    private int _readPosition;
    private int _fill;
    private bool _endOfFileWritten;
    private BlockOffsetRecord _blockOffsetRecord;

    public BgzfStream(Stream innerStream, CompressionLevel compressionLevel, bool leaveOpen = true)
    {
        _innerStream = innerStream;
        _compressionLevel = compressionLevel;
        _leaveOpen = leaveOpen;
        _writeBuffer = new byte[X64K];
    }

    public BgzfStream(Stream innerStream, CompressionMode mode, bool leaveOpen = true)
    {
        _innerStream = innerStream;
        _mode = mode;
        if (_mode == CompressionMode.Decompress)
        {
            _writeBuffer = new byte[X64K];
        }

        _leaveOpen = leaveOpen;
    }

    /// <inheritdoc />
    public override void Flush()
    {
        if (_fill > 0)
        {
            WriteBlock();
            _fill = 0;
        }

        _innerStream.Flush();
    }

    /// <inheritdoc />
    public override async Task FlushAsync(CancellationToken cancellationToken)
    {
        if (_fill > 0)
        {
            await WriteBlockAsync(cancellationToken).ConfigureAwait(false);
        }

        await _innerStream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public override int Read(Span<byte> buffer)
    {
        if (_mode == CompressionMode.Compress)
        {
            throw new InvalidOperationException("Cannot read while compressing");
        }

        var bufferPosition = 0;
        while (buffer.Length - bufferPosition > 0)
        {
            var remainingInCurrentBlock = _readBuffer.Length - _readPosition;
            if (remainingInCurrentBlock == 0)
            {
                FillReadBuffer();
                continue;
            }

            var toFill = Math.Min(buffer.Length - bufferPosition, remainingInCurrentBlock);
            _readBuffer.AsSpan(_readPosition, toFill).CopyTo(buffer.Slice(bufferPosition, toFill));
            bufferPosition += toFill;
            _readPosition += toFill;
        }

        return buffer.Length;
    }

    /// <inheritdoc />
    public override int Read(byte[] buffer, int offset, int count)
    {
        return Read(buffer.AsSpan(offset, count));
    }

    /// <inheritdoc />
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        return ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
    }

    /// <inheritdoc />
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (_mode == CompressionMode.Compress)
        {
            throw new InvalidOperationException("Cannot read while compressing");
        }

        var bufferPosition = 0;
        while (buffer.Length - bufferPosition > 0)
        {
            var remainingInCurrentBlock = _readBuffer.Length - _readPosition;
            if (remainingInCurrentBlock == 0)
            {
                await FillReadBufferAsync(cancellationToken);
                continue;
            }

            var toFill = Math.Min(buffer.Length - bufferPosition, remainingInCurrentBlock);
            _readBuffer.AsSpan(_readPosition, toFill).CopyTo(buffer.Slice(bufferPosition, toFill).Span);
            bufferPosition += toFill;
            _readPosition += toFill;
        }

        return buffer.Length;
    }

    /// <inheritdoc />
    public override long Seek(long offset, SeekOrigin origin)
    {
        if (origin != SeekOrigin.Begin || _mode == CompressionMode.Compress)
        {
            throw new InvalidOperationException("Only seek from begin while reading is supported");
        }

        var newOffset = new BlockOffsetRecord((ulong)offset);
        if (newOffset != _blockOffsetRecord)
        {
            MoveToOffset(newOffset);
            _blockOffsetRecord = newOffset;
        }

        return (long)_blockOffsetRecord;
    }

    private void MoveToOffset(BlockOffsetRecord offset)
    {
        _innerStream.Seek((long)offset.BlockAddress, SeekOrigin.Begin);

        FillReadBuffer();
        _readPosition = offset.BlockOffset;
    }

    private void FillReadBuffer()
    {
        Span<byte> header = stackalloc byte[16];
        _innerStream.ReadExactly(header);
        using var reader = new BinaryReader(_innerStream, Encoding.UTF8, true);
        var blockSize = reader.ReadUInt16();
        var content = new byte[blockSize - 25];
        _innerStream.ReadExactly(content);
        var targetCrc = reader.ReadUInt32();
        var iSize = reader.ReadUInt32();
        _readBuffer = new byte[iSize];
        using var ms = new MemoryStream(content);
        using var deflate = new DeflateStream(ms, CompressionMode.Decompress);
        deflate.ReadExactly(_readBuffer);
        using var ms2 = new MemoryStream(_readBuffer);
        var c = new CRC32();
        var crc = c.GetCrc32(ms2);
        if (targetCrc != crc)
        {
            throw new Exception("CRC mismatch");
        }

        _readPosition = 0;
    }

    private async Task FillReadBufferAsync(CancellationToken cancellationToken)
    {
        var header = new byte[16];
        await _innerStream.ReadExactlyAsync(header, cancellationToken);
        using var reader = new BinaryReader(_innerStream, Encoding.UTF8, true);
        var blockSize = reader.ReadUInt16();
        var content = new byte[blockSize - 25];
        _innerStream.ReadExactly(content);
        var targetCrc = reader.ReadUInt32();
        var iSize = reader.ReadUInt32();
        _readBuffer = new byte[iSize];
        await using var ms = new MemoryStream(content);
        await using var deflate = new DeflateStream(ms, CompressionMode.Decompress);
        await deflate.ReadExactlyAsync(_readBuffer, cancellationToken);
        await using var ms2 = new MemoryStream(_readBuffer);
        var c = new CRC32();
        var crc = await c.GetCrc32Async(ms2);
        if (targetCrc != crc)
        {
            throw new Exception("CRC mismatch");
        }

        _readPosition = 0;
    }

    /// <inheritdoc />
    public override void SetLength(long value)
    {
        throw new NotSupportedException();
    }

    /// <inheritdoc />
    public override void Write(byte[] buffer, int offset, int count)
    {
        Write(buffer.AsSpan(offset, count));
    }

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        if (_mode == CompressionMode.Decompress)
        {
            throw new InvalidOperationException("Cannot write while decompressing");
        }

        if (buffer.Length == 0)
        {
            return;
        }

        while (true)
        {
            var toCopy = Math.Min(X64K - _fill, buffer.Length);
            buffer[..toCopy].CopyTo(_writeBuffer.AsSpan(_fill, toCopy));
            _fill += toCopy;
            if (toCopy < buffer.Length)
            {
                WriteBlock();
                buffer = buffer[toCopy..];
            }
            else
            {
                _blockOffsetRecord = new BlockOffsetRecord(_blockOffsetRecord.BlockAddress, _fill);
                break;
            }
        }
    }

    /// <inheritdoc />
    public override async ValueTask WriteAsync(
        ReadOnlyMemory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        if (_mode == CompressionMode.Decompress)
        {
            throw new InvalidOperationException("Cannot write while decompressing");
        }

        if (buffer.Length == 0)
        {
            return;
        }

        while (true)
        {
            var toCopy = Math.Min(X64K - _fill, buffer.Length);
            buffer[..toCopy].CopyTo(_writeBuffer.AsMemory(_fill, toCopy));
            _fill += toCopy;
            if (toCopy < buffer.Length)
            {
                await WriteBlockAsync(cancellationToken);
                buffer = buffer[toCopy..];
            }
            else
            {
                _blockOffsetRecord = new BlockOffsetRecord(_blockOffsetRecord.BlockAddress, _fill);
                break;
            }
        }
    }

    /// <inheritdoc />
    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        return WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
    }

    public void WriteEndOfFile()
    {
        if (_endOfFileWritten)
        {
            return;
        }

        _endOfFileWritten = true;
        _innerStream.Write(EofSequence.AsSpan());
    }

    public async ValueTask WriteEndOfFileAsync(CancellationToken cancellationToken)
    {
        if (!_endOfFileWritten && _mode == CompressionMode.Compress)
        {
            await _innerStream.WriteAsync(EofSequence.AsMemory(), cancellationToken);
            _endOfFileWritten = true;
        }
    }

    private void WriteBlock()
    {
        using var ms = new MemoryStream();
        using var zip = new DeflateStream(ms, _compressionLevel, true);
        zip.Write(_writeBuffer.AsSpan(0, _fill));
        zip.Flush();
        uint crc32;
        using (var msCrc = new MemoryStream(_writeBuffer.AsSpan(0, _fill).ToArray()))
        {
            crc32 = _crc.GetCrc32(msCrc);
        }

        ms.Position = 0;
        var msLength = ms.Length;
        _innerStream.Write(FixedHeader.AsSpan());
        using var writer = new BinaryWriter(_innerStream, Encoding.UTF8, true);
        // Block size minus 1
        writer.Write((ushort)(msLength + 25));
        ms.CopyTo(_innerStream);
        writer.Write(crc32);
        writer.Write((uint)_fill);
        _fill = 0;
        Array.Clear(_writeBuffer);
        _blockOffsetRecord = new BlockOffsetRecord(_blockOffsetRecord.BlockAddress + 1, 0);
    }

    private async Task WriteBlockAsync(CancellationToken cancellationToken = default)
    {
        using var ms = new MemoryStream();
        await using var zip = new DeflateStream(ms, _compressionLevel, true);
        await zip.WriteAsync(_writeBuffer.AsMemory(0, _fill), cancellationToken);
        await zip.FlushAsync(cancellationToken);
        uint crc32;
        using (var msCrc = new MemoryStream(_writeBuffer.AsSpan(0, _fill).ToArray()))
        {
            crc32 = await _crc.GetCrc32Async(msCrc);
        }

        ms.Position = 0;
        var msLength = ms.Length;
        _innerStream.Write(FixedHeader.AsSpan());
        await using var writer = new BinaryWriter(_innerStream, Encoding.UTF8, true);
        writer.Write((ushort)(msLength + 25));
        await ms.CopyToAsync(_innerStream, cancellationToken);
        writer.Write(crc32);
        writer.Write((uint)_fill);
        _fill = 0;
        Array.Clear(_writeBuffer);
        _blockOffsetRecord = new BlockOffsetRecord(_blockOffsetRecord.BlockAddress + 1, 0);
    }

    /// <inheritdoc />
    public override bool CanRead
    {
        get { return _mode == CompressionMode.Decompress; }
    }

    /// <inheritdoc />
    public override bool CanSeek
    {
        get { return _mode == CompressionMode.Decompress; }
    }

    /// <inheritdoc />
    public override bool CanWrite
    {
        get { return _mode == CompressionMode.Compress; }
    }

    /// <inheritdoc />
    public override long Length
    {
        get { return _innerStream.Length; }
    }

    public BlockOffsetRecord BlockOffset
    {
        get { return _blockOffsetRecord; }
    }

    /// <inheritdoc />
    public override long Position
    {
        get { return (long)BlockOffset; }
        set
        {
            if (value < 0)
            {
                throw new InvalidOperationException("Invalid position");
            }

            Seek(value, SeekOrigin.Begin);
        }
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        WriteEndOfFile();
        Flush();
        if (!_leaveOpen)
        {
            _innerStream.Dispose();
        }
    }

    public override async ValueTask DisposeAsync()
    {
        await FlushAsync().ConfigureAwait(false);
        await WriteEndOfFileAsync(CancellationToken.None);
        if (!_leaveOpen)
        {
            await _innerStream.DisposeAsync().ConfigureAwait(false);
        }

        GC.SuppressFinalize(this);
    }
}
