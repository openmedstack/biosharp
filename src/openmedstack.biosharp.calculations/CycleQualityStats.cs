namespace OpenMedStack.BioSharp.Calculations;

/// <summary>
/// Statistics for a single read cycle's quality scores.
/// </summary>
public sealed class CycleQualityStats
{
    /// <summary>Mean Phred quality score.</summary>
    public double Mean { get; init; }

    /// <summary>Median Phred quality score.</summary>
    public double Median { get; init; }

    /// <summary>Lower quartile (Q1) Phred quality score.</summary>
    public double LowerQuartile { get; init; }

    /// <summary>Upper quartile (Q3) Phred quality score.</summary>
    public double UpperQuartile { get; init; }

    /// <summary>Minimum Phred quality score.</summary>
    public double Min { get; init; }

    /// <summary>Maximum Phred quality score.</summary>
    public double Max { get; init; }
}