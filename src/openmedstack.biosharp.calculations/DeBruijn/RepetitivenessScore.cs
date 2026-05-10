namespace OpenMedStack.BioSharp.Calculations.DeBruijn;

/// <summary>
/// Results of analyzing a bubble for repetitiveness.
/// Used by RepetitivenessAnalyzer to score path uniqueness and recommend confidence level.
/// </summary>
public class RepetitivenessScore
{
    /// <summary>Total number of unique k-mers across all paths in the bubble.</summary>
    public int TotalKmers { get; }

    /// <summary>Average copy number of k-mers in the bubble. Higher means more repetitive.</summary>
    public int AvgCopyNum { get; }

    /// <summary>Number of k-mers that appear above the high-copy threshold (potential repeats).</summary>
    public int RepeatKmerCount { get; }

    /// <summary>High copy-number k-mers found in this bubble's paths.</summary>
    public string[] RepeatKmers { get; }

    /// <summary>Estimated confidence based on repeat analysis.</summary>
    public BubbleConfidence Confidence { get; }

    /// <summary>
    /// Score formula: (avgCopyNum * refDepth) / (repeatCount + 1).
    /// Higher values mean more confident variant; lower values indicate likely artifact.
    /// </summary>
    public double ConfidenceScore { get; }

    public RepetitivenessScore(
        int totalKmers,
        int avgCopyNum,
        int repeatKmerCount,
        string[] repeatKmers,
        BubbleConfidence confidence,
        double confidenceScore)
    {
        TotalKmers = totalKmers;
        AvgCopyNum = avgCopyNum;
        RepeatKmerCount = repeatKmerCount;
        RepeatKmers = repeatKmers;
        Confidence = confidence;
        ConfidenceScore = confidenceScore;
    }
}
