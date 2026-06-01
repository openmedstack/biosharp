using Microsoft.Extensions.Logging;

namespace OpenMedStack.BioSharp.Io.FastA;

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Model;

public partial class FastAReader
{
    private readonly ILogger<FastAReader> _logger;

    public FastAReader(ILogger<FastAReader> logger)
    {
        _logger = logger;
    }

    public async IAsyncEnumerable<Sequence> Read(
        string path,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        LogStartReadingFromPath(path);
        var file = File.Open(
            path,
            new FileStreamOptions
            {
                Access = FileAccess.Read,
                Mode = FileMode.Open,
                //Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
                Share = FileShare.Read
            });
        await using var _ = file.ConfigureAwait(false);
        var isGzip = IsGzip(file);
        // IsGzip reads 2 bytes to inspect the magic number; seek back to start regardless
        // so the subsequent read does not skip the first two characters of the FASTA.
        file.Seek(0, SeekOrigin.Begin);
        if (isGzip)
        {
            await foreach (var sequence in ReadGz(file, true, cancellationToken).ConfigureAwait(false))
            {
                yield return sequence;
            }
        }
        else
        {
            await foreach (var sequence in Read(file, cancellationToken).ConfigureAwait(false))
            {
                yield return sequence;
            }
        }
    }

    private static bool IsGzip(Stream file)
    {
        Span<byte> gzipCheck = stackalloc byte[2];
        file.ReadExactly(gzipCheck);
        var isGzip = gzipCheck.SequenceEqual(new byte[] { 0x1F, 0x8B });
        return isGzip;
    }

    public async IAsyncEnumerable<Sequence> Read(
        Stream file,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        LogStartReadingFromPath("Stream");
        var encoding = Encoding.ASCII;
        using var reader = new StreamReader(file, encoding);

        var line = "";
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var id = string.IsNullOrWhiteSpace(line)
                ? await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false)
                : line;
            if (id == null)
            {
                break;
            }

            var letters = new StringBuilder();
            while (true)
            {
                line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                if (line == null)
                {
                    break;
                }

                if (line.StartsWith('>') || string.IsNullOrWhiteSpace(line))
                {
                    break;
                }

                letters.Append(line);
            }

            var qualities = new char[letters.Length];
            const char defaultQuality = (char)255;
            Array.Fill(qualities, defaultQuality);
            var readOnlyMemory = letters.ToString().AsMemory();
            yield return new Sequence(id.Length > 1 ? id[1..] : "", readOnlyMemory, qualities);
        }
    }

    public async IAsyncEnumerable<Sequence> ReadGz(
        string path,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await using var file = File.Open(
            path,
            new FileStreamOptions
            {
                Access = FileAccess.Read,
                Mode = FileMode.Open,
                //Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
                Share = FileShare.Read
            });

        var doubleCompressed = new GZipStream(file, CompressionMode.Decompress, true);
        var compressedFile = IsGzip(doubleCompressed);
        await doubleCompressed.DisposeAsync();
        file.Seek(0, SeekOrigin.Begin);
        await using var zip = new GZipStream(file, CompressionMode.Decompress);
        await foreach (var sequence in ReadGz(zip, compressedFile, cancellationToken).ConfigureAwait(false))
        {
            yield return sequence;
        }
    }

    public async IAsyncEnumerable<Sequence> ReadGz(
        Stream file,
        bool compressedFile = true,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (compressedFile)
        {
            await using var archive = new GZipStream(file, CompressionMode.Decompress);
            await foreach (var sequence in Read(archive, cancellationToken).ConfigureAwait(false))
            {
                yield return sequence;
            }
        }
        else
        {
            await foreach (var sequence in Read(file, cancellationToken).ConfigureAwait(false))
            {
                yield return sequence;
            }
        }
    }

    [LoggerMessage(LogLevel.Debug, "Start reading from {Path}")]
    partial void LogStartReadingFromPath(string path);
}
