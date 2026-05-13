using System.Collections.Generic;

namespace OpenMedStack.BioSharp.Calculations.Alignment;

/// <summary>
/// Summary of the insert-size distribution computed by <see cref="InsertSizeEstimator"/>.
/// Serialisable to JSON.
/// </summary>
public sealed class InsertSizeReport
{
    /// <summary>Mean insert size in base pairs.</summary>
    public double Mean { get; init; }

    /// <summary>Median insert size in base pairs.</summary>
    public double Median { get; init; }

    /// <summary>Sample standard deviation of insert sizes.</summary>
    public double StandardDeviation { get; init; }

    /// <summary>10th percentile insert size.</summary>
    public double Percentile10 { get; init; }

    /// <summary>90th percentile insert size.</summary>
    public double Percentile90 { get; init; }

    /// <summary>
    /// Histogram of insert sizes. Keys are the lower bound of each bin;
    /// values are read counts in that bin.
    /// </summary>
    public SortedDictionary<int, int> Histogram { get; init; } = new();

    /// <summary>Number of properly paired reads with a valid TLEN.</summary>
    public int ProperlyPairedReads { get; init; }

    /// <summary>
    /// Number of paired reads whose mate maps to a different reference or has TLEN = 0.
    /// </summary>
    public int DiscordantPairs { get; init; }
}