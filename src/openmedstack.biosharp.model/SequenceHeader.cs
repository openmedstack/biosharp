namespace OpenMedStack.BioSharp.Model;

using Bcl;

public record SequenceHeader(string Barcode, string Instrument, int RunNumber, string FlowCell, int Lane, int Tile, IPositionalData Position, bool PairedEndRead, bool Filtered, ReadDirection Direction, ReadType Type)
{
    /// <inheritdoc />
    public override string ToString()
    {
        return $"{Instrument}:{RunNumber}:{FlowCell}:{Lane}:{Tile}:{Position.XCoordinate}:{Position.YCoordinate} {(PairedEndRead ? 2 : 1)}:{(Filtered ? "Y" : "N")}:0:{Barcode}";

    }

    public static SequenceHeader Empty { get; } = new (
        "",
        "",
        0,
        "",
        0,
        0,
        new PositionalData(0, 0),
        false,
        false,
        ReadDirection.Forward,
        ReadType.S);
}