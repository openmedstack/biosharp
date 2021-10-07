namespace OpenMedStack.BioSharp.Io.Bcl
{
    using System.Collections.Generic;
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
    }
}