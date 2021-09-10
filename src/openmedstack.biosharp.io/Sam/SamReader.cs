﻿namespace OpenMedStack.BioSharp.Io.Sam
{
    using System.Buffers.Text;
    using System.Collections.Generic;
    using System.IO;
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

        public async Task<SamDefinition> Read(string filePath, CancellationToken cancellationToken = default)
        {
            var file = File.OpenRead(filePath);
            await using var _ = file.ConfigureAwait(false);
            return await Read(file, cancellationToken).ConfigureAwait(false);
        }

        public async Task<SamDefinition> Read(Stream file, CancellationToken cancellationToken = default)
        {
            using var reader = new StreamReader(file);
            var sq = new List<ReferenceSequence>();
            var alignmentSections = new List<AlignmentSection>();
            FileMetadata fmd = null!;
            Program pg = null!;
            ReadGroup rg = null!;
            while (!reader.EndOfStream)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var line = await reader.ReadLineAsync().ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(line)) { break; }

                if (line[0] == '@')
                {
                    var span = line.Substring(1, 2);
                    switch (span)
                    {
                        case "HD":
                            fmd = FileMetadata.Parse(line);
                            break;
                        case "SQ":
                            sq.Add(ReferenceSequence.Parse(line));
                            break;
                        case "PG":
                            pg = Program.Parse(line);
                            break;
                        case "RG":
                            rg = ReadGroup.Parse(line);
                            break;
                    }
                }
                else
                {
                    alignmentSections.Add(AlignmentSection.Parse(line));
                }
            }

            return new SamDefinition(fmd, sq, rg, pg, alignmentSections);
        }
    }
}
