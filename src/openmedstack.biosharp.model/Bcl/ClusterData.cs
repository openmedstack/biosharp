namespace OpenMedStack.BioSharp.Model.Bcl
{
    using System;

    public record struct ClusterData(string Barcode,
        Memory<byte> Bases,
        Memory<byte> Qualities,
        ReadType Type,
        int Lane,
        int Tile,
        IPositionalData Position,
        bool PairedEndRead,
        ReadDirection Direction,
        bool Filtered,
        int ReadIndex)
    {
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

    public enum ReadDirection:byte
    {
        Forward,
        Reverse
    }
}