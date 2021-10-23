namespace OpenMedStack.BioSharp.Model.Bcl
{
    using System;

    public record struct ClusterData
    {
        public ClusterData(
            string barcode,
            Memory<byte> bases,
            Memory<byte> qualities,
            ReadType type,
            int lane,
            int tile,
            IPositionalData position,
            bool pairedEndRead,
            bool filtered,
            int readIndex)
        {
            Barcode = barcode;
            Bases = bases;
            Qualities = qualities;
            Type = type;
            Lane = lane;
            Tile = tile;
            Position = position;
            PairedEndRead = pairedEndRead;
            Filtered = filtered;
            ReadIndex = readIndex;
        }

        public string Barcode { get; }
        public Memory<byte> Bases { get; }
        public Memory<byte> Qualities { get; }
        public ReadType Type { get; }
        public int Lane { get; }
        public int Tile { get; }
        public IPositionalData Position { get; }
        public bool PairedEndRead { get; }
        public bool Filtered { get; }
        public int ReadIndex { get; }

        public string ToSequenceHeader(Run run)
        {
            return ToSequenceHeader(run.Instrument, run.Number, run.Flowcell);
        }

        public string ToSequenceHeader(string instrument, int runNumber, string flowCell)
        {
            return
                $"{instrument}:{runNumber}:{flowCell}:{Lane}:{Tile}:{Position.XCoordinate}:{Position.YCoordinate} {(PairedEndRead ? 2 : 1)}:{(Filtered ? "Y" : "N")}:0:{Barcode}";
        }
    }
}