namespace OpenMedStack.BioSharp.Io.FastA
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.IO.Compression;
    using System.Linq;
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
            while (!reader.EndOfStream)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var id = string.IsNullOrWhiteSpace(line) ? await reader.ReadLineAsync().ConfigureAwait(false) : line;
                var letters = new List<string>();
                while (!reader.EndOfStream)
                {
                    line = await reader.ReadLineAsync().ConfigureAwait(false);
                    if (line!.StartsWith('>') || string.IsNullOrWhiteSpace(line))
                    {
                        break;
                    }
                    letters.Add(line);
                }

                var data = new byte[letters.Sum(x => x.Length)];
                var index = 0;
                foreach (var chunk in letters)
                {
                    _ = encoding.GetBytes(chunk.AsSpan(), data.AsSpan(index, chunk.Length));
                    index += chunk.Length;
                }

                var qualities = new byte[data.Length];
                Array.Fill(qualities, (byte)255);
                letters.Clear();
                GC.Collect(3, GCCollectionMode.Forced);
                yield return new Sequence(id![1..], data, qualities);
            }
        }

        public async IAsyncEnumerable<Sequence> ReadGz(
            string path,
            bool compressedFile = false,
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
            var zip = new GZipStream(file, CompressionMode.Decompress);
            await using var __ = zip.ConfigureAwait(false);
            Stream stream = zip;
            if (compressedFile)
            {
                var ms = new MemoryStream();
                await zip.CopyToAsync(ms, cancellationToken).ConfigureAwait(false);
                await zip.FlushAsync(cancellationToken).ConfigureAwait(false);
                ms.Position = 0;
                stream = ms;
            }

            await foreach (var sequence in ReadGz(stream, compressedFile, cancellationToken).ConfigureAwait(false))
            {
                yield return sequence;
            }
        }

        public async IAsyncEnumerable<Sequence> ReadGz(
            Stream file,
            bool compressedFile = false,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            if (compressedFile)
            {
                var archive = new ZipArchive(file);

                foreach (var entry in archive.Entries)
                {
                    var entryStream = entry.Open();
                    await using var _ = entryStream.ConfigureAwait(false);

                    await foreach (var sequence in Read(entryStream, cancellationToken).ConfigureAwait(false))
                    {
                        yield return sequence;
                    }
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
}
