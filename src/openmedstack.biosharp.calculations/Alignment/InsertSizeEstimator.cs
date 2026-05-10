namespace OpenMedStack.BioSharp.Calculations.Alignment;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using Io.Sam;

/// <summary>
/// Computes the empirical insert-size distribution from aligned paired-end reads.
///
/// For each properly paired read (TLEN ≠ 0 and both mates on the same reference sequence),
/// the absolute value of TLEN is used as the insert size. Reads with TLEN = 0 or whose mate
/// maps to a different reference are counted as discordant.
/// </summary>
public sealed class InsertSizeEstimator
{
    private readonly int _binSize;

    /// <summary>
    /// Creates a new <see cref="InsertSizeEstimator"/>.
    /// </summary>
    /// <param name="binSize">Histogram bin width in base pairs (default: 10).</param>
    public InsertSizeEstimator(int binSize = 10)
    {
        if (binSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(binSize));
        }

        _binSize = binSize;
    }

    /// <summary>
    /// Estimates the insert-size distribution from <paramref name="alignments"/>.
    /// </summary>
    public InsertSizeReport Estimate(IReadOnlyList<AlignmentSection> alignments)
    {
        var insertSizes = new List<double>(alignments.Count);
        var discordant = 0;

        foreach (var a in alignments)
        {
            // Only consider paired reads
            if ((a.Flag & AlignmentSection.AlignmentFlag.MultipleSegments) == 0)
            {
                continue;
            }

            // Discordant: mate on different reference (rnext != 0 means different contig in our model)
            // or unmapped mate
            if (a.ReferenceIdOfNextSegment != 0 ||
                (a.Flag & AlignmentSection.AlignmentFlag.NextSegmentInTemplateUnmapped) != 0)
            {
                discordant++;
                continue;
            }

            var tlen = Math.Abs(a.TemplateLength);

            // TLEN = 0 indicates the insert size is unknown — treat as discordant
            if (tlen == 0)
            {
                discordant++;
                continue;
            }

            insertSizes.Add(tlen);
        }

        if (insertSizes.Count == 0)
        {
            return new InsertSizeReport
            {
                ProperlyPairedReads = 0,
                DiscordantPairs = discordant,
                Histogram = new SortedDictionary<int, int>()
            };
        }

        insertSizes.Sort();
        var n = insertSizes.Count;

        var mean = insertSizes.Average();
        var median = ComputeMedian(insertSizes);
        var stdDev = ComputeStdDev(insertSizes, mean);
        var p10 = Percentile(insertSizes, 10);
        var p90 = Percentile(insertSizes, 90);
        var histogram = BuildHistogram(insertSizes, _binSize);

        return new InsertSizeReport
        {
            Mean = mean,
            Median = median,
            StandardDeviation = stdDev,
            Percentile10 = p10,
            Percentile90 = p90,
            Histogram = histogram,
            ProperlyPairedReads = n,
            DiscordantPairs = discordant
        };
    }

    // -------------------------------------------------------------------------
    // Private statistics helpers
    // -------------------------------------------------------------------------

    private static double ComputeMedian(List<double> sorted)
    {
        var n = sorted.Count;
        if (n == 0)
        {
            return 0;
        }

        return n % 2 == 0
            ? (sorted[n / 2 - 1] + sorted[n / 2]) / 2.0
            : sorted[n / 2];
    }

    private static double ComputeStdDev(List<double> values, double mean)
    {
        if (values.Count < 2)
        {
            return 0;
        }

        var variance = values.Sum(v => (v - mean) * (v - mean)) / (values.Count - 1);
        return Math.Sqrt(variance);
    }

    private static double Percentile(List<double> sorted, int pct)
    {
        if (sorted.Count == 0)
        {
            return 0;
        }

        var index = (pct / 100.0) * (sorted.Count - 1);
        var lower = (int)index;
        var upper = Math.Min(lower + 1, sorted.Count - 1);
        var fraction = index - lower;
        return sorted[lower] + fraction * (sorted[upper] - sorted[lower]);
    }

    private static SortedDictionary<int, int> BuildHistogram(List<double> sizes, int binSize)
    {
        var hist = new SortedDictionary<int, int>();
        foreach (var size in sizes)
        {
            var bin = (int)(Math.Floor(size / binSize) * binSize);
            hist[bin] = hist.TryGetValue(bin, out var count) ? count + 1 : 1;
        }
        return hist;
    }
}

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
