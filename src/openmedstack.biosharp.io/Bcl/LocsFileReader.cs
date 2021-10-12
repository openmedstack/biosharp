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
        private readonly Stream _locsFile = new MemoryStream();

        public LocsFileReader(FileInfo locsFile)
        {
            using var file = File.OpenRead(locsFile.FullName);
            var headerBuffer = new byte[12];
            _ = file.Read(headerBuffer);
            if (!BitConverter.ToInt32(headerBuffer.AsSpan(0, 4)).Equals(1))
            {
                throw new Exception("Invalid byte 1-4");
            }

            if (!BitConverter.ToSingle(headerBuffer.AsSpan(4, 4)).Equals(1.0f))
            {
                throw new Exception("Invalid version");
            }

            NumClusters = BitConverter.ToInt32(headerBuffer.AsSpan(8, 4));
            file.CopyTo(_locsFile);
            _locsFile.Position = 0;
        }

        public int NumClusters { get; }

        /// <inheritdoc />
        public async IAsyncEnumerator<IPositionalData> GetAsyncEnumerator(CancellationToken cancellationToken = default)
        {
            var buffer = new byte[8];
            while (true)
            {
                var read = await _locsFile.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (read == 8)
                {
                    yield return new PositionalData(
                        BitConverter.ToInt32(buffer.AsSpan(0, 4)),
                        BitConverter.ToInt32(buffer.AsSpan(4, 4)));
                }
                else { yield break; }
            }
        }

        /// <inheritdoc />
        public async ValueTask DisposeAsync()
        {
            await _locsFile.DisposeAsync().ConfigureAwait(false);
            GC.SuppressFinalize(this);
        }
    }

}