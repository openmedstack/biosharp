namespace OpenMedStack.BioSharp.Io.FastQ
{
    using System.Collections.Generic;
    using System.IO;
    using System.IO.Compression;
    using System.Runtime.CompilerServices;
    using System.Text;
    using System.Threading;
    using Model;

    public class FastQReader
    {
        public async IAsyncEnumerable<Sequence> Read(string path, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await using var file = File.OpenRead(path);
            await using var gzip = new GZipStream(file, CompressionMode.Decompress, false);
            using var reader = new StreamReader(gzip);
            while (file.Position < file.Length)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var id = await reader.ReadLineAsync().ConfigureAwait(false);
                var letters = await reader.ReadLineAsync().ConfigureAwait(false);

                _ = await reader.ReadLineAsync().ConfigureAwait(false);
                var q = await reader.ReadLineAsync().ConfigureAwait(false);
                var qualities = Encoding.UTF8.GetBytes(q!);
                yield return new Sequence(id![1..], Encoding.ASCII.GetBytes(letters!), qualities);
            }
        }
    }
}
