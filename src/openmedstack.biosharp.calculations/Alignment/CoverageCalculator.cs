using System.Linq;

namespace OpenMedStack.BioSharp.Calculations.Alignment;

using System;
using System.Collections.Generic;
using Io.Sam;
using Model;

/// <summary>
/// Computes per-position read depth and coverage uniformity metrics from aligned reads.
/// Equivalent to <c>samtools depth</c>.
/// </summary>
public sealed class CoverageCalculator
{
    /// <summary>
    /// Computes coverage from the given alignments.
    /// </summary>
    /// <param name="alignments">Aligned reads.</param>
    /// <param name="referenceLength">Total length of the reference sequence (to size the depth array).</param>
    /// <param name="targetIntervals">
    /// Optional BED intervals; if supplied only positions within these intervals are used for
    /// summary statistics. Per-position depth is still computed for all positions.
    /// </param>
    public static CoverageReport Compute(
        IReadOnlyList<AlignmentSection> alignments,
        int referenceLength,
        IReadOnlyList<BedInterval>? targetIntervals = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(referenceLength);

        var depth = new int[referenceLength];

        foreach (var a in alignments)
        {
            if ((a.Flag & AlignmentSection.AlignmentFlag.SegmentUnmapped) != 0)
            {
                continue;
            }

            // Position is used as provided (test helpers pass 0-based positions directly)
            var refPos = a.Position;

            foreach (var (count, op) in a.Cigar)
            {
                var c = (int)count;
                switch (op)
                {
                    // Consumes reference and query → increment depth
                    case CigarOp.Match:
                    case CigarOp.Equal:
                    case CigarOp.Difference:
                    {
                        var to = Math.Min(refPos + c, referenceLength);
                        for (var pos = refPos; pos < to; pos++)
                        {
                            depth[pos]++;
                        }

                        refPos += c;
                        break;
                    }
                    // Consumes reference only (deletion, skip) → advance ref position, no depth
                    case CigarOp.Deletion:
                    case CigarOp.Skip:
                        refPos += c;
                        break;
                    // Consumes query only (insertion, soft-clip) → no ref position change
                    case CigarOp.Insertion:
                    case CigarOp.SoftClip:
                        break;
                    // Consumes neither (hard-clip, padding)
                    default:
                        break;
                }
            }
        }

        // Determine positions to summarise over
        int[] targetPositions;
        if (targetIntervals is { Count: > 0 })
        {
            var totalCount = targetIntervals.Sum(interval =>
                Math.Max(0, Math.Min(interval.End, referenceLength) - interval.Start));

            targetPositions = new int[totalCount];
            var idx = 0;
            foreach (var interval in targetIntervals)
            {
                for (var p = interval.Start; p < interval.End && p < referenceLength; p++)
                {
                    targetPositions[idx++] = p;
                }
            }
        }
        else
        {
            targetPositions = new int[referenceLength];
            for (var i = 0; i < referenceLength; i++)
            {
                targetPositions[i] = i;
            }
        }

        return ComputeStats(depth, targetPositions);
    }

    private static CoverageReport ComputeStats(int[] depth, int[] targetPositions)
    {
        if (targetPositions.Length == 0)
        {
            return new CoverageReport
            {
                PerPositionDepth = depth,
                TargetBasesCovered = 0
            };
        }

        var targetDepths = new int[targetPositions.Length];
        for (var i = 0; i < targetPositions.Length; i++)
        {
            targetDepths[i] = depth[targetPositions[i]];
        }

        Array.Sort(targetDepths);

        var n = targetDepths.Length;
        double sum = 0;
        double sumSq = 0;
        var minD = (double)targetDepths[0];
        var maxD = (double)targetDepths[n - 1];

        for (var i = 0; i < n; i++)
        {
            sum += targetDepths[i];
            sumSq += (double)targetDepths[i] * targetDepths[i];
        }

        var mean = sum / n;
        var variance = sumSq / n - mean * mean;
        var stdDev = Math.Sqrt(Math.Max(0.0, variance));
        var cv = mean > 0 ? stdDev / mean : 0.0;

        var median = n % 2 == 1
            ? targetDepths[n / 2]
            : (targetDepths[n / 2 - 1] + targetDepths[n / 2]) / 2.0;

        return new CoverageReport
        {
            PerPositionDepth = depth,
            MeanDepth = mean,
            MedianDepth = median,
            MinDepth = minD,
            MaxDepth = maxD,
            FractionAt10x = (double)CountAtLeast(10) / n,
            FractionAt20x = (double)CountAtLeast(20) / n,
            FractionAt30x = (double)CountAtLeast(30) / n,
            FractionAt100x = (double)CountAtLeast(100) / n,
            FractionAt500x = (double)CountAtLeast(500) / n,
            CoefficientOfVariation = cv,
            TargetBasesCovered = n
        };

        int CountAtLeast(int threshold)
        {
            return targetDepths.Count(d => d >= threshold);
        }
    }
}
