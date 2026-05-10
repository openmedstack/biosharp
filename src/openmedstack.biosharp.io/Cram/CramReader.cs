namespace OpenMedStack.BioSharp.Io.Cram;

using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Sam;

/// <summary>
/// Reads CRAM v3.0 files and yields <see cref="AlignmentSection"/> records.
/// Uses the same interface convention as <see cref="Bam.BamReader"/>.
/// </summary>
public sealed class CramReader : IAsyncDisposable
{
    private readonly Stream _input;
    private readonly bool _leaveOpen;
    // Reusable single-byte buffer for ReadByteAsync — only used from non-concurrent header reads.
    private readonly byte[] _singleByteBuf = new byte[1];

    public CramReader(string path)
    {
        _input = File.Open(path, new FileStreamOptions
        {
            Access = FileAccess.Read,
            Mode = FileMode.Open,
            Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
            Share = FileShare.Read
        });
        _leaveOpen = false;
    }

    public CramReader(Stream input, bool leaveOpen = false)
    {
        _input = input;
        _leaveOpen = leaveOpen;
    }

    /// <summary>
    /// Reads the CRAM file definition and SAM header.
    /// Returns the SAM header text.
    /// </summary>
    public async Task<string> ReadFileHeaderAsync(CancellationToken cancellationToken = default)
    {
        // Read magic (4 bytes) and version using a pooled buffer
        var headerBuf = ArrayPool<byte>.Shared.Rent(24);
        try
        {
            await _input.ReadExactlyAsync(headerBuf.AsMemory(0, 4), cancellationToken).ConfigureAwait(false);
            if (!headerBuf.AsSpan(0, 4).SequenceEqual(CramEncoding.Magic.AsSpan()))
            {
                throw new InvalidDataException("Invalid CRAM magic bytes.");
            }

            // Major + minor version
            var major = await ReadByteAsync(cancellationToken).ConfigureAwait(false);
            var minor = await ReadByteAsync(cancellationToken).ConfigureAwait(false);

            if (major < 3)
            {
                throw new NotSupportedException($"CRAM version {major}.{minor} is not supported (requires 3.0+).");
            }

            // Skip 20-byte file ID
            await _input.ReadExactlyAsync(headerBuf.AsMemory(0, 20), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(headerBuf);
        }

        // Read the SAM header container
        return await ReadSamHeaderContainerAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Streams alignment records from the CRAM file.
    /// <see cref="ReadFileHeaderAsync"/> must be called first.
    /// </summary>
    public async IAsyncEnumerable<AlignmentSection> ReadAlignmentsAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var lenBuf = ArrayPool<byte>.Shared.Rent(4);
        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var read = await TryReadExactlyAsync(lenBuf, 4, cancellationToken).ConfigureAwait(false);
                if (read < 4)
                {
                    yield break;
                }

                var containerLength = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(lenBuf);
                if (containerLength <= 0)
                {
                    yield break; // EOF marker
                }

                var containerData = ArrayPool<byte>.Shared.Rent(containerLength);
                try
                {
                    await _input.ReadExactlyAsync(containerData.AsMemory(0, containerLength), cancellationToken)
                        .ConfigureAwait(false);

                    foreach (var alignment in DecodeContainer(containerData.AsMemory(0, containerLength)))
                    {
                        yield return alignment;
                    }
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(containerData);
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(lenBuf);
        }
    }

    private async Task<string> ReadSamHeaderContainerAsync(CancellationToken ct)
    {
        var lenBuf = ArrayPool<byte>.Shared.Rent(4);
        int containerLength;
        try
        {
            await _input.ReadExactlyAsync(lenBuf.AsMemory(0, 4), ct).ConfigureAwait(false);
            containerLength = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(lenBuf);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(lenBuf);
        }

        if (containerLength <= 0)
        {
            return string.Empty;
        }

        var containerData = ArrayPool<byte>.Shared.Rent(containerLength);
        try
        {
            await _input.ReadExactlyAsync(containerData.AsMemory(0, containerLength), ct).ConfigureAwait(false);

            using var ms = new MemoryStream(containerData, 0, containerLength);

            // Read container header fields
            CramEncoding.ReadItf8(ms); // ref_seq_id
            CramEncoding.ReadItf8(ms); // start_pos
            CramEncoding.ReadItf8(ms); // alignment_span
            CramEncoding.ReadItf8(ms); // n_records
            CramEncoding.ReadLtf8(ms); // record_counter
            CramEncoding.ReadLtf8(ms); // bases
            var nBlocks = CramEncoding.ReadItf8(ms);

            // Skip landmarks
            var lLandmarks = CramEncoding.ReadItf8(ms);
            for (var i = 0; i < lLandmarks; i++)
            {
                CramEncoding.ReadItf8(ms); // landmark offset
            }

            // Skip CRC32 (4 bytes)
            ms.Position += 4;

            // Read blocks
            for (var i = 0; i < nBlocks; i++)
            {
                var block = CramBlock.ReadFrom(ms);
                if (block.ContentType == CramBlock.TypeFileHeader)
                {
                    var data = block.GetDecompressedData();
                    var textLength = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(0, 4));
                    if (textLength > 0 && textLength <= data.Length - 4)
                    {
                        return Encoding.UTF8.GetString(data, 4, textLength);
                    }

                    return string.Empty;
                }
            }

            return string.Empty;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(containerData);
        }
    }

