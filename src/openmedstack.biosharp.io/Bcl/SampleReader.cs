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

    public class SampleReader
    {
        private readonly int _lane;
        private readonly int _sample;
        private readonly IAsyncEnumerable<IPositionalData> _positionReader;
        private readonly IEnumerable<bool> _filter;

        public SampleReader(
            int lane,
            int sample,
            BclReader reader,
            IAsyncEnumerable<IPositionalData> positionReader,
            IEnumerable<bool> filter)
        {
            _lane = lane;
            _sample = sample;
            _positionReader = positionReader;
            _filter = filter;
            Reader = reader;
        }

        public BclReader Reader { get; }

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
                    data.Where(x => x.Type == ReadType.B).Select(x => Encoding.ASCII.GetString(x.Bases)));
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
                        Array.ConvertAll(qualities, b => (byte)(b + 33)),
                        readType,
                        _lane,
                        tile,
                        positionalEnumerator.Current,
                        pairedEndRead,
                        filtered,
                        readIndex);
                }
            }

            filter.Dispose();
        }
    }
}
