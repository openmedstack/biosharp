namespace OpenMedStack.BioSharp.Calculations.Alignment;

/// <summary>
/// Result of a coverage computation.
/// </summary>
public sealed class CoverageReport
{
    /// <summary>Per-position depth array (0-based index = 0-based position).</summary>
    public int[] PerPositionDepth { get; init; } = [];

    /// <summary>Mean depth across covered positions (or target region if supplied).</summary>
    public double MeanDepth { get; init; }

    /// <summary>Median depth.</summary>
    public double MedianDepth { get; init; }

    /// <summary>Minimum depth across the target region.</summary>
    public double MinDepth { get; init; }

    /// <summary>Maximum depth across the target region.</summary>
    public double MaxDepth { get; init; }

    /// <summary>Fraction of target bases with depth ≥ 10.</summary>
    public double FractionAt10x { get; init; }

    /// <summary>Fraction of target bases with depth ≥ 20.</summary>
    public double FractionAt20x { get; init; }

    /// <summary>Fraction of target bases with depth ≥ 30.</summary>
    public double FractionAt30x { get; init; }

    /// <summary>Fraction of target bases with depth ≥ 100.</summary>
    public double FractionAt100x { get; init; }

    /// <summary>Fraction of target bases with depth ≥ 500.</summary>
    public double FractionAt500x { get; init; }

    /// <summary>Coefficient of variation (stddev / mean) of depth across the target region.</summary>
    public double CoefficientOfVariation { get; init; }

    /// <summary>Number of bases in the target region (or reference if no target supplied).</summary>
    public int TargetBasesCovered { get; init; }
}