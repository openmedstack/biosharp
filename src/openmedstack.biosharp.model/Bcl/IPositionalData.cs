namespace OpenMedStack.BioSharp.Model.Bcl
{
    public interface IPositionalData //: IIlluminaData
    {
        public int Tile { get; }
        public int Lane { get; }
        public int XCoordinate { get; }
        public int YCoordinate { get; }
    }

    public record PositionalData(int Tile, int Lane, int XCoordinate, int YCoordinate) : IPositionalData;
}