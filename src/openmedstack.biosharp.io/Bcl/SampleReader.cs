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
        public SampleReader(int lane, int sample, int tile, BclReader reader, IAsyncEnumerable<IPositionalData> positionReader, IEnumerable<bool> filter)
        {
            Lane = lane;
            Sample = sample;
            Tile = tile;
            Reader = reader;
            PositionReader = positionReader;
            Filter = filter;
        }

        public int Lane { get; }

        public int Sample { get; }

        public int Tile { get; }
        public BclReader Reader { get; }
        public IAsyncEnumerable<IPositionalData> PositionReader { get; }
        public IEnumerable<bool> Filter { get; }

        public async IAsyncEnumerable<ClusterData> ReadBclData([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var enumerator = PositionReader.GetAsyncEnumerator(cancellationToken);
            await using var positionEnumerator = enumerator.ConfigureAwait(false);
            var enumerable = (IAsyncEnumerable<ReadData[]>)Reader;
            using var filter = Filter.GetEnumerator();
            await foreach (var data in enumerable.ConfigureAwait(false))
            {
                if (!await enumerator.MoveNextAsync().ConfigureAwait(false))
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
                    index = Sample.ToString();
                }

                var filtered = filter.Current;
                var pairedEndRead = index.Contains('_');
                foreach (var (readType, bytes, qualities, readIndex) in data)
                {
                    yield return new ClusterData(
                        index,
                        bytes,
                        Array.ConvertAll(qualities, b => (byte)(b + 33)),
                        readType,
                        Lane,
                        Tile,
                        enumerator.Current,
                        pairedEndRead,
                        filtered,
                        readIndex);
                }
            }
        }
    }
}