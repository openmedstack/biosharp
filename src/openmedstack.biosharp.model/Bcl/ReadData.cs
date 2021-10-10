namespace OpenMedStack.BioSharp.Model.Bcl
{
    public record ReadData(int Tile, ReadType Type, byte[] Bases, byte[] Qualities, int ReadIndex);
}
