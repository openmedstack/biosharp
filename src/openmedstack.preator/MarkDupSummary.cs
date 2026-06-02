namespace OpenMedStack.Preator;

internal sealed class MarkDupSummary
{
    public string InputBam { get; set; } = string.Empty;
    public string OutputBam { get; set; } = string.Empty;
    public int TotalReads { get; set; }
    public int DuplicateReads { get; set; }
    public int OpticalDuplicateReads { get; set; }
    public double DuplicateRate { get; set; }
}
