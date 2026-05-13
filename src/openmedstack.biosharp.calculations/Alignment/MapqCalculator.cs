namespace OpenMedStack.BioSharp.Calculations.Alignment;

using System;

/// <summary>
/// Computes the mapping quality (MAPQ) score for a read alignment.
///
/// The model follows BWA-MEM's approach:
/// <list type="bullet">
///   <item><description>
///     MAPQ = 60 when the best alignment is unique (no second-best, i.e., <paramref name="secondBestScore"/> ≤ 0).
///   </description></item>
///   <item><description>
///     MAPQ = 0 when <paramref name="bestScore"/> == <paramref name="secondBestScore"/> (ambiguous alignment).
///   </description></item>
///   <item><description>
///     Otherwise MAPQ = round(60 × (1 − secondBestScore / bestScore)), clamped to [0, 60].
///   </description></item>
/// </list>
///
/// A <paramref name="secondBestScore"/> of −1 is treated as "no second-best alignment found"
/// and results in MAPQ = 60.
/// </summary>
public static class MapqCalculator
{
    /// <summary>Maximum MAPQ value emitted (SAM specification cap).</summary>
    public const int MaxMapq = 60;

    /// <summary>
    /// Calculates the MAPQ score.
    /// </summary>
    /// <param name="bestScore">
    /// Alignment score of the best (primary) alignment window.  Must be &gt; 0.
    /// </param>
    /// <param name="secondBestScore">
    /// Alignment score of the second-best competing alignment window.
    /// Pass −1 (or any negative value) when no second-best window was found.
    /// </param>
    /// <returns>MAPQ value in [0, 60].</returns>
    public static byte Calculate(int bestScore, int secondBestScore)
    {
        if (bestScore <= 0)
        {
            return 0;
        }

        // No competing alignment → uniquely mapped
        if (secondBestScore < 0)
        {
            return MaxMapq;
        }

        // Equal alignments → ambiguous
        if (secondBestScore >= bestScore)
        {
            return 0;
        }

        // BWA-MEM-style linear model:
        //   MAPQ = round(60 × (1 − secondBest / best))
        //
        // This scales from 0 (when scores are equal) to 60 (when secondBest → 0).
        // The scores are compared directly without normalisation to read length so
        // that the ratio captures the relative confidence of the best alignment.
        var ratio = (double)secondBestScore / bestScore;
        var mapq = (int)Math.Round(MaxMapq * (1.0 - ratio));
        return (byte)Math.Min(mapq, MaxMapq);
    }

    /// <summary>
    /// Convenience overload that takes floating-point (unnormalised) scores and
    /// computes MAPQ = round(60 × (1 − second / best)), capped at [0, 60].
    /// </summary>
    public static byte CalculateFromRatio(double bestScore, double secondBestScore)
    {
        if (bestScore <= 0)
        {
            return 0;
        }

        if (secondBestScore < 0)
        {
            return MaxMapq;
        }

        if (secondBestScore >= bestScore)
        {
            return 0;
        }

        var pWrong = secondBestScore / bestScore;
        var phred = -10.0 * Math.Log10(pWrong);
        var mapq = (int)Math.Round(phred);
        return (byte)Math.Min(mapq, MaxMapq);
    }
}
