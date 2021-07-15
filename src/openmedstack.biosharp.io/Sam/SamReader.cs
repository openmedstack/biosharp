namespace OpenMedStack.BioSharp.Io.Sam
{
    using System;
    using System.IO;
    using System.Runtime.CompilerServices;
    using System.Threading;
    using System.Threading.Tasks;
    using Model;

    public class SamReader
    {
        public Task<SamDefinition> Read(
            string filePath,
            Sequence baseSequence,
            CancellationToken cancellationToken = default)
        {
            return Read(filePath, cancellationToken);
        }

        public async Task<SamDefinition> Read(
            string filePath,
            CancellationToken cancellationToken = default)
        {
            await using var file = File.OpenRead(filePath);
            using var reader = new StreamReader(file);
            var sam = new SamDefinition();
            while (file.Position < file.Length)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var line = await reader.ReadLineAsync().ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(line)) { break; }

                if (line.StartsWith('@'))
                {
                    var span = line.AsMemory(1, 2);
                    if ("HD" == span.Span)
                    {
                        sam.Hd = FileMetadata.Parse(line);
                    }
                    else if ("SQ" == span.Span)
                    {
                        sam.Sq = ReferenceSequence.Parse(line);
                    }
                }
            }

            return sam;
        }
    }
}
