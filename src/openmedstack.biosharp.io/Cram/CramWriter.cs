namespace OpenMedStack.BioSharp.Io.Cram;

using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Sam;

/// <summary>
/// Writes CRAM v3.0 files from <see cref="AlignmentSection"/> records.
/// Uses EXTERNAL encoding for all data series with GZIP compression.
/// </summary>
public sealed class CramWriter : IAsyncDisposable
{
    private readonly Stream _output;
    private readonly string _referenceFastaPath;
    private readonly bool _leaveOpen;
    private bool _headerWritten;

    // Records buffered for the current container
    private readonly List<AlignmentSection> _pendingRecords = [];
    private const int RecordsPerContainer = 10000;

    public CramWriter(string path, string referenceFastaPath)
    {
        _output = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None,
            bufferSize: 65536, useAsync: true);
        _referenceFastaPath = referenceFastaPath;
        _leaveOpen = false;
    }

    public CramWriter(Stream output, string referenceFastaPath, bool leaveOpen = false)
    {
        _output = output;
        _referenceFastaPath = referenceFastaPath;
        _leaveOpen = leaveOpen;
    }

    /// <summary>
    /// Writes the CRAM file definition and SAM header container.
    /// Must be called before <see cref="WriteAlignment"/>.
    /// </summary>
    public async Task WriteHeader(
        string samHeaderText,
        CancellationToken cancellationToken = default)
    {
        if (_headerWritten)
        {
            throw new InvalidOperationException("Header already written.");
        }

        // 1. File definition: "CRAM" + major(3) + minor(0) + 20-byte file ID
        await _output.WriteAsync(CramEncoding.Magic, cancellationToken).ConfigureAwait(false);
        _output.WriteByte(3); // major version
        _output.WriteByte(0); // minor version

        // 20-byte file identifier (zeros = anonymous)
        var fileId = new byte[20];
        await _output.WriteAsync(fileId, cancellationToken).ConfigureAwait(false);

        // 2. SAM header container
        var headerBytes = Encoding.UTF8.GetBytes(samHeaderText);

        // FILE_HEADER block data: 4-byte length (LE) + header text
        var blockData = new byte[4 + headerBytes.Length];
        BinaryPrimitives.WriteInt32LittleEndian(blockData, headerBytes.Length);
        headerBytes.CopyTo(blockData, 4);

        var fileHeaderBlock = CramBlock.CreateGzip(CramBlock.TypeFileHeader, 0, blockData);

        // Write container header + block
        WriteContainer(
            refSeqId: -1,
            startPos: 0,
            alignmentSpan: 0,
            nRecords: 0,
            recordCounter: 0,
            bases: 0,
            blocks: [fileHeaderBlock]);

        _headerWritten = true;
    }

    /// <summary>Queues an alignment record for writing. Records are flushed per container.</summary>
    public async Task WriteAlignment(
        AlignmentSection alignment,
        CancellationToken cancellationToken = default)
    {
        if (!_headerWritten)
        {
            throw new InvalidOperationException("Call WriteHeaderAsync first.");
        }

        _pendingRecords.Add(alignment);

        if (_pendingRecords.Count >= RecordsPerContainer)
        {
            await FlushContainer(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Flushes any pending records and writes the CRAM EOF container.</summary>
    public async Task Finalize(CancellationToken cancellationToken = default)
    {
        if (!_headerWritten)
        {
            throw new InvalidOperationException("Call WriteHeaderAsync first.");
        }

        if (_pendingRecords.Count > 0)
        {
            await FlushContainer(cancellationToken).ConfigureAwait(false);
        }

        // EOF container
        WriteEofContainer();

        await _output.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task FlushContainer(CancellationToken ct)
    {
        if (_pendingRecords.Count == 0)
        {
            return;
        }

        var records = _pendingRecords.ToArray();
        _pendingRecords.Clear();

        // Determine reference ID and start position from first mapped record
        var firstMapped = Array.Find(records,
            r => !r.Flag.HasFlag(AlignmentSection.AlignmentFlag.SegmentUnmapped));

        var refSeqId = firstMapped != null ? 0 : -1;
        var startPos = firstMapped?.Position ?? 0;

        // Encode all records into external data streams
        await Task.Run(() => EncodeAndWriteContainer(records, refSeqId, startPos), ct).ConfigureAwait(false);
    }

    private void EncodeAndWriteContainer(AlignmentSection[] records, int refSeqId, int startPos)
    {
        // Per-series byte buffers (external blocks)
        using var bfMs = new MemoryStream();   // BAM flags
        using var cfMs = new MemoryStream();   // CRAM flags
        using var riMs = new MemoryStream();   // reference ID
        using var rlMs = new MemoryStream();   // read length
        using var apMs = new MemoryStream();   // alignment position delta
        using var rgMs = new MemoryStream();   // read group
        using var rnMs = new MemoryStream();   // read name (null-terminated)
        using var mfMs = new MemoryStream();   // mate flags
        using var mqMs = new MemoryStream();   // mapping quality
        using var qsMs = new MemoryStream();   // quality scores (raw bytes)
        using var sqMs = new MemoryStream();   // sequence (raw bytes)
        using var tsMs = new MemoryStream();   // template size
        using var nsMs = new MemoryStream();   // next frag ref id
        using var npMs = new MemoryStream();   // next frag position

        var prevPos = startPos;
        long totalBases = 0;
        var maxEnd = startPos;

        foreach (var r in records)
        {
            // BF (BAM flags)
            CramEncoding.WriteItf8(bfMs, (int)r.Flag);

            // CF (CRAM flags): 0 = standard, 4 = sequence stored as byte array
            var cramFlags = 4; // always store sequence externally
            if (r.Quality.Length > 0)
            {
                cramFlags |= 8; // quality stored
            }

            CramEncoding.WriteItf8(cfMs, cramFlags);

            // RI (reference ID)
            CramEncoding.WriteItf8(riMs, r.Flag.HasFlag(AlignmentSection.AlignmentFlag.SegmentUnmapped)
                ? -1
                : 0); // simplified: all mapped reads on ref 0

            // RL (read length)
            CramEncoding.WriteItf8(rlMs, r.Sequence.Length);

            // AP (alignment position delta)
            var posDelta = r.Position - prevPos;
            CramEncoding.WriteItf8(apMs, posDelta);
            if (!r.Flag.HasFlag(AlignmentSection.AlignmentFlag.SegmentUnmapped))
            {
                prevPos = r.Position;
            }

            // RG (read group) — -1 = no group
            CramEncoding.WriteItf8(rgMs, -1);

            // RN (read name, null terminated)
            var rnMaxBytes = Encoding.UTF8.GetMaxByteCount(r.QName.Length);
            var rnBuf = ArrayPool<byte>.Shared.Rent(rnMaxBytes);
            try
            {
                var rnWritten = Encoding.UTF8.GetBytes(r.QName, rnBuf);
                rnMs.Write(rnBuf, 0, rnWritten);
                rnMs.WriteByte(0); // null terminator
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(rnBuf);
            }

            // MF (mate flags) — 0 for single-end
            CramEncoding.WriteItf8(mfMs, 0);

            // NS (next fragment ref id)
            CramEncoding.WriteItf8(nsMs,
                r.ReferenceIdOfNextSegment < 0 ? -1 : r.ReferenceIdOfNextSegment);

            // NP (next fragment position)
            CramEncoding.WriteItf8(npMs, r.NextPosition);

            // TS (template size)
            CramEncoding.WriteItf8(tsMs, r.TemplateLength);

            // MQ (mapping quality)
            CramEncoding.WriteItf8(mqMs, r.MappingQuality);

            // Sequence bytes
            var seqLen = r.Sequence.Length;
            if (seqLen > 0)
            {
                var seqBuf = ArrayPool<byte>.Shared.Rent(seqLen);
                try
                {
                    Encoding.ASCII.GetBytes(r.Sequence, seqBuf);
                    sqMs.Write(seqBuf, 0, seqLen);
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(seqBuf);
                }
            }

            // Quality score bytes (Phred ASCII)
            var qualLen = r.Quality.Length;
            if (qualLen > 0)
            {
                var qualBuf = ArrayPool<byte>.Shared.Rent(qualLen);
                try
                {
                    Encoding.ASCII.GetBytes(r.Quality, qualBuf);
                    qsMs.Write(qualBuf, 0, qualLen);
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(qualBuf);
                }
            }

            totalBases += r.Sequence.Length;
            var end = r.Position + r.Sequence.Length;
            if (end > maxEnd)
            {
                maxEnd = end;
            }
        }

        var alignmentSpan = maxEnd - startPos;

        // Compression header block
        var compressionHeaderBlock = CramCompressionHeader.Build();

        // External data blocks (GZIP-compressed)
        var externalBlocks = new List<CramBlock>
        {
            CramBlock.CreateGzip(CramBlock.TypeExternalData, CramCompressionHeader.ExtIdBf, bfMs.ToArray()),
            CramBlock.CreateGzip(CramBlock.TypeExternalData, CramCompressionHeader.ExtIdCf, cfMs.ToArray()),
            CramBlock.CreateGzip(CramBlock.TypeExternalData, CramCompressionHeader.ExtIdRi, riMs.ToArray()),
            CramBlock.CreateGzip(CramBlock.TypeExternalData, CramCompressionHeader.ExtIdRl, rlMs.ToArray()),
            CramBlock.CreateGzip(CramBlock.TypeExternalData, CramCompressionHeader.ExtIdAp, apMs.ToArray()),
            CramBlock.CreateGzip(CramBlock.TypeExternalData, CramCompressionHeader.ExtIdRg, rgMs.ToArray()),
            CramBlock.CreateGzip(CramBlock.TypeExternalData, CramCompressionHeader.ExtIdRn, rnMs.ToArray()),
            CramBlock.CreateGzip(CramBlock.TypeExternalData, CramCompressionHeader.ExtIdMf, mfMs.ToArray()),
            CramBlock.CreateGzip(CramBlock.TypeExternalData, CramCompressionHeader.ExtIdMq, mqMs.ToArray()),
            CramBlock.CreateGzip(CramBlock.TypeExternalData, CramCompressionHeader.ExtIdBs, sqMs.ToArray()),
            CramBlock.CreateGzip(CramBlock.TypeExternalData, CramCompressionHeader.ExtIdQs, qsMs.ToArray()),
            CramBlock.CreateGzip(CramBlock.TypeExternalData, CramCompressionHeader.ExtIdTs, tsMs.ToArray()),
            CramBlock.CreateGzip(CramBlock.TypeExternalData, CramCompressionHeader.ExtIdNs, nsMs.ToArray()),
            CramBlock.CreateGzip(CramBlock.TypeExternalData, CramCompressionHeader.ExtIdNp, npMs.ToArray()),
        };

        var allBlocks = new List<CramBlock> { compressionHeaderBlock };
        allBlocks.AddRange(externalBlocks);

        WriteContainer(
            refSeqId: refSeqId,
            startPos: startPos,
            alignmentSpan: alignmentSpan,
            nRecords: records.Length,
            recordCounter: 0,
            bases: totalBases,
            blocks: allBlocks);
    }

    private void WriteContainer(
        int refSeqId,
        int startPos,
        int alignmentSpan,
        int nRecords,
        long recordCounter,
        long bases,
        List<CramBlock> blocks)
    {
        // Serialize all blocks to bytes first
        using var blocksMs = new MemoryStream();
        foreach (var block in blocks)
        {
            block.WriteTo(blocksMs);
        }

        var blocksBytes = blocksMs.ToArray();

        // Container header (written first, then we update the length)
        using var headerMs = new MemoryStream();

        CramEncoding.WriteItf8(headerMs, refSeqId);
        CramEncoding.WriteItf8(headerMs, startPos);
        CramEncoding.WriteItf8(headerMs, alignmentSpan);
        CramEncoding.WriteItf8(headerMs, nRecords);
        CramEncoding.WriteLtf8(headerMs, recordCounter);
        CramEncoding.WriteLtf8(headerMs, bases);
        CramEncoding.WriteItf8(headerMs, blocks.Count);

        // Landmarks: one landmark per block (byte offset from start of all blocks)
        CramEncoding.WriteItf8(headerMs, blocks.Count); // l_landmarks count
        var offset = 0;
        foreach (var t in blocks)
        {
            CramEncoding.WriteItf8(headerMs, offset);
            // Calculate block size
            using var tempMs = new MemoryStream();
            t.WriteTo(tempMs);
            offset += (int)tempMs.Length;
        }

        var headerBody = headerMs.ToArray();

        // Container length field: length of header body + blocks
        var containerLength = headerBody.Length + blocksBytes.Length;

        // CRC32 of header body
        var crc = CramEncoding.Crc32(headerBody);

        // Write: container_length (int32 LE) + header body + CRC32 (4 bytes LE) + blocks
        var lenBuf = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(lenBuf, containerLength + 4); // +4 for CRC32
        _output.Write(lenBuf, 0, 4);
        _output.Write(headerBody, 0, headerBody.Length);

        Span<byte> crcBuf = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(crcBuf, crc);
        _output.Write(crcBuf);
        _output.Write(blocksBytes, 0, blocksBytes.Length);
    }

    private void WriteEofContainer()
    {
        // CRAM v3 EOF container: ref_seq_id=-1, all zeros, 0 blocks
        WriteContainer(
            refSeqId: -1,
            startPos: 4542278,
            alignmentSpan: 0,
            nRecords: 0,
            recordCounter: 0,
            bases: 0,
            blocks: []);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_headerWritten)
            {
                await Finalize().ConfigureAwait(false);
            }
        }
        finally
        {
            if (!_leaveOpen)
            {
                await _output.DisposeAsync().ConfigureAwait(false);
            }
        }

        GC.SuppressFinalize(this);
    }
}