    private static IEnumerable<AlignmentSection> DecodeContainer(ReadOnlyMemory<byte> containerData)
    {
        using var ms = new MemoryStream(containerData.ToArray());

        // Parse container header
        CramEncoding.ReadItf8(ms); // ref_seq_id
        var containerStartPos = CramEncoding.ReadItf8(ms); // start_pos
        CramEncoding.ReadItf8(ms); // alignment_span
        var nRecords = CramEncoding.ReadItf8(ms);
        CramEncoding.ReadLtf8(ms); // record_counter
        CramEncoding.ReadLtf8(ms); // bases
        var nBlocks = CramEncoding.ReadItf8(ms);

        var lLandmarks = CramEncoding.ReadItf8(ms);
        for (var i = 0; i < lLandmarks; i++)
        {
            CramEncoding.ReadItf8(ms); // skip landmark
        }

        // Skip CRC32
        ms.Position += 4;

        if (nRecords == 0)
        {
            yield break;
        }

        // Read all blocks, sorted by content ID
        var externalData = new Dictionary<int, byte[]>();
        for (var i = 0; i < nBlocks; i++)
        {
            var block = CramBlock.ReadFrom(ms);
            if (block.ContentType == CramBlock.TypeExternalData)
            {
                externalData[block.ContentId] = block.GetDecompressedData();
            }
        }

        // Decode records from external data streams
        var streams = new Dictionary<int, MemoryStream>();
        foreach (var (id, data) in externalData)
        {
            streams[id] = new MemoryStream(data);
        }

        try
        {
            var prevPos = containerStartPos;
            for (var recIdx = 0; recIdx < nRecords; recIdx++)
            {
                var flags = (AlignmentSection.AlignmentFlag)ReadItf8FromStream(streams, CramCompressionHeader.ExtIdBf);
                ReadItf8FromStream(streams, CramCompressionHeader.ExtIdCf); // CRAM flags
                var refId = ReadItf8FromStream(streams, CramCompressionHeader.ExtIdRi);
                var readLength = ReadItf8FromStream(streams, CramCompressionHeader.ExtIdRl);
                var posDelta = ReadItf8FromStream(streams, CramCompressionHeader.ExtIdAp);
                ReadItf8FromStream(streams, CramCompressionHeader.ExtIdRg); // read group

                // Read name (null-terminated string)
                var readName = ReadNullTerminatedString(streams, CramCompressionHeader.ExtIdRn);

                ReadItf8FromStream(streams, CramCompressionHeader.ExtIdMf); // mate flags
                ReadItf8FromStream(streams, CramCompressionHeader.ExtIdNs); // next seg ref id
                var nextPos = ReadItf8FromStream(streams, CramCompressionHeader.ExtIdNp);
                var templateLen = ReadItf8FromStream(streams, CramCompressionHeader.ExtIdTs);
                var mapq = (byte)ReadItf8FromStream(streams, CramCompressionHeader.ExtIdMq);

                var pos = prevPos + posDelta;
                if (!flags.HasFlag(AlignmentSection.AlignmentFlag.SegmentUnmapped))
                {
                    prevPos = pos;
                }

                // Sequence bytes
                var seqBytes = ReadBytes(streams, CramCompressionHeader.ExtIdBs, readLength);
                var sequence = Encoding.ASCII.GetString(seqBytes);

                // Quality bytes
                var qualBytes = ReadBytes(streams, CramCompressionHeader.ExtIdQs, readLength);
                var quality = Encoding.ASCII.GetString(qualBytes);

                yield return new AlignmentSection(
                    qname: readName,
                    flag: flags,
                    rname: refId >= 0 ? $"ref{refId}" : "*",
                    position: pos,
                    mapq: mapq,
                    cigar: [],
                    rnext: -1,
                    pnext: nextPos,
                    templatelength: templateLen,
                    sequence: sequence,
                    quality: quality,
                    tags: [],
                    index: recIdx);
            }
        }
        finally
        {
            foreach (var s in streams.Values)
            {
                s.Dispose();
            }
        }
    }

