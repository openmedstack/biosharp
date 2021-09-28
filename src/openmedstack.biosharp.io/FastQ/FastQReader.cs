namespace OpenMedStack.BioSharp.Io.FastQ
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.IO.Compression;
    using System.Runtime.CompilerServices;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Model;

    public class FastQReader
    {
        public async IAsyncEnumerable<Sequence> Read(string path, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var file = File.OpenRead(path);
            await using var _ = file.ConfigureAwait(false);
            var gzip = new GZipStream(file, CompressionMode.Decompress, false);
            await using var __ = gzip.ConfigureAwait(false);
            using var reader = new StreamReader(gzip);
            while (file.Position < file.Length)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var id = await reader.ReadLineAsync().ConfigureAwait(false);
                var letters = await reader.ReadLineAsync().ConfigureAwait(false);

                var q = await reader.ReadLineAsync().ConfigureAwait(false);
                var qualities = Encoding.ASCII.GetBytes(q!);
                var data = Encoding.ASCII.GetBytes(letters!);
                if (qualities.Length == 1 && q == "+")
                {
                    qualities = new byte[data.Length];
                    Array.Fill(qualities, (byte)43);
                }
                yield return new Sequence(id![1..], data, qualities);
            }
        }
    }
}
