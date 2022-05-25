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
        private readonly SemaphoreSlim _semaphore = new(1);
        private readonly ILogger _logger;
        private readonly GZipStream _gzip;
        private readonly StreamWriter _writer;

        public FastQWriter(ILogger logger, Stream output, CompressionLevel compressionLevel = CompressionLevel.Optimal)
        {
            _logger = logger;
            _gzip = new GZipStream(output, compressionLevel, false);
            _writer = new StreamWriter(_gzip, Encoding.UTF8);
        }

        public async Task Write(
            Sequence sequence,
            CancellationToken cancellationToken = default)
        {
            await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

            await WriteSingle(sequence, cancellationToken).ConfigureAwait(false);
            await _writer.FlushAsync().ConfigureAwait(false);
            _semaphore.Release();
        }

        public async Task Write(IEnumerable<Sequence> sequences, CancellationToken cancellationToken = default)
        {
            await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

            foreach (var sequence in sequences)
            {
                await WriteSingle(sequence, cancellationToken).ConfigureAwait(false);
            }

            await _writer.FlushAsync().ConfigureAwait(false);
            _logger.LogInformation("Flushed to disk");

            _semaphore.Release();
        }

        public async Task Write(IAsyncEnumerable<Sequence> sequences, CancellationToken cancellationToken = default)
        {
            await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            await foreach (var sequence in sequences.WithCancellation(cancellationToken))
            {
                await WriteSingle(sequence, cancellationToken).ConfigureAwait(false);
            }

            await _writer.FlushAsync().ConfigureAwait(false);
            _logger.LogInformation("Flushed to disk");

            _semaphore.Release();
        }

        private async Task WriteSingle(Sequence sequence, CancellationToken cancellationToken)
        {
            await _writer.WriteAsync('@').ConfigureAwait(false);
            await _writer.WriteLineAsync(sequence.Id.AsMemory(), cancellationToken).ConfigureAwait(false);
            await _writer.WriteLineAsync(Encoding.ASCII.GetString(sequence.GetData().Span).AsMemory(), cancellationToken).ConfigureAwait(false);
            await _writer.WriteLineAsync('+').ConfigureAwait(false);
            await _writer.WriteLineAsync(Encoding.ASCII.GetString(sequence.GetQuality().Span).AsMemory(), cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async ValueTask DisposeAsync()
        {
            await _writer.FlushAsync().ConfigureAwait(false);
            _writer.Close();
            await _writer.DisposeAsync().ConfigureAwait(false);
            await _gzip.DisposeAsync().ConfigureAwait(false);
            _semaphore.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}