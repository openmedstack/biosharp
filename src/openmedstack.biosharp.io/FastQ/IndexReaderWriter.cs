namespace OpenMedStack.BioSharp.Io.FastQ;

using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

public class IndexReaderWriter : IWriteFastQIndex, IReadFastQIndex, IAsyncDisposable
{
    private readonly Stream _stream;
    private readonly bool _leaveOpen;

    public IndexReaderWriter(Stream stream, bool leaveOpen = false)
    {
        _stream = stream;
        _leaveOpen = leaveOpen;
    }

    /// <inheritdoc />
    public async Task Write(string key, BlockOffsetRecord blockOffset, CancellationToken cancellationToken = default)
    {
        var keyBytes = Encoding.UTF8.GetBytes(key);
        await _stream.WriteAsync(BitConverter.GetBytes(keyBytes.Length), cancellationToken);
        await _stream.WriteAsync(keyBytes, cancellationToken);
        await _stream.WriteAsync(BitConverter.GetBytes(blockOffset), cancellationToken);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<(string key, BlockOffsetRecord offset)> Read(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(256);
        while (true)
        {
            var lengthBuffer = await _stream.FillBuffer(buffer.AsMemory(0, 4), true, cancellationToken);
            if (lengthBuffer.Length == 0)
            {
                break;
            }

            var keyLength = BitConverter.ToInt32(lengthBuffer.Span);
            var keyBuffer = await _stream.FillBuffer(buffer.AsMemory(0, keyLength), cancellationToken);
            var key = Encoding.UTF8.GetString(keyBuffer.Span);
            var offsetBuffer = await _stream.FillBuffer(buffer.AsMemory(0, 8), cancellationToken);
            var offset = BitConverter.ToUInt64(offsetBuffer.Span);

            yield return (key, offset);
        }

        ArrayPool<byte>.Shared.Return(buffer);
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        return _leaveOpen ? ValueTask.CompletedTask : _stream.DisposeAsync();
    }
}