namespace OpenMedStack.BioSharp.Io.FastA
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

    public class FastAWriter
    {
        private readonly ILogger _logger;

        public FastAWriter(ILogger logger)
        {
            _logger = logger;
        }

        public Task Write(
            Sequence sequence,
            Stream output,
            byte lineLength = 70,
            CancellationToken cancellationToken = default)
        {
            return Write(new[] {sequence}, output, lineLength, cancellationToken);
        }

        public async Task Write(
            IEnumerable<Sequence> sequences,
            Stream output,
            byte lineLength = 70,
            CancellationToken cancellationToken = default)
        {
            await using var gzip = new GZipStream(output, CompressionLevel.Optimal, true);
            await using var writer = new StreamWriter(gzip, Encoding.UTF8);

            foreach (var sequence in sequences)
            {
                cancellationToken.ThrowIfCancellationRequested();
                _logger.LogInformation("Writing {0} with length {1}", sequence.Id, sequence.Length);
                await writer.WriteLineAsync('>' + sequence.Id).ConfigureAwait(false);

                var letters = new char[sequence.Length];
                for (var i = 0; i < sequence.Length; i++)
                {
                    letters[i] = sequence[i];
                }

                for (var i = 0; i < letters.Length; i += lineLength)
                {
                    var end = Math.Min(letters.Length - i, lineLength);
                    await writer.WriteLineAsync(letters.AsMemory(i, end), cancellationToken).ConfigureAwait(false);
                }
            }

            await writer.FlushAsync().ConfigureAwait(false);
        }
    }
}
