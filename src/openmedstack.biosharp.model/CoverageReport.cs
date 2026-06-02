namespace OpenMedStack.BioSharp.Model;

/// <summary>
/// Result of a coverage depth computation (equivalent to <c>samtools depth</c> summary output).
/// </summary>
public sealed class CoverageReport
{
    /// <summary>Per-position depth array (0-based index = 0-based reference position).</summary>
    public int[] PerPositionDepth { get; init; } = [];

    /// <summary>Mean depth across the target region (or whole reference if no target supplied).</summary>
    public double MeanDepth { get; init; }

    /// <summary>Median depth across the target region.</summary>
    public double MedianDepth { get; init; }

    /// <summary>Minimum depth within the target region.</summary>
    public double MinDepth { get; init; }

    /// <summary>Maximum depth within the target region.</summary>
    public double MaxDepth { get; init; }

    /// <summary>Fraction of target bases with depth ≥ 10×.</summary>
    public double FractionAt10x { get; init; }

    /// <summary>Fraction of target bases with depth ≥ 20×.</summary>
    public double FractionAt20x { get; init; }

    /// <summary>Fraction of target bases with depth ≥ 30×.</summary>
    public double FractionAt30x { get; init; }

    /// <summary>Fraction of target bases with depth ≥ 100×.</summary>
    public double FractionAt100x { get; init; }

    /// <summary>Fraction of target bases with depth ≥ 500×.</summary>
    public double FractionAt500x { get; init; }

    /// <summary>Coefficient of variation (stddev / mean) of depth across the target region.</summary>
    public double CoefficientOfVariation { get; init; }

    /// <summary>Number of bases in the target region (or reference length if no target supplied).</summary>
    public int TargetBasesCovered { get; init; }
}
