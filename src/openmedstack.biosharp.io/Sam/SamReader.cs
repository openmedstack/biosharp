namespace OpenMedStack.BioSharp.Io.Sam
{
    using System.Collections.Generic;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Extensions.Logging;

    public class SamReader
    {
        private readonly ILogger _logger;

        public SamReader(ILogger logger)
        {
            _logger = logger;
        }
        
        public async Task<SamDefinition> Read(string filePath, CancellationToken cancellationToken = default)
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

        public async Task<SamDefinition> Read(Stream file, CancellationToken cancellationToken = default)
        {
            _logger.LogDebug("Start reading");
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
