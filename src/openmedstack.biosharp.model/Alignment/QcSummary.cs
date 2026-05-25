namespace OpenMedStack.BioSharp.Model.Alignment;

/// <summary>
/// Summary of QC metrics for use in clinical reports.
/// </summary>
public sealed class QcSummary
{
    public double MeanCoverage { get; init; }
    public double MappingRate { get; init; }
    public double DuplicateRate { get; init; }
    public double Q30Fraction { get; init; }
}