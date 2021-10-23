namespace OpenMedStack.BioSharp.Io.Bcl
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Runtime.CompilerServices;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Model.Bcl;

    public class SampleReader : IAsyncDisposable
    {
        private readonly int _sample;
        private readonly ILocationReader _positionReader;
        private readonly IEnumerable<bool> _filter;

        public SampleReader(
            int lane,
            int sample,
            BclReader reader,
            ILocationReader positionReader,
            IEnumerable<bool> filter)
        {
            Lane = lane;
            _sample = sample;
            _positionReader = positionReader;
            _filter = filter;
            Reader = reader;
        }

        public BclReader Reader { get; }

        public int Tile => Reader.Tile;

        public int Lane { get; }

        public async IAsyncEnumerable<ClusterData> ReadBclData(
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var positionalEnumerator = _positionReader.GetAsyncEnumerator(cancellationToken);
            await using var positionEnumerator = positionalEnumerator.ConfigureAwait(false);
            var dataReader = (IAsyncEnumerable<ReadData[]>)Reader;
            using var filter = _filter.GetEnumerator();

            await foreach (var data in dataReader.ConfigureAwait(false))
            {
                if (!await positionalEnumerator.MoveNextAsync().ConfigureAwait(false))
                {
                    throw new Exception("Could not read position for sequence read");
                }

                if (!filter.MoveNext())
                {
                    throw new Exception("Could not read filter for cluster");
                }

                var index = string.Join(
                    "-",
                    data.Where(x => x.Type == ReadType.B).Select(x => Encoding.ASCII.GetString(x.Bases.Span)));
                if (string.IsNullOrWhiteSpace(index))
                {
                    index = _sample.ToString();
                }

                var filtered = filter.Current;
                var pairedEndRead = index.Contains('_');
                foreach (var (tile, readType, bytes, qualities, readIndex) in data)
                {
                    yield return new ClusterData(
                        index,
                        bytes,
                        qualities,
                        readType,
                        Lane,
                        tile,
                        positionalEnumerator.Current,
                        pairedEndRead,
                        filtered,
                        readIndex);
                }
            }

            filter.Dispose();
        }

        /// <inheritdoc />
        public async ValueTask DisposeAsync()
        {
            await Reader.DisposeAsync().ConfigureAwait(false);
            await _positionReader.DisposeAsync().ConfigureAwait(false);
            GC.SuppressFinalize(this);
        }
    }
}
