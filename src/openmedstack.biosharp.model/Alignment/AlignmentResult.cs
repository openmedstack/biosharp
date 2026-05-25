namespace OpenMedStack.BioSharp.Model.Alignment;

/// <summary>
/// Result of a Smith-Waterman local alignment.
/// </summary>
public record AlignmentResult(
    /// <summary>Aligned reference sequence (with '-' for insertions in read).</summary>
    string AlignedReference,
    /// <summary>Aligned read sequence (with '-' for deletions in read).</summary>
    string AlignedRead,
    /// <summary>Visual alignment string: '|' for match, 'X' for mismatch, ' ' for gap in either sequence.</summary>
    string VisualString,
    /// <summary>Alignment score (total match/mismatch/gap score).</summary>
    int Score,
    /// <summary>0-based start position on the reference where alignment begins.</summary>
    int ReferenceStartPosition,
    /// <summary>Number of unaligned read bases at the 5' end (left soft clip).</summary>
    int LeftSoftClip = 0,
    /// <summary>Number of unaligned read bases at the 3' end (right soft clip).</summary>
    int RightSoftClip = 0,
    /// <summary>True when banding or X-drop pruned part of the DP search space.</summary>
    bool WasPruned = false
    );
