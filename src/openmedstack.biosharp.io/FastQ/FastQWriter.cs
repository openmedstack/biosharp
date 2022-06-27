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
        private readonly IndexReaderWriter _indexOutput;
        private readonly Func<Sequence, string> _keySelector;
        private readonly BgzfStream _gzip;

        public FastQWriter(ILogger logger, Stream output, Stream indexOutput, CompressionLevel compressionLevel = CompressionLevel.Optimal, Func<Sequence, string>? keySelector = null, bool leaveOpen = false)
        {
            _logger = logger;
            _indexOutput = new IndexReaderWriter(new GZipStream(indexOutput, compressionLevel, leaveOpen), leaveOpen);
            _keySelector = keySelector ?? (s => s.Header.Barcode);
            _gzip = new BgzfStream(output, compressionLevel, leaveOpen);
        }

        public async Task Write(
            Sequence sequence,
            CancellationToken cancellationToken = default)
        {
            await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

            await WriteSingle(sequence, cancellationToken).ConfigureAwait(false);
            await _gzip.FlushAsync(cancellationToken).ConfigureAwait(false);
            _semaphore.Release();
        }

        public async Task Write(IEnumerable<Sequence> sequences, CancellationToken cancellationToken = default)
        {
            await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

            foreach (var sequence in sequences)
            {
                await WriteSingle(sequence, cancellationToken).ConfigureAwait(false);
            }

            await _gzip.FlushAsync(cancellationToken).ConfigureAwait(false);

            _semaphore.Release();
        }

        public async Task<(int sequenceCount, int byteCount)> Write(IAsyncEnumerable<Sequence> sequences, CancellationToken cancellationToken = default)
        {
            var count = 0;
            var byteCount = 0;
            //await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            await foreach (var sequence in sequences.WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                var written = await WriteSingle(sequence, cancellationToken).ConfigureAwait(false);
                byteCount += written;
                Interlocked.Increment(ref count);
            }

            await _gzip.FlushAsync(cancellationToken).ConfigureAwait(false);

            //_semaphore.Release();
            return (count, byteCount);
        }

        private async Task<int> WriteSingle(Sequence sequence, CancellationToken cancellationToken)
        {
            var key = _keySelector(sequence);
            await _indexOutput.Write(key, _gzip.BlockOffset, cancellationToken);
            var data = sequence.GetData();
            var quality = sequence.GetQuality();

            var builder = new StringBuilder();
            builder.Append($"@{sequence.Id}\n");
            builder.Append(data.Span);
            builder.Append('\n');
            builder.Append("+\n");
            builder.Append(quality);
            builder.Append('\n');
            var bytes = Encoding.UTF8.GetBytes(builder.ToString());

            await _gzip.WriteAsync(bytes, cancellationToken);
            //await _writer.WriteAsync('@').ConfigureAwait(false);
            //await _writer.WriteLineAsync(sequence.Id).ConfigureAwait(false);
            //await _writer.WriteLineAsync(data, cancellationToken).ConfigureAwait(false);
            //await _writer.WriteLineAsync('+').ConfigureAwait(false);
            //await _writer.WriteLineAsync(quality, cancellationToken).ConfigureAwait(false);

            return bytes.Length;
        }

        /// <inheritdoc />
        public async ValueTask DisposeAsync()
        {
            await _indexOutput.DisposeAsync().ConfigureAwait(false);
            await _gzip.FlushAsync().ConfigureAwait(false);
            _logger.LogInformation("Flushed and disposed");
            await _gzip.DisposeAsync().ConfigureAwait(false);
            _semaphore.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}