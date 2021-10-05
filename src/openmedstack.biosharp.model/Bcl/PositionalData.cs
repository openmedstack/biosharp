namespace OpenMedStack.BioSharp.Model.Bcl
{
    public record PositionalData(int Tile, int Lane, int XCoordinate, int YCoordinate) : IPositionalData;
}