    private static int ReadItf8FromStream(Dictionary<int, MemoryStream> streams, int extId)
    {
        if (!streams.TryGetValue(extId, out var s))
        {
            return 0;
        }

        return CramEncoding.ReadItf8(s);
    }

    private static string ReadNullTerminatedString(Dictionary<int, MemoryStream> streams, int extId)
    {
        if (!streams.TryGetValue(extId, out var s))
        {
            return "";
        }

        var rented = ArrayPool<byte>.Shared.Rent(256);
        var length = 0;
        try
        {
            int b;
            while ((b = s.ReadByte()) > 0)
            {
                if (length == rented.Length)
                {
                    var bigger = ArrayPool<byte>.Shared.Rent(rented.Length * 2);
                    rented.AsSpan(0, length).CopyTo(bigger);
                    ArrayPool<byte>.Shared.Return(rented);
                    rented = bigger;
                }
                rented[length++] = (byte)b;
            }
            return Encoding.UTF8.GetString(rented.AsSpan(0, length));
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    private static byte[] ReadBytes(Dictionary<int, MemoryStream> streams, int extId, int count)
    {
        if (!streams.TryGetValue(extId, out var s))
        {
            return new byte[count];
        }

        var buf = new byte[count];
        var totalRead = 0;
        while (totalRead < count)
        {
            var read = s.Read(buf, totalRead, count - totalRead);
            if (read == 0)
            {
                break;
            }

            totalRead += read;
        }

        return buf;
    }

    private async Task<int> TryReadExactlyAsync(byte[] buffer, int count, CancellationToken ct)
    {
        var totalRead = 0;
        while (totalRead < count)
        {
            var read = await _input.ReadAsync(buffer.AsMemory(totalRead, count - totalRead), ct)
                .ConfigureAwait(false);
            if (read == 0)
            {
                return totalRead;
            }

            totalRead += read;
        }

        return totalRead;
    }

    private async Task<byte> ReadByteAsync(CancellationToken ct)
    {
        await _input.ReadExactlyAsync(_singleByteBuf.AsMemory(0, 1), ct).ConfigureAwait(false);
        return _singleByteBuf[0];
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (!_leaveOpen)
        {
            await _input.DisposeAsync().ConfigureAwait(false);
        }

        GC.SuppressFinalize(this);
    }
}
