namespace OpenMedStack.BioSharp.Model.Bcl
{
    using System;

    public record struct ClusterData(
        string Barcode,
        Memory<char> Bases,
        Memory<char> Qualities,
        ReadType Type,
        int Lane,
        int Tile,
        IPositionalData Position,
        bool PairedEndRead,
        ReadDirection Direction,
        bool Filtered,
        int ReadIndex);
}