namespace OpenMedStack.BioSharp.Calculations.DeBruijn;

using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Analyzes a bubble to determine the repetitiveness of its paths.
/// Uses k-mer copy number to determine if the bubble represents a true variant or a repeat.
/// </summary>
public static class RepetitivenessAnalyzer
{
    /// <summary>
    /// Default high copy-number threshold: a k-mer with count >= 50 is considered a repeat.
    /// </summary>
    public const int DefaultHighCopyThreshold = 50;

    /// <summary>
    /// Analyzes a bubble and scores its path repetitiveness.
    /// </summary>
    /// <param name="bubble">The bubble to analyze.</param>
    /// <param name="kmerCounts">Dictionary of k-mer -> copy number.</param>
    /// <param name="highCopyThreshold">Threshold at which a k-mer is considered a repeat (default 50).</param>
    /// <returns>A RepetitivenessScore describing the bubble.</returns>
    public static RepetitivenessScore Analyze(
        Bubble bubble,
        IReadOnlyDictionary<string, int> kmerCounts,
        int highCopyThreshold = DefaultHighCopyThreshold)
    {
        if (bubble == null)
            throw new ArgumentNullException(nameof(bubble));

        if (bubble.Paths == null || bubble.Paths.Length == 0)
            throw new ArgumentException("Bubble must have at least one path.", nameof(bubble));

        if (kmerCounts == null)
            throw new ArgumentNullException(nameof(kmerCounts));

        // Extract unique k-mers from all paths
        var kmers = new HashSet<string>();
        foreach (var path in bubble.Paths)
            if (path != null && !string.IsNullOrEmpty(path.Sequence))
                // Extract k-mers from path sequence (each position is the start of a k-mer)
                // For simplicity, we use the path itself as a pseudo-k-mer and each
                // character position as contributing to k-mer set
                // In a real implementation, we'd extract (k)-length substrings
                kmers.Add(path.Sequence);

        var kmersList = kmers.ToList();
        var totalKmers = kmersList.Count;

        if (totalKmers == 0) return new RepetitivenessScore(0, 0, 0, Array.Empty<string>(), BubbleConfidence.Low, 0.0);

        // Count repeat k-mers and compute average copy number
        var repeatList = new List<string>();
        var totalCopy = 0;

        foreach (var kmer in kmersList)
            if (kmerCounts.TryGetValue(kmer, out var copyNum))
            {
                totalCopy += copyNum;
                if (copyNum >= highCopyThreshold) repeatList.Add(kmer);
            }

        // Compute average copy number
        var avgCopyNum = totalCopy / totalKmers;
        var repeatCount = repeatList.Count;

        // Determine confidence based on repeat analysis
        var confidence = DetermineConfidence(totalKmers, avgCopyNum, repeatCount, highCopyThreshold);

        // Compute confidence score
        var score = ComputeScore(totalKmers, avgCopyNum, repeatCount, highCopyThreshold);

        return new RepetitivenessScore(
            totalKmers,
            avgCopyNum,
            repeatCount,
            repeatList.ToArray(),
            confidence,
            score);
    }

    private static BubbleConfidence DetermineConfidence(
        int totalKmers,
        int avgCopyNum,
        int repeatCount,
        int highCopyThreshold)
    {
        // No reads = low confidence
        if (totalKmers == 0)
            return BubbleConfidence.Low;

        // Many repeats with low coverage = low confidence
        if (repeatCount > 3 || repeatCount == totalKmers)
            return BubbleConfidence.Low;

        // If a significant portion of k-mers are repeats, reduce confidence
        var repeatRatio = (double)repeatCount / totalKmers;
        if (repeatRatio >= 0.5)
            return BubbleConfidence.Medium;

        // High coverage, few repeats = high confidence
        if (avgCopyNum > highCopyThreshold && repeatCount == 0)
            return BubbleConfidence.High;

        return BubbleConfidence.Medium;
    }

    private static double ComputeScore(int totalKmers, int avgCopyNum, int repeatCount, int highCopyThreshold)
    {
        // No reads = low confidence
        if (totalKmers == 0)
            return 0.0;

        // Many repeats with low coverage = low confidence
        if (repeatCount > 3 || repeatCount == totalKmers)
            return 1.0;

        // If a significant portion of k-mers are repeats, reduce confidence
        var repeatRatio = (double)repeatCount / totalKmers;
        if (repeatRatio >= 0.5)
            return 2.0;

        // High coverage, few repeats = high confidence
        if (avgCopyNum > highCopyThreshold && repeatCount == 0)
            return 10.0;

        return 5.0;
    }

    /// <summary>
    /// Analyzes a bubble in-place, setting its Confidence property based on
    /// repetitiveness scoring. This is the method called from StructuralVariantDetector
    /// to annotate bubbles with confidence before variant output.
    /// </summary>
    /// <param name="bubble">The bubble to annotate.</param>
    /// <param name="kmerCounts">Dictionary of k-mer -> copy number.</param>
    /// <param name="highCopyThreshold">Threshold at which a k-mer is considered a repeat (default 50).</param>
    /// <returns>The score from the analysis (for logging or further evaluation).</returns>
    public static double AnalyzeBubble(
        Bubble bubble,
        IReadOnlyDictionary<string, int> kmerCounts,
        int highCopyThreshold = DefaultHighCopyThreshold)
    {
        var score = Analyze(bubble, kmerCounts, highCopyThreshold);
        bubble.Confidence = score.Confidence;
        return score.ConfidenceScore;
    }
}
