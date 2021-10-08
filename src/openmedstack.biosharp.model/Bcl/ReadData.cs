namespace OpenMedStack.BioSharp.Model.Bcl
{
    public record ReadData(ReadType Type, byte[] Bases, byte[] Qualities, int ReadIndex);
}
