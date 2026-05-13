namespace OpenMedStack.BioSharp.Model.Bcl;

using System;

public record struct ReadData(int Tile, ReadType Type, ReadOnlyMemory<char> Bases, ReadOnlyMemory<char> Qualities, int ReadIndex);
