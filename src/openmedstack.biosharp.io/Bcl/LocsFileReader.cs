namespace OpenMedStack.BioSharp.Io.Bcl
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using Model.Bcl;

    public class LocsFileReader : IAsyncEnumerable<IPositionalData>
    {
        private readonly FileInfo _locsFile;

        public LocsFileReader(FileInfo locsFile)
        {
            _locsFile = locsFile;
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
        }

        public int NumClusters { get; }

        /// <inheritdoc />
        public async IAsyncEnumerator<IPositionalData> GetAsyncEnumerator(CancellationToken cancellationToken = default)
        {
            var file = new FileStream(_locsFile.FullName, FileMode.Open, FileAccess.Read, FileShare.Read);
            await using var _ = file.ConfigureAwait(false);
            var buffer = new byte[8];
            while (true)
            {
                var read = await file.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (read == 8)
                {
                    yield return new PositionalData(
                        BitConverter.ToInt32(buffer.AsSpan(0, 4)),
                        BitConverter.ToInt32(buffer.AsSpan(4, 4)));
                }
                else { yield break; }
            }
        }
    }

}