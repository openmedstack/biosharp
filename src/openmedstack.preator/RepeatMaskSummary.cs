namespace OpenMedStack.Preator;

internal sealed class RepeatMaskSummary
{
    public string InputFasta { get; set; } = string.Empty;
    public string OutputFasta { get; set; } = string.Empty;
    public string? LibraryPath { get; set; }
    public long TotalBases { get; set; }
    public long MaskedBases { get; set; }
    public int MaskedRegions { get; set; }
    public double MaskedFraction { get; set; }
}
