using System.Runtime.CompilerServices;

namespace OpenMedStack.BioSharp.Io.Bam;

using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Sam;

public partial class BamReader
{
    private readonly string? _bamFilePath;
    private SamDefinition? _readDefinition;
    private readonly BgzfStream _stream;
    private readonly ILogger<BamReader> _logger;
    private static readonly byte[] MagicHeader = [66, 65, 77, 0x01];

    public BamReader(string filePath, ILogger<BamReader> logger)
    {
        _logger = logger;
        var file = File.Open(
            filePath,
            new FileStreamOptions
            {
                Access = FileAccess.Read,
                Mode = FileMode.Open,
                Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
                Share = FileShare.Read
            });
        _stream = new BgzfStream(file, CompressionMode.Decompress, false);
        _bamFilePath = filePath;
    }

    public BamReader(Stream stream, ILogger<BamReader> logger)
    {
        _bamFilePath = null!;
        _stream = stream as BgzfStream ?? new BgzfStream(stream, CompressionMode.Decompress, false);
        _logger = logger;
    }

    public async Task<SamDefinition> Read(CancellationToken cancellationToken)
    {
        _logger.LogDebug("Start reading");

        var arrayPool = ArrayPool<byte>.Shared;
        var buffer = arrayPool.Rent(1024);
        var mem = await _stream.FillBuffer(buffer.AsMemory(0, 4), cancellationToken).ConfigureAwait(false);
        if (mem.Length != 4 || !mem.Span.SequenceEqual(MagicHeader))
        {
            throw new InvalidDataException("Invalid Header");
        }

        mem = await _stream.FillBuffer(buffer.AsMemory(0, 4), cancellationToken).ConfigureAwait(false);

        var textLength = (int)BitConverter.ToUInt32(mem.Span);
        if (textLength > buffer.Length)
        {
            arrayPool.Return(buffer);
            buffer = arrayPool.Rent(textLength);
        }

        mem = await _stream.FillBuffer(buffer.AsMemory(0, textLength), cancellationToken).ConfigureAwait(false);

        var text = textLength == 0 ? "" : Encoding.UTF8.GetString(mem[..^1].Span);

        mem = await _stream.FillBuffer(buffer.AsMemory(0, 4), cancellationToken).ConfigureAwait(false);

        var numberOfReferences = BitConverter.ToUInt32(mem.Span);

        var refSeqs = new HashSet<ReferenceSequence>();
        for (var i = 0; i < numberOfReferences; i++)
        {
            var referenceSequence = await ReadReferenceSequence(_stream, buffer, cancellationToken);
            refSeqs.Add(referenceSequence);
        }

        var (fmd, pg, rg, rs) = ReadFileMetadata(text);
        refSeqs.UnionWith(rs);

        if (refSeqs.Count != numberOfReferences)
        {
            throw new InvalidDataException("Inconsistent reference count");
        }

        var alignments = await ReadAlignments(cancellationToken).ToListAsync(cancellationToken);

        _readDefinition = new SamDefinition(fmd, refSeqs, rg, pg, alignments);
        var content = _readDefinition;

        arrayPool.Return(buffer);
        return content;

        async IAsyncEnumerable<AlignmentSection> ReadAlignments([EnumeratorCancellation] CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                mem = await _stream.FillBuffer(buffer.AsMemory(0, 4), true, token)
                    .ConfigureAwait(false);
                if (mem.Length < 4)
                {
                    break;
                }

                var blockSize = (int)BitConverter.ToUInt32(mem.Span);
                var blockBuffer = arrayPool.Rent(blockSize);
                var block = await _stream.FillBuffer(blockBuffer.AsMemory(0, blockSize), token)
                    .ConfigureAwait(false);
                var section = ProcessBlock(block);
                arrayPool.Return(blockBuffer);
                yield return section;
            }
        }
    }

    /// <summary>
    /// Streams alignment sections from the BAM file without materializing the complete SAM definition.
    /// </summary>
    public async IAsyncEnumerable<AlignmentSection> ReadAlignmentSections(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Start streaming BAM alignments");

        var arrayPool = ArrayPool<byte>.Shared;
        var buffer = arrayPool.Rent(1024);
        try
        {
            var mem = await _stream.FillBuffer(buffer.AsMemory(0, 4), cancellationToken).ConfigureAwait(false);
            if (mem.Length != 4 || !mem.Span.SequenceEqual(MagicHeader))
            {
                throw new InvalidDataException("Invalid Header");
            }

            mem = await _stream.FillBuffer(buffer.AsMemory(0, 4), cancellationToken).ConfigureAwait(false);

            var textLength = (int)BitConverter.ToUInt32(mem.Span);
            if (textLength > buffer.Length)
            {
                arrayPool.Return(buffer);
                buffer = arrayPool.Rent(textLength);
            }

            await _stream.FillBuffer(buffer.AsMemory(0, textLength), cancellationToken).ConfigureAwait(false);
            mem = await _stream.FillBuffer(buffer.AsMemory(0, 4), cancellationToken).ConfigureAwait(false);

            var numberOfReferences = BitConverter.ToUInt32(mem.Span);
            for (var referenceIndex = 0; referenceIndex < numberOfReferences; referenceIndex++)
            {
                _ = await ReadReferenceSequence(_stream, buffer, cancellationToken).ConfigureAwait(false);
            }

            while (!cancellationToken.IsCancellationRequested)
            {
                mem = await _stream.FillBuffer(buffer.AsMemory(0, 4), true, cancellationToken)
                    .ConfigureAwait(false);
                if (mem.Length < 4)
                {
                    break;
                }

                var blockSize = (int)BitConverter.ToUInt32(mem.Span);
                var blockBuffer = arrayPool.Rent(blockSize);
                try
                {
                    var block = await _stream.FillBuffer(blockBuffer.AsMemory(0, blockSize), cancellationToken)
                        .ConfigureAwait(false);
                    yield return ProcessBlock(block);
                }
                finally
                {
                    arrayPool.Return(blockBuffer);
                }
            }
        }
        finally
        {
            arrayPool.Return(buffer);
        }
    }

    private static (FileMetadata fmd, Program pg, ReadGroup rg, HashSet<ReferenceSequence> refSeqs)
        ReadFileMetadata(string text)
    {
        FileMetadata fmd = null!;
        Program pg = null!;
        ReadGroup rg = null!;
        HashSet<ReferenceSequence> refSeqs = [];
        if (!string.IsNullOrWhiteSpace(text))
        {
            foreach (var line in text.Split('\n', StringSplitOptions.TrimEntries))
            {
                var span = line.AsSpan(1, 2);
                switch (span)
                {
                    case "HD":
                        fmd = FileMetadata.Parse(line);
                        break;
                    case "SQ":
                        refSeqs.Add(ReferenceSequence.Parse(line));
                        break;
                    case "PG":
                        pg = Program.Parse(line);
                        break;
                    case "RG":
                        rg = ReadGroup.Parse(line);
                        break;
                }
            }
        }
        else
        {
            fmd = new FileMetadata("1.0", FileMetadata.SortingOrder.Unknown, FileMetadata.AlignmentGrouping.None);
            pg = new Program("");
            rg = new ReadGroup("");
        }

        return (fmd, pg, rg, refSeqs);
    }

    private static async Task<ReferenceSequence> ReadReferenceSequence(
        Stream file,
        byte[] buffer,
        CancellationToken cancellationToken)
    {
        var nameLength = (int)BitConverter.ToUInt32(
            (await file.FillBuffer(buffer.AsMemory(0, 4), cancellationToken).ConfigureAwait(false)).Span);

        var name = Encoding.UTF8.GetString(
            (await file.FillBuffer(buffer.AsMemory(0, nameLength), cancellationToken).ConfigureAwait(false))
            .Span[..(nameLength - 1)]);

        var length = await file.FillBuffer(buffer.AsMemory(0, 4), cancellationToken).ConfigureAwait(false);
        var referenceSequence = new ReferenceSequence(name, BitConverter.ToUInt32(length.Span));
        return referenceSequence;
    }

    private static AlignmentSection ProcessBlock(Memory<byte> block)
    {
        var position = BitConverter.ToInt32(block.Slice(4, 4).Span) + 1;
        var nameLength = block.Span[8];
        var mapq = block.Span[9];
        var bamBinary = BitConverter.ToUInt16(block.Slice(10, 2).Span);
        var cigarLength = BitConverter.ToUInt16(block.Slice(12, 2).Span) * 4;
        var alignmentFlag = (AlignmentSection.AlignmentFlag)BitConverter.ToUInt16(block.Slice(14, 2).Span);
        var baseSeqLength = (int)BitConverter.ToUInt32(block.Slice(16, 4).Span);
        var rnext = BitConverter.ToInt32(block.Slice(20, 4).Span);
        var pnext = BitConverter.ToInt32(block.Slice(24, 4).Span) - 1;
        var templateLength = BitConverter.ToInt32(block.Slice(28, 4).Span);
        var qName = Encoding.UTF8.GetString(block.Slice(32, nameLength - 1).Span);
        var sequenceLength = (baseSeqLength + 1) / 2;
        var cigarStart = 32 + nameLength;
        var sequenceStart = cigarStart + cigarLength;
        var qStart = sequenceStart + sequenceLength;
        var tagStart = qStart + baseSeqLength;
        var ops = block.Slice(cigarStart, cigarLength).Span;
        var cigars = new (uint, CigarOp)[cigarLength / 4];

        for (var i = 0; i < cigars.Length; i++)
        {
            var encoded = BitConverter.ToUInt32(ops.Slice(i * 4, 4));
            cigars[i] = encoded.Decode();
        }

        var tags = ReadTags(block[tagStart..]).ToArray();

        var sequence = block.Slice(sequenceStart, sequenceLength).Span.ReadSequence();
        var qualMemory = block.Slice(qStart, baseSeqLength);
        var quality = string.Create(baseSeqLength, qualMemory, static (chars, mem) =>
        {
            var bytes = mem.Span;
            for (var i = 0; i < bytes.Length; i++)
            {
                chars[i] = bytes[i] == 255 ? ' ' : (char)bytes[i];
            }
        });
        return new AlignmentSection(
            qName,
            alignmentFlag,
            qName,
            position,
            mapq,
            cigars,
            rnext,
            pnext,
            templateLength,
            sequence,
            quality,
            tags,
            bamBinary);
    }

    private static IEnumerable<AlignmentTag> ReadTags(Memory<byte> memory)
    {
        var position = 0;
        while (position < memory.Length)
        {
            var slice = memory.Span[position..];
            var key = Encoding.ASCII.GetString(slice[..2]);
            var type = (char)slice[2];
            var (length, value) = ReadTagValue(slice[3..], type);

            position += 3 + length;
            yield return new AlignmentTag(key, type, value);
        }
    }

    private static (int length, object value) ReadTagValue(Span<byte> span, char type)
    {
        switch (type)
        {
            case 'A':
                return (1, (char)span[0]);
            case 'Z':
            {
                var length = span.IndexOf((byte)'\0');
                return (length + 1, Encoding.UTF8.GetString(span[..length]));
            }
            case 'i':
            {
                return (4, BitConverter.ToInt32(span[..4]));
            }
            case 'I':
            {
                return (4, BitConverter.ToUInt32(span[..4]));
            }
            case 's':
            {
                return (2, BitConverter.ToInt16(span[..2]));
            }
            case 'S':
            {
                return (2, BitConverter.ToUInt16(span[..2]));
            }
            case 'c':
            {
                return (1, (sbyte)span[0]);
            }
            case 'C':
            {
                return (1, span[0]);
            }
            case 'f':
            {
                return (4, BitConverter.ToSingle(span[..4]));
            }
            case 'H':
            {
                return (2, Encoding.UTF8.GetString(span[..2]));
            }
            case 'B':
            {
                var subtype = (char)span[0];
                var count = BitConverter.ToInt32(span.Slice(1, 4));
                var total = 5;
                var array = Array.CreateInstance(GetTagType(type), count);
                for (var i = 0; i < count; i++)
                {
                    var (length, value) = ReadTagValue(span[5..], subtype);
                    total += length;
                    array.SetValue(value, i);
                }

                return (total, array);
            }
            default: throw new InvalidDataException($"Invalid tag type: {type}");
        }
    }

    private static Type GetTagType(char type)
    {
        return type switch
        {
            'A' => typeof(char),
            'i' => typeof(int),
            'I' => typeof(uint),
            'f' => typeof(float),
            'Z' => typeof(string),
            'H' => typeof(char[]),
            'c' => typeof(sbyte),
            'C' => typeof(byte),
            's' => typeof(short),
            'S' => typeof(ushort),
            _ => throw new InvalidDataException("Invalid tag value")
        };
    }

    /// <summary>
    /// Queries alignments for a specific genomic region using the BAM index.
    /// Loads the .bai index file from the same directory (same path, .bai extension).
    /// </summary>
    /// <param name="referenceName">Reference sequence name (e.g., "chr1").</param>
    /// <param name="start">0-based start position of the region.</param>
    /// <param name="end">0-based end position of the region.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Alignment sections that overlap the requested region.</returns>
    public async IAsyncEnumerable<AlignmentSection> QueryRegion(
        string referenceName,
        int start,
        int end,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // Load the BAM index. We need the BAM file path to find the .bai next to it.
        // BamReader only holds a stream, not a path. We expose a property for this.
        var bamFilePath = _bamFilePath!;
        if (string.IsNullOrEmpty(bamFilePath) || !File.Exists($"{bamFilePath}.bai"))
        {
            LogNoBamFilePathOrIndexAvailableForRegionQuery();
            yield break;
        }

        var baiFilePath = $"{bamFilePath}.bai";
        var indexReader = new BamIndexReader(_logger);
        var index = await indexReader.Read(baiFilePath, cancellationToken).ConfigureAwait(false);

        // Find the reference index matching the requested name
        var refIndex = await FindReferenceIndex(referenceName, cancellationToken);
        if (refIndex < 0)
        {
            LogReferenceRefnameNotFoundInBamHeader(referenceName);
            yield break;
        }

        if (refIndex >= index.Content.Length)
        {
            LogReferenceIndexRefindexOutOfRangeForBamIndex(refIndex);
            yield break;
        }

        var sequenceIndex = index.Content[refIndex];
        // Reg2Bins returns count; bins are stored in the returned array
        const int maxBinCalc = 60596;
        var binList = new ushort[maxBinCalc];
        var binCount = BamIndexCalculator.Reg2Bins(start, end, binList);
        var bins = binList.Take(binCount).ToArray();

        LogQueryingRegionRefnameStartEndBincountBins(referenceName, start, end, bins.Length);

        // Collect all chunks from matching bins
        var allChunks = new List<(uint bin, Chunk chunk)>();
        foreach (var bin in bins)
        {
            if (bin < sequenceIndex.Content.Length)
            {
                allChunks.AddRange(sequenceIndex.Content[bin].Chunks.Select(chunk => (bin, chunk))
                    .Select(dummy => ((uint bin, Chunk chunk))dummy));
            }
        }

        if (allChunks.Count == 0)
        {
            LogNoIndexEntriesFoundForRegion();
            yield break;
        }

        // Sort chunks by file offset for sequential reading
        allChunks.Sort((a, b) => a.chunk.Begin.BlockAddress.CompareTo(b.chunk.Begin.BlockAddress));

        // Re-open the BAM file for random-access region reads
        await using var fileStream = File.Open(
            bamFilePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read);

        var blockSizeBuf = ArrayPool<byte>.Shared.Rent(4);
        try
        {
            foreach (var (_, chunk) in allChunks)
            {
                cancellationToken.ThrowIfCancellationRequested();

                fileStream.Seek((long)chunk.Begin, SeekOrigin.Begin);

                // Read block size
                var readCount = await fileStream.ReadAsync(blockSizeBuf.AsMemory(0, 4), cancellationToken);
                if (readCount < 4)
                {
                    break;
                }

                var blockSize = (int)BitConverter.ToUInt32(blockSizeBuf.AsSpan(0, 4));
                if (blockSize == 0)
                {
                    continue;
                }

                var blockBuf = ArrayPool<byte>.Shared.Rent(blockSize);
                try
                {
                    var totalRead = 0;
                    while (totalRead < blockSize)
                    {
                        var remaining = blockSize - totalRead;
                        var actuallyRead =
                            await fileStream.ReadAsync(blockBuf.AsMemory(totalRead, Math.Min(remaining, 8192)),
                                cancellationToken);
                        if (actuallyRead == 0)
                        {
                            break;
                        }

                        totalRead += actuallyRead;
                    }

                    if (totalRead < blockSize)
                    {
                        continue;
                    }

                    var block = new ReadOnlyMemory<byte>(blockBuf, 0, totalRead);

                    // Extract position from alignment block
                    if (block.Length < 8)
                    {
                        continue;
                    }

                    var position = BitConverter.ToInt32(block.Slice(4, 4).Span) + 1;

                    // Check if unmapped
                    var alignmentFlag = (AlignmentSection.AlignmentFlag)BitConverter.ToUInt16(block.Slice(14, 2).Span);
                    if ((alignmentFlag & AlignmentSection.AlignmentFlag.SegmentUnmapped) != 0)
                    {
                        continue;
                    }

                    var refConsumed = 0;
                    var nameLen = (int)block.Span[8];
                    var cigarStart = 32 + nameLen;
                    var cigarLength = BitConverter.ToUInt16(block.Slice(12, 2).Span) * 4;
                    var ops = block.Slice(cigarStart, cigarLength).Span;

                    for (var i = 0; i < cigarLength; i += 4)
                    {
                        var encoded = BitConverter.ToUInt32(ops.Slice(i, 4));
                        var count = encoded >> 4;
                        var opcode = (byte)(encoded & 0xf);
                        // M(0), D(7), N(8), =(9), X(10) consume reference
                        if (opcode is 0 or 4 or 7 or 8 or 9 or 10)
                        {
                            refConsumed += (int)count;
                        }
                    }

                    var alignmentEnd = position + refConsumed - 1;

                    // Overlap check: [start, end) overlaps [position, alignmentEnd]
                    if (alignmentEnd >= start && position <= end)
                    {
                        yield return ProcessBlock(block.ToArray().AsMemory());
                    }
                } // end try blockBuf
                finally
                {
                    ArrayPool<byte>.Shared.Return(blockBuf);
                }
            }
        } // end try blockSizeBuf
        finally
        {
            ArrayPool<byte>.Shared.Return(blockSizeBuf);
        }
    }

    /// <summary>
    /// Synchronous wrapper for QueryRegionAsync that reads all matching alignments into a list.
    /// </summary>
    public async Task<List<AlignmentSection>> QueryRegionList(
        string referenceName,
        int start,
        int end,
        CancellationToken cancellationToken = default)
    {
        var results = new List<AlignmentSection>();
        await foreach (var section in QueryRegion(referenceName, start, end, cancellationToken)
            .ConfigureAwait(false))
        {
            results.Add(section);
        }

        return results;
    }

    /// <summary>
    /// Determines the reference index for a given name by reading the BAM header.
    /// </summary>
    private async Task<int> FindReferenceIndex(string referenceName, CancellationToken cancellationToken)
    {
        if (_readDefinition != null)
        {
            var idx = 0;
            foreach (var sq in _readDefinition.Sq)
            {
                if (sq.Name == referenceName)
                {
                    return idx;
                }

                idx++;
            }
        }

        // Fallback: parse the BAM header to find the reference index
        if (!string.IsNullOrEmpty(_bamFilePath))
        {
            await using var headerStream = File.Open(
                _bamFilePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read);

            var header = new BgzfStream(headerStream, CompressionMode.Decompress, false);
            var buf = new byte[1024];
            await header.FillBuffer(buf.AsMemory(0, 4), cancellationToken);
            var textLen = (int)BitConverter.ToUInt32(buf);
            if (textLen is <= 0 or > 100000)
            {
                return -1;
            }

            await header.FillBuffer(buf.AsMemory(0, textLen), cancellationToken);
            var headerText = Encoding.UTF8.GetString(buf[..(textLen - 1)]);

            var refIdx = 0;
            foreach (var line in headerText.Split('\n', StringSplitOptions.TrimEntries))
            {
                if (line.StartsWith("@SQ"))
                {
                    var nameStart = line.IndexOf("SN:", StringComparison.Ordinal);
                    if (nameStart >= 0)
                    {
                        var snParts = line[(nameStart + 3)..].Split('\t');
                        var sn = snParts[0][3..]; // remove "SN:" prefix
                        if (sn == referenceName)
                        {
                            return refIdx;
                        }
                    }

                    refIdx++;
                }
            }
        }

        return -1;
    }

    [LoggerMessage(LogLevel.Warning, "No BAM file path or index available for region query")]
    partial void LogNoBamFilePathOrIndexAvailableForRegionQuery();

    [LoggerMessage(LogLevel.Warning, "Reference '{RefName}' not found in BAM header")]
    partial void LogReferenceRefnameNotFoundInBamHeader(string refName);

    [LoggerMessage(LogLevel.Warning, "Reference index {RefIndex} out of range for BAM index")]
    partial void LogReferenceIndexRefindexOutOfRangeForBamIndex(int refIndex);

    [LoggerMessage(LogLevel.Debug, "Querying region {RefName}:{Start}-{End} — {BinCount} bins")]
    partial void LogQueryingRegionRefnameStartEndBincountBins(string refName, int start, int end, int binCount);

    [LoggerMessage(LogLevel.Debug, "No index entries found for region")]
    partial void LogNoIndexEntriesFoundForRegion();
}
