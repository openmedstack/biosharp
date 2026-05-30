namespace OpenMedStack.Preator;

/// <summary>
/// Summary statistics written by the <c>trim</c> command.
/// </summary>
internal sealed class TrimSummary
{
    public string Adapter { get; init; } = string.Empty;
    public int MinLength { get; init; }
    public int MaxMismatches { get; init; }
    public long TotalReads { get; init; }
    public int ReadsTrimmed { get; init; }
    public int ReadsDiscarded { get; init; }
    public long SurvivingReads { get; init; }
    public int BasesRemoved { get; init; }
    public long BasesWritten { get; init; }
    public double SurvivalRate => TotalReads == 0 ? 0 : (double)SurvivingReads / TotalReads * 100.0;
}

