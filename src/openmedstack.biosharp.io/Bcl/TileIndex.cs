namespace OpenMedStack.BioSharp.Io.Bcl
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;

    /**
     * Load a file containing 8-byte records like this:
     * tile number: 4-byte int
     * number of clusters in tile: 4-byte int
     * Number of records to read is determined by reaching EOF.
     */
    public class TileIndex : IAsyncEnumerable<TileIndexRecord>
    {
        private readonly FileInfo _tileIndexFile;

        public TileIndex(FileInfo tileIndexFile)
        {
            _tileIndexFile = tileIndexFile;
        }

        /// <inheritdoc />
        public async IAsyncEnumerator<TileIndexRecord> GetAsyncEnumerator(CancellationToken cancellationToken = default)
        {
            var input = File.Open(
                _tileIndexFile.FullName,
                new FileStreamOptions
                {
                    Access = FileAccess.Read,
                    Mode = FileMode.Open,
                    Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
                    Share = FileShare.Read
                });
            await using var _ = input.ConfigureAwait(false);
            var buf = new byte[8];

            var absoluteRecordIndex = 0;
            var numTiles = 0;
            while (buf.Length == await input.ReadAsync(buf, cancellationToken).ConfigureAwait(false))
            {
                var tile = BitConverter.ToInt32(buf.AsSpan(0, 4));

                // Note: not handling unsigned ints > 2^31, but could if one of these exceptions is thrown.
                if (tile < 0)
                {
                    throw new Exception("Tile number too large in " + _tileIndexFile.FullName);
                }

                var numClusters = BitConverter.ToInt32(buf.AsSpan(4, 4));
                if (numClusters < 0)
                {
                    throw new Exception("Cluster size too large in " + _tileIndexFile.FullName);
                }

                yield return new TileIndexRecord(tile, numClusters, absoluteRecordIndex, numTiles++);

                absoluteRecordIndex += numClusters;
            }
        }

    }
}
