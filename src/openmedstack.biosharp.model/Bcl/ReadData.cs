namespace OpenMedStack.BioSharp.Model.Bcl
{
    using System;

    public record struct ReadData(int Tile, ReadType Type, Memory<char> Bases, Memory<char> Qualities, int ReadIndex);
}
