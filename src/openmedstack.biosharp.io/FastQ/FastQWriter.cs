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

    public class FastQWriter : IAsyncDisposable
    {
        private readonly ILogger _logger;
        private readonly GZipStream _gzip;
        private readonly StreamWriter _writer;

        public FastQWriter(ILogger logger, Stream output)
        {
            _logger = logger;
            _gzip = new GZipStream(output, CompressionLevel.Optimal, false);
            _writer = new StreamWriter(_gzip, Encoding.UTF8);
        }

        public Task Write(
            Sequence sequence,
            CancellationToken cancellationToken = default)
        {
            return WriteSingle(sequence, cancellationToken);
        }

        public async Task Write(IEnumerable<Sequence> sequences, CancellationToken cancellationToken = default)
        {
            foreach (var sequence in sequences)
            {
                await WriteSingle(sequence, cancellationToken).ConfigureAwait(false);
            }

            await _writer.FlushAsync().ConfigureAwait(false);
        }

        public async Task Write(IAsyncEnumerable<Sequence> sequences, CancellationToken cancellationToken = default)
        {
            await foreach (var sequence in sequences.WithCancellation(cancellationToken))
            {
                await WriteSingle(sequence, cancellationToken).ConfigureAwait(false);
            }

            await _writer.FlushAsync().ConfigureAwait(false);
        }

        private async Task WriteSingle(Sequence sequence, CancellationToken cancellationToken)
        {
            _logger.LogInformation("Writing {0} with length {1}", sequence.Id, sequence.Length);

            await _writer.WriteAsync('@').ConfigureAwait(false);
            await _writer.WriteLineAsync(sequence.Id.AsMemory(), cancellationToken).ConfigureAwait(false);
            await _writer.WriteLineAsync(Encoding.ASCII.GetString(sequence.GetData().Span).AsMemory(), cancellationToken).ConfigureAwait(false);
            await _writer.WriteLineAsync('+').ConfigureAwait(false);
            await _writer.WriteLineAsync(Encoding.ASCII.GetString(sequence.GetQuality().Span).AsMemory(), cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async ValueTask DisposeAsync()
        {
            _writer.Close();
            await _writer.DisposeAsync().ConfigureAwait(false);
            await _gzip.DisposeAsync().ConfigureAwait(false);

            GC.SuppressFinalize(this);
        }
    }
}