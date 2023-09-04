namespace OpenMedStack.BioSharp.Io;

using System;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;

public class BgzfStream : Stream
{
    private const int X64K = 1024 * 64;
    private readonly Stream _innerStream;
    private readonly CompressionMode _mode = CompressionMode.Compress;
    private readonly CompressionLevel _compressionLevel;
    private readonly bool _leaveOpen;
    private readonly byte[] _buffer = new byte[X64K];
    private int _fill;
    private BlockOffsetRecord _blockOffsetRecord;

    public BgzfStream(Stream innerStream, CompressionLevel compressionLevel, bool leaveOpen = true)
    {
        _innerStream = innerStream;
        _compressionLevel = compressionLevel;
        _leaveOpen = leaveOpen;
    }

    public BgzfStream(Stream innerStream, CompressionMode mode, bool leaveOpen = true)
    {
        _innerStream = innerStream;
        _mode = mode;
        _leaveOpen = leaveOpen;
    }

    /// <inheritdoc />
    public override void Flush()
    {
        if (_fill > 0)
        {
            WriteBlock(_buffer.AsMemory(0, _fill));
            _fill = 0;
        }
        _innerStream.Flush();
    }

    /// <inheritdoc />
    public override async Task FlushAsync(CancellationToken cancellationToken)
    {
        if (_fill > 0)
        {
            await WriteBlockAsync(_buffer.AsMemory(0, _fill));
            _fill = 0;
        }
        await _innerStream.FlushAsync(cancellationToken);
    }

    /// <inheritdoc />
    public override int Read(byte[] buffer, int offset, int count)
    {
        if (_mode == CompressionMode.Compress)
        {
            throw new InvalidOperationException("Cannot read while compressing");
        }

        _innerStream.Seek((long)_blockOffsetRecord.BlockAddress, SeekOrigin.Begin);
        using var gzip = new GZipStream(_innerStream, CompressionMode.Decompress, true);
        for (var i = 0; i < _blockOffsetRecord.BlockOffset; i++)
        {
            _ = gzip.ReadByte();
        }

        _blockOffsetRecord = new BlockOffsetRecord();

        var totalRead = 0;
        var pointer = 0;
        while (count > 0)
        {
            if (pointer == X64K)
            {
                pointer = 0;
            }
            var toRead = Math.Min(X64K - pointer, count);
            var read = gzip.Read(buffer, offset, toRead);

            count -= read;
            offset += read;
            pointer += read;
            totalRead += read;
        }

        return totalRead;
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

        _innerStream.Seek((long)_blockOffsetRecord.BlockAddress, SeekOrigin.Begin);
        await using var gzip = new GZipStream(_innerStream, CompressionMode.Decompress, true);
        for (var i = 0; i < _blockOffsetRecord.BlockOffset; i++)
        {
            gzip.ReadByte();
        }

        var offset = 0;
        var count = buffer.Length;
        var totalRead = 0;
        var pointer = 0;
        while (count > 0)
        {
            if (pointer == X64K)
            {
                pointer = 0;
            }
            var toRead = Math.Min(X64K - pointer, count);
            var read = await gzip.ReadAsync(buffer.Slice(offset, toRead), cancellationToken);

            count -= read;
            offset += read;
            pointer += read;
            totalRead += read;
        }

        return totalRead;
    }

    /// <inheritdoc />
    public override long Seek(long offset, SeekOrigin origin)
    {
        if (origin != SeekOrigin.Begin || _mode == CompressionMode.Compress)
        {
            throw new InvalidOperationException("Only seek from begin while reading is supported");
        }

        _blockOffsetRecord = new BlockOffsetRecord((ulong)offset);
        return offset;
    }

    /// <inheritdoc />
    public override void SetLength(long value)
    {
        throw new NotSupportedException();
    }

    /// <inheritdoc />
    public override void Write(byte[] buffer, int offset, int count)
    {
        if (_mode == CompressionMode.Decompress)
        {
            throw new InvalidOperationException("Cannot write while decompressing");
        }
        var (delta, data) = GetMax64K(buffer, offset, count);

        while (data.Length > 0)
        {
            count -= delta;
            offset += delta;
            if (data.Length == X64K)
            {
                WriteBlock(data);
                _fill = 0;
            }
            else if (delta == 0)
            {
                WriteBlock(data);
                break;
            }
            else
            {
                data.CopyTo(_buffer);
                _fill = data.Length;
            }

            (delta, data) = GetMax64K(buffer, offset, count);
            _fill += delta;
        }
    }

    /// <inheritdoc />
    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (_mode == CompressionMode.Decompress)
        {
            throw new InvalidOperationException("Cannot write while decompressing");
        }

        var offset = 0;
        var count = buffer.Length;
        var (delta, data) = GetMax64K(buffer.Span, offset, count);

        while (data.Length > 0)
        {
            count -= delta;
            offset += delta;
            if (data.Length == X64K)
            {
                await WriteBlockAsync(data);
                _fill = 0;
            }
            else if (delta == 0)
            {
                await WriteBlockAsync(data);
                break;
            }
            else
            {
                data.CopyTo(_buffer);
                _fill = data.Length;
            }

            (delta, data) = GetMax64K(buffer.Span, offset, count);
            _fill += delta;
        }
    }

    /// <inheritdoc />
    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        return WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
    }

    private void WriteBlock(Memory<byte> span)
    {
        using var ms = new MemoryStream();
        using (var zip = new GZipStream(ms, _compressionLevel, true))
        {
            zip.Write(span.Span);
        }

        ms.Flush();
        ms.Position = 0;
        ms.CopyTo(_innerStream);
    }

    private async Task WriteBlockAsync(Memory<byte> span)
    {
        await using var ms = new MemoryStream();
        await using (var zip = new GZipStream(ms, _compressionLevel, true))
        {
            await zip.WriteAsync(span);
        }

        await ms.FlushAsync();
        ms.Position = 0;
        await ms.CopyToAsync(_innerStream);
    }

    private (int delta, Memory<byte> data) GetMax64K(ReadOnlySpan<byte> buffer, int offset, int count)
    {
        if (count <= 0)
        {
            return (0, Memory<byte>.Empty);
        }
        var c = Math.Min(count, X64K - _fill);
        buffer.Slice(offset, c).CopyTo(_buffer.AsSpan(_fill, c));
        return (c, _buffer.AsMemory(0, c + _fill));
    }

    /// <inheritdoc />
    public override bool CanRead { get { return _mode == CompressionMode.Decompress; } }

    /// <inheritdoc />
    public override bool CanSeek
    {
        get { return _mode == CompressionMode.Decompress; }
    }

    /// <inheritdoc />
    public override bool CanWrite
    {
        get
        {
            return _mode == CompressionMode.Compress;
        }
    }

    /// <inheritdoc />
    public override long Length
    {
        get
        {
            throw new NotSupportedException();
        }
    }

    public BlockOffsetRecord BlockOffset
    {
        get { return new BlockOffsetRecord((ulong)_innerStream.Position, _fill); }
    }

    /// <inheritdoc />
    public override long Position
    {
        get
        {
            return (long)BlockOffset;
        }
        set
        {
            if (value < 0)
            {
                throw new InvalidOperationException("Invalid position");
            }
            _blockOffsetRecord = new BlockOffsetRecord((ulong)value);
        }
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        Flush();
        if (!_leaveOpen)
        {
            _innerStream.Dispose();
        }
        GC.SuppressFinalize(this);
    }
}
