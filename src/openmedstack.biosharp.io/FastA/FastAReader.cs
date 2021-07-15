namespace OpenMedStack.BioSharp.Io.FastA
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.IO.Compression;
    using System.Runtime.CompilerServices;
    using System.Text;
    using System.Threading;
    using Model;
    using SharpCompress.Archives.GZip;

    public class FastAReader
    {
        public async IAsyncEnumerable<Sequence> Read(
            string path,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await using var file = File.OpenRead(path);
            await foreach (var sequence in Read(file, cancellationToken))
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
                var letters = new StringBuilder();
                while (!reader.EndOfStream)
                {
                    line = await reader.ReadLineAsync().ConfigureAwait(false);
                    if (line!.StartsWith('>')||string.IsNullOrWhiteSpace(line))
                    {
                        break;
                    }
                    letters.Append(line.AsSpan());
                }

                var data = new byte[letters.Length];
                var index = 0;
                foreach (var chunk in letters.GetChunks())
                {
                    var bytes = encoding.GetBytes(chunk.ToArray());
                    Array.Copy(bytes, 0, data, index, bytes.Length);
                    index += bytes.Length;
                }

                var qualities = new byte[data.Length];
                Array.Fill(qualities, (byte)255);
                yield return new Sequence(id![1..], data, qualities);
            }
        }

        public async IAsyncEnumerable<Sequence> ReadGz(
            string path,
            bool compressedFile = false,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await using var file = File.OpenRead(path);
            await using var zip = new GZipStream(file, CompressionMode.Decompress);
            Stream stream = zip;
            if (compressedFile)
            {
                var ms = new MemoryStream();
                await zip.CopyToAsync(ms, cancellationToken).ConfigureAwait(false);
                await zip.FlushAsync(cancellationToken).ConfigureAwait(false);
                ms.Position = 0;
                stream = ms;
            }

            await foreach (var sequence in ReadGz(stream, compressedFile, cancellationToken))
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
                var archive = GZipArchive.Open(file);

                foreach (var entry in archive.Entries)
                {
                    await using var entryStream = entry.OpenEntryStream();

                    await foreach (var sequence in Read(entryStream, cancellationToken))
                    {
                        yield return sequence;
                    }
                }
            }
            else
            {
                await foreach (var sequence in Read(file, cancellationToken))
                {
                    yield return sequence;
                }
            }
        }
    }
}
