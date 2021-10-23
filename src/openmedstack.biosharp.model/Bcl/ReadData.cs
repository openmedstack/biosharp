namespace OpenMedStack.BioSharp.Model.Bcl
{
    using System;

    public record struct ReadData(int Tile, ReadType Type, Memory<byte> Bases, Memory<byte> Qualities, int ReadIndex);
}
