namespace OpenMedStack.BioSharp.Calculations.Alignment;

/// <summary>
/// Configurable thresholds for panel QC decisions.
/// </summary>
public sealed class PanelQcThresholds
{
    /// <summary>Minimum mean coverage to PASS (default: 20x).</summary>
    public double MinMeanCoverage { get; init; } = 20.0;

    /// <summary>Minimum mean coverage to WARN (below this → FAIL). Default: 10x.</summary>
    public double WarnMeanCoverage { get; init; } = 10.0;

    /// <summary>Minimum mapping rate [0–1] to PASS (default: 0.90).</summary>
    public double MinMappingRate { get; init; } = 0.90;

    /// <summary>Minimum mapping rate to WARN (default: 0.80).</summary>
    public double WarnMappingRate { get; init; } = 0.80;

    /// <summary>Maximum duplicate rate [0–1] to PASS (default: 0.20).</summary>
    public double MaxDuplicateRate { get; init; } = 0.20;

    /// <summary>Maximum duplicate rate to WARN (default: 0.30).</summary>
    public double WarnDuplicateRate { get; init; } = 0.30;

    /// <summary>Minimum Q30 fraction [0–1] to PASS (default: 0.80).</summary>
    public double MinQ30Fraction { get; init; } = 0.80;

    /// <summary>Minimum Q30 fraction to WARN (default: 0.70).</summary>
    public double WarnQ30Fraction { get; init; } = 0.70;
}