namespace OpenMedStack.BioSharp.Io.FastQ
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.IO.Compression;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Extensions.Logging;
    using Model;

    public class FastQWriter
    {
        private readonly ILogger _logger;

        public FastQWriter(ILogger logger)
        {
            _logger = logger;
        }

        public Task Write(
            Sequence sequence,
            Stream output,
            CancellationToken cancellationToken = default)
        {
            return Write(new[] { sequence }, output, cancellationToken);
        }

        public async Task Write(IEnumerable<Sequence> sequences, Stream output, CancellationToken cancellationToken = default)
        {
            var gzip = new GZipStream(output, CompressionLevel.Optimal, true);
            await using var _ = gzip.ConfigureAwait(false);
            var writer = new StreamWriter(gzip, Encoding.UTF8);
            await using var __ = writer.ConfigureAwait(false);

            foreach (var sequence in sequences)
            {
                _logger.LogInformation("Writing {0} with length {1}", sequence.Id, sequence.Length);
                await writer.WriteAsync('@').ConfigureAwait(false);
                await writer.WriteLineAsync(sequence.Id.AsMemory(), cancellationToken).ConfigureAwait(false);

                var letters = new char[sequence.Length];
                var qualities = new char[sequence.Length];
                var index = 0;
                foreach (var basePair in sequence)
                {
                    letters[index] = basePair.Letter;
                    qualities[index] = (char)basePair.ErrorProbability;
                    index++;
                }

                await writer.WriteLineAsync(letters, cancellationToken).ConfigureAwait(false);
                await writer.WriteAsync('+').ConfigureAwait(false);
                await writer.WriteLineAsync(sequence.Id.AsMemory(), cancellationToken).ConfigureAwait(false);
                await writer.WriteLineAsync(qualities, cancellationToken).ConfigureAwait(false);
            }

            await writer.FlushAsync().ConfigureAwait(false);
        }
    }
}