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

public class FastAReader
{
    public async IAsyncEnumerable<Sequence> Read(
        string path,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var file = File.Open(
            path,
            new FileStreamOptions
            {
                Access = FileAccess.Read,
                Mode = FileMode.Open,
                Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
                Share = FileShare.Read
            });
        await using var _ = file.ConfigureAwait(false);
        await foreach (var sequence in Read(file, cancellationToken).ConfigureAwait(false))
        {
            yield return sequence;
        }
    }

    public async IAsyncEnumerable<Sequence> Read(
        Stream file,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
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

            line = "";

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
            yield return new Sequence(id[1..], readOnlyMemory, qualities);
        }
    }

    public async IAsyncEnumerable<Sequence> ReadGz(
        string path,
        bool compressedFile = true,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await using var file = File.Open(
            path,
            new FileStreamOptions
            {
                Access = FileAccess.Read,
                Mode = FileMode.Open,
//                    Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
                Share = FileShare.Read
            });
        if (compressedFile)
        {
            await using var zip = new GZipStream(file, CompressionMode.Decompress);
            await foreach (var sequence in Read(zip, cancellationToken).ConfigureAwait(false))
            {
                yield return sequence;
            }
        }
        else
        {
            await foreach (var sequence in ReadGz(file, compressedFile, cancellationToken).ConfigureAwait(false))
            {
                yield return sequence;
            }
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
}
