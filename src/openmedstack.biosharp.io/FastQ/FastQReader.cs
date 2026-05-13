namespace OpenMedStack.BioSharp.Io.FastQ;

using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Model;

public partial class FastQReader
{
    private enum CompressionKind
    {
        None,
        Gzip,
        Bgzf
    }

    private readonly ILogger _logger;

    public FastQReader(ILogger logger)
    {
        _logger = logger;
    }

    public async IAsyncEnumerable<Sequence> Read(
        string path,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        LogStartReadingFastqFromPath(path);
        await using var file = File.Open(
            path,
            new FileStreamOptions
            {
                Access = FileAccess.Read,
                Mode = FileMode.Open,
                Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
                Share = FileShare.Read
            });

        Stream input = file;
        var compressionKind = await DetectCompression(file, path, cancellationToken).ConfigureAwait(false);
        input = compressionKind switch
        {
            CompressionKind.Bgzf => new BgzfStream(file, CompressionMode.Decompress, leaveOpen: false),
            CompressionKind.Gzip => new GZipStream(file, CompressionMode.Decompress, leaveOpen: false),
            _ => input
        };

        await using var _ = input.ConfigureAwait(false);
        using var reader = new StreamReader(input, Encoding.ASCII, detectEncodingFromByteOrderMarks: false);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var id = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (id == null)
            {
                yield break;
            }

            var letters = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            var separatorOrQuality = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            string? quality;

            if (letters == null || separatorOrQuality == null)
            {
                throw new InvalidDataException($"Incomplete FASTQ record encountered in '{path}'.");
            }

            if (id.Length == 0 || id[0] != '@')
            {
                throw new InvalidDataException($"FASTQ record in '{path}' is missing '@' header prefix.");
            }

            if (separatorOrQuality.Length > 0 && separatorOrQuality[0] == '+')
            {
                quality = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                if (quality == null)
                {
                    throw new InvalidDataException($"Incomplete FASTQ record encountered in '{path}'.");
                }
            }
            else
            {
                quality = separatorOrQuality;
            }

            if (letters.Length != quality.Length)
            {
                throw new InvalidDataException(
                    $"FASTQ record in '{path}' has mismatched sequence and quality lengths.");
            }

            yield return new Sequence(id[1..], letters.AsMemory(), quality.AsMemory());
        }
    }

    /// <summary>
    /// Reads FASTQ records directly from a stream (no file path detection; assumes plain text or pre-decompressed).
    /// </summary>
    public async IAsyncEnumerable<Sequence> Read(
        Stream stream,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var reader = new StreamReader(stream, Encoding.ASCII,
            detectEncodingFromByteOrderMarks: false, leaveOpen: true);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var id = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (id == null)
            {
                yield break;
            }

            if (string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            var letters = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            var separatorOrQuality = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (letters == null || separatorOrQuality == null)
            {
                yield break;
            }

            if (id[0] != '@')
            {
                throw new InvalidDataException("FASTQ record is missing '@' header prefix.");
            }

            string quality;
            if (separatorOrQuality.Length > 0 && separatorOrQuality[0] == '+')
            {
                quality = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false)
                 ?? throw new InvalidDataException("Incomplete FASTQ record.");
            }
            else
            {
                quality = separatorOrQuality;
            }

            if (letters.Length != quality.Length)
            {
                throw new InvalidDataException("FASTQ record has mismatched sequence and quality lengths.");
            }

            yield return new Sequence(id[1..], letters.AsMemory(), quality.AsMemory());
        }
    }

    /// <summary>
    /// Reads FASTQ records from a stream with a background read-ahead buffer.
    /// Decompression and I/O happen on a background task while the consumer processes records.
    /// The output order matches the input order exactly.
    /// </summary>
    /// <param name="stream">Input stream (plain text, assumed not compressed).</param>
    /// <param name="bufferCapacity">Maximum number of pre-parsed records to buffer. Default: 1024.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async IAsyncEnumerable<Sequence> ReadWithReadAhead(
        Stream stream,
        int bufferCapacity = 1024,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var channel = Channel.CreateBounded<Sequence>(new BoundedChannelOptions(bufferCapacity)
        {
            SingleWriter = true,
            SingleReader = true,
            FullMode = BoundedChannelFullMode.Wait
        });

        // Background producer
        var producerTask = Task.Run(async () =>
        {
            try
            {
                await foreach (var seq in Read(stream, cancellationToken).ConfigureAwait(false))
                {
                    await channel.Writer.WriteAsync(seq, cancellationToken).ConfigureAwait(false);
                }
            }
            finally
            {
                channel.Writer.TryComplete();
            }
        }, cancellationToken);

        // Consumer: yield from channel
        await foreach (var seq in channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            yield return seq;
        }

        await producerTask.ConfigureAwait(false);
    }

    private static async Task<CompressionKind> DetectCompression(
        FileStream stream,
        string path,
        CancellationToken cancellationToken)
    {
        if (!stream.CanSeek)
        {
            return path.EndsWith(".gz", StringComparison.OrdinalIgnoreCase)
                ? CompressionKind.Gzip
                : CompressionKind.None;
        }

        var originalPosition = stream.Position;
        var header = ArrayPool<byte>.Shared.Rent(18);
        try
        {
            var bytesRead = await stream.ReadAsync(header.AsMemory(0, 18), cancellationToken).ConfigureAwait(false);
            stream.Position = originalPosition;

            if (bytesRead < 2 || header[0] != 0x1F || header[1] != 0x8B)
            {
                return CompressionKind.None;
            }

            if (bytesRead >= 16 &&
                header[3] == 0x04 &&
                header[12] == 0x42 &&
                header[13] == 0x43 &&
                header[14] == 0x02 &&
                header[15] == 0x00)
            {
                return CompressionKind.Bgzf;
            }

            return CompressionKind.Gzip;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(header);
        }
    }

    /// <summary>
    /// Reads two separate FASTQ files (R1 and R2) as matched read pairs.
    /// Read names are validated to be concordant after stripping '/1', '/2', ' 1:', ' 2:' suffixes.
    /// </summary>
    /// <param name="r1Path">Path to the R1 (forward) FASTQ file.</param>
    /// <param name="r2Path">Path to the R2 (reverse) FASTQ file.</param>
    /// <param name="strict">
    /// When <c>true</c> (default), throws <see cref="InvalidDataException"/> on name mismatch.
    /// When <c>false</c>, logs a warning and continues.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async IAsyncEnumerable<(Sequence R1, Sequence R2)> ReadPaired(
        string r1Path,
        string r2Path,
        bool strict = true,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await using var r1Enum = Read(r1Path, cancellationToken).GetAsyncEnumerator(cancellationToken);
        await using var r2Enum = Read(r2Path, cancellationToken).GetAsyncEnumerator(cancellationToken);

        while (true)
        {
            var r1HasNext = await r1Enum.MoveNextAsync().ConfigureAwait(false);
            var r2HasNext = await r2Enum.MoveNextAsync().ConfigureAwait(false);

            if (!r1HasNext && !r2HasNext)
            {
                yield break;
            }

            if (r1HasNext != r2HasNext)
            {
                throw new InvalidDataException(
                    $"Paired FASTQ files have different numbers of records: '{r1Path}' and '{r2Path}'.");
            }

            var r1 = r1Enum.Current;
            var r2 = r2Enum.Current;

            var r1Name = StripPairSuffix(r1.Id);
            var r2Name = StripPairSuffix(r2.Id);

            if (!string.Equals(r1Name, r2Name, StringComparison.Ordinal))
            {
                if (strict)
                {
                    throw new InvalidDataException(
                        $"Read name mismatch in paired FASTQ: R1='{r1.Id}', R2='{r2.Id}'.");
                }

                _logger.LogWarning("Read name mismatch: R1='{R1}', R2='{R2}'", r1.Id, r2.Id);
            }

            yield return (r1, r2);
        }
    }

    /// <summary>
    /// Reads an interleaved FASTQ file where successive records alternate R1/R2.
    /// Odd-positioned records are R1 and even-positioned records are R2.
    /// Read names are validated to be concordant.
    /// </summary>
    /// <param name="path">Path to the interleaved FASTQ file.</param>
    /// <param name="strict">
    /// When <c>true</c> (default), throws on name mismatch; when <c>false</c>, logs a warning.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async IAsyncEnumerable<(Sequence R1, Sequence R2)> ReadInterleaved(
        string path,
        bool strict = true,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await using var enumerator = Read(path, cancellationToken).GetAsyncEnumerator(cancellationToken);

        while (true)
        {
            var r1HasNext = await enumerator.MoveNextAsync().ConfigureAwait(false);
            if (!r1HasNext)
            {
                yield break;
            }

            var r1 = enumerator.Current;

            var r2HasNext = await enumerator.MoveNextAsync().ConfigureAwait(false);
            if (!r2HasNext)
            {
                throw new InvalidDataException(
                    $"Interleaved FASTQ file '{path}' has an odd number of records (missing R2 mate for '{r1.Id}').");
            }

            var r2 = enumerator.Current;

            var r1Name = StripPairSuffix(r1.Id);
            var r2Name = StripPairSuffix(r2.Id);

            if (!string.Equals(r1Name, r2Name, StringComparison.Ordinal))
            {
                if (strict)
                {
                    throw new InvalidDataException(
                        $"Read name mismatch in interleaved FASTQ '{path}': R1='{r1.Id}', R2='{r2.Id}'.");
                }

                _logger.LogWarning("Read name mismatch in interleaved FASTQ: R1='{R1}', R2='{R2}'", r1.Id, r2.Id);
            }

            yield return (r1, r2);
        }
    }

    /// <summary>
    /// Strips Illumina-style read-pair suffixes from a read name:
    /// '/1', '/2', ' 1:', ' 2:', ' 1 ', ' 2 '.
    /// Returns the base name for concordance checking.
    /// </summary>
    internal static string StripPairSuffix(string name)
    {
        // Strip space-delimited suffix like " 1:N:0:ATCG" or " 2:N:0:ATCG"
        var spaceIdx = name.IndexOf(' ');
        if (spaceIdx > 0)
        {
            var suffix = name.AsSpan(spaceIdx + 1);
            if (suffix.Length >= 2 && (suffix[0] == '1' || suffix[0] == '2') && suffix[1] == ':')
            {
                return name[..spaceIdx];
            }
        }

        // Strip '/1' or '/2' suffix
        if (name.Length > 2 && name[^2] == '/' && (name[^1] == '1' || name[^1] == '2'))
        {
            return name[..^2];
        }

        return name;
    }

    [LoggerMessage(LogLevel.Debug, "Start reading FASTQ from '{Path}'")]
    partial void LogStartReadingFastqFromPath(string path);
}
