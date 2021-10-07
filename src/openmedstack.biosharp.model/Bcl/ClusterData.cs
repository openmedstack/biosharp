namespace OpenMedStack.BioSharp.Model.Bcl
{
    using System.Text;

    public record ClusterData
    {
        public ClusterData(string barcode, byte[] bases, byte[] qualities, ReadType type, int lane, int tile, IPositionalData position, bool pairedEndRead, bool filtered)
        {
            Barcode = barcode;
            Bases = Encoding.ASCII.GetString(bases);
            Qualities = Encoding.ASCII.GetString(qualities);
            Type = type;
            Lane = lane;
            Tile = tile;
            Position = position;
            PairedEndRead = pairedEndRead;
            Filtered = filtered;
        }

        public string Barcode { get; }
        public string Bases { get; }
        public string Qualities { get; }
        public ReadType Type { get; }
        public int Lane { get; }
        public int Tile { get; }
        public IPositionalData Position { get; }
        public bool PairedEndRead { get; }
        public bool Filtered { get; }

        public string ToSequenceHeader(string instrument, int runNumber, string flowCell)
        {
            return
                $"{instrument}:{runNumber}:{flowCell}:{Lane}:{Tile}:{Position.XCoordinate}:{Position.YCoordinate} {(PairedEndRead ? 2 : 1)}:{(Filtered ? "Y" : "N")}:0:{Barcode}";
        }
    }
}