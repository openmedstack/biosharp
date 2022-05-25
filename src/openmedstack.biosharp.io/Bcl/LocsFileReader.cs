namespace OpenMedStack.BioSharp.Io.Bcl
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using Model.Bcl;

    public class LocsFileReader : ILocationReader
    {
        private readonly Stream _stream;

        public LocsFileReader(FileInfo locsFile)
        {
            _stream = File.Open(
                locsFile.FullName,
                new FileStreamOptions
                {
                    Access = FileAccess.Read,
                    Mode = FileMode.Open,
                    Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
                    Share = FileShare.Read
                });
            var headerBuffer = new byte[12];
            _ = _stream.Read(headerBuffer);
            if (!BitConverter.ToInt32(headerBuffer.AsSpan(0, 4)).Equals(1))
            {
                throw new Exception("Invalid byte 1-4");
            }

            if (!BitConverter.ToSingle(headerBuffer.AsSpan(4, 4)).Equals(1.0f))
            {
                throw new Exception("Invalid version");
            }

            NumClusters = BitConverter.ToInt32(headerBuffer.AsSpan(8, 4));
        }

        public int NumClusters { get; }

        /// <inheritdoc />
        public async IAsyncEnumerator<IPositionalData> GetAsyncEnumerator(CancellationToken cancellationToken = default)
        {
            var buffer = new byte[8];
            while (true)
            {
                var read = await _stream.FillBuffer(buffer, cancellationToken).ConfigureAwait(false);
                //.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (read.Length == 8)
                {
                    var xCoordinate = BitConverter.ToInt32(read.Span[..4]);
                    var yCoordinate = BitConverter.ToInt32(read.Span[4..]);
                    yield return new PositionalData(
                        xCoordinate,
                        yCoordinate);
                }
                else { yield break; }
            }
        }

        /// <inheritdoc />
        public async ValueTask DisposeAsync()
        {
            await _stream.DisposeAsync().ConfigureAwait(false);
            GC.SuppressFinalize(this);
        }
    }

}