namespace OpenMedStack.BioSharp.Io.Bam;

using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

public class BamIndexReader
{
    private static readonly byte[] MagicHeader = { 66, 65, 73, 0x01 };
    private readonly ILogger _logger;

    public BamIndexReader(ILogger logger)
    {
        _logger = logger;
    }

    public async Task<BamIndex> Read(string filePath, CancellationToken cancellationToken = default)
    {
        var file = File.Open(
            filePath,
            new FileStreamOptions
            {
                Access = FileAccess.Read,
                Mode = FileMode.Open,
                Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
                Share = FileShare.Read
            });
        await using var _ = file.ConfigureAwait(false);
        return await Read(file, cancellationToken).ConfigureAwait(false);
    }

    public async Task<BamIndex> Read(Stream file, CancellationToken cancellationToken)
    {
        var buffer = new byte[8];
        var mem = await file.FillBuffer(buffer.AsMemory(0, 4), cancellationToken);
        if (mem.Length != 4 || !mem.Span.SequenceEqual(MagicHeader))
        {
            _logger.LogError("Invalid header: {header}", Encoding.UTF8.GetString(mem.Span));
            throw new InvalidDataException("Invalid Header");
        }

        var referenceCount =
            BitConverter.ToUInt32((await file.FillBuffer(buffer.AsMemory(0, 4), cancellationToken)).Span);
        var content = new SequenceIndex[referenceCount];
        for (var i = 0; i < referenceCount; i++)
        {
            var numberOfBins = BitConverter.ToUInt32((await file.FillBuffer(buffer.AsMemory(0, 4), cancellationToken)).Span);
            var bins = new Bin[numberOfBins];
            for (var j = 0; j < numberOfBins; j++)
            {
                var bin = BitConverter.ToUInt32((await file.FillBuffer(buffer.AsMemory(0, 4), cancellationToken)).Span);
                var numberOfChunks = BitConverter.ToUInt32((await file.FillBuffer(buffer.AsMemory(0, 4), cancellationToken)).Span);
                var chunks = new Chunk[numberOfChunks];
                for (var k = 0; k < numberOfChunks; k++)
                {
                    var begin = BitConverter.ToUInt64((await file.FillBuffer(buffer.AsMemory(0, 8), cancellationToken)).Span);
                    var end = BitConverter.ToUInt64((await file.FillBuffer(buffer.AsMemory(0, 8), cancellationToken)).Span);
                    chunks[k] = new Chunk(begin, end);
                }

                bins[j] = new Bin(bin, chunks);
            }
            var offsetCount = BitConverter.ToUInt32((await file.FillBuffer(buffer.AsMemory(0, 4), cancellationToken)).Span);
            var offsets = new ulong[offsetCount];
            for (var j = 0; j < offsetCount; j++)
            {
                offsets[j] = BitConverter.ToUInt64((await file.FillBuffer(buffer.AsMemory(0, 8), cancellationToken)).Span);
            }

            content[i] = new SequenceIndex(bins, offsets);
        }

        var memory = buffer.AsMemory(0, 8);
        for (var i = 0; i < 8; i++)
        {
            memory.Span[i] = 0;
        }

        var fillBuffer = await file.FillBuffer(memory, true, cancellationToken);
        var numberOfUnmapped = fillBuffer.Length == 0 ? 0 : BitConverter.ToUInt64(fillBuffer.Span);
        return new BamIndex(content, numberOfUnmapped);
    }
}