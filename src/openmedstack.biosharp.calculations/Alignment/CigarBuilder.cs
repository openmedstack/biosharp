namespace OpenMedStack.BioSharp.Calculations.Alignment;

using System;
using System.Collections.Generic;

/// <summary>
/// Converts Smith-Waterman alignment results into CIGAR strings for SAM/BAM output.
/// Also handles soft-clipped reads by detecting gaps at the ends of alignments.
/// </summary>
public static class CigarBuilder
{
    private const char OpMatch = 'M';
    private const char OpInsertion = 'I';
    private const char OpDeletion = 'D';
    private const char OpSoftClip = 'S';
    private const char OpHardClip = 'H';

    /// <summary>
    /// Builds a CIGAR string from a Smith-Waterman alignment result.
    /// Gaps in the reference are insertions (I), gaps in the read are deletions (D).
    /// Leading/trailing gaps in the read that represent soft-clips are marked accordingly.
    /// </summary>
    /// <param name="result">The alignment result with aligned ref/read sequences.</param>
    /// <param name="trimPrefix">
    /// Number of characters to skip from the start of the alignment string.
    /// Typically the visual alignment string has leading spaces for ref offset.
    /// </param>
    /// <returns>A CIGAR string (e.g., "10M5I3M1D7M").</returns>
    public static string BuildCigar(AlignmentResult result, int trimPrefix = 0)
    {
        var refSeq = result.AlignedReference;
        var readSeq = result.AlignedRead;
        var len = refSeq.Length;

        if (len == 0) return string.Empty;

        var leftClip = result.LeftSoftClip;
        var rightClip = result.RightSoftClip;
        var start = trimPrefix;
        if (start < 0 || start >= len) start = 0;

        var ops = new List<CigarOpCount>();

        if (leftClip > 0 && result.ReferenceStartPosition > 0) ops.Add(new CigarOpCount(leftClip, OpSoftClip));

        for (var i = start; i < len; i++)
        {
            var refChar = refSeq[i];
            var readChar = readSeq[i];

            if (refChar == '-')
                AppendOp(ops, OpInsertion);
            else if (readChar == '-')
                AppendOp(ops, OpDeletion);
            else
                AppendOp(ops, OpMatch);
        }

        if (rightClip > 0) ops.Add(new CigarOpCount(rightClip, OpSoftClip));

        return FormatCigar(ops);
    }

    /// <summary>
    /// Builds a CIGAR string directly from aligned sequence strings.
    /// Useful when you don't have a full AlignmentResult but have aligned ref/read strings.
    /// </summary>
    /// <param name="alignedRef">Aligned reference sequence (with '-' for insertions).</param>
    /// <param name="alignedRead">Aligned read sequence (with '-' for deletions).</param>
    /// <param name="leftClip">Number of soft-clipped bases at the 5' end.</param>
    /// <param name="rightClip">Number of soft-clipped bases at the 3' end.</param>
    /// <returns>A CIGAR string.</returns>
    public static string BuildCigar(string alignedRef, string alignedRead, int leftClip = 0, int rightClip = 0)
    {
        var len = Math.Max(alignedRef.Length, alignedRead.Length);
        var ops = new List<CigarOpCount>();

        if (leftClip > 0) ops.Add(new CigarOpCount(leftClip, OpSoftClip));

        for (var i = 0; i < len; i++)
        {
            var refChar = i < alignedRef.Length ? alignedRef[i] : '-';
            var readChar = i < alignedRead.Length ? alignedRead[i] : '-';

            if (refChar == '-')
                AppendOp(ops, OpInsertion);
            else if (readChar == '-')
                AppendOp(ops, OpDeletion);
            else
                AppendOp(ops, OpMatch);
        }

        if (rightClip > 0) ops.Add(new CigarOpCount(rightClip, OpSoftClip));

        return FormatCigar(ops);
    }

    /// <summary>
    /// Builds CIGAR operations from an alignment result as an array of (count, op) tuples.
    /// This can be used when you need to further manipulate the CIGAR operations.
    /// </summary>
    public static (int, char)[] BuildCigarOps(AlignmentResult result, int trimPrefix = 0)
    {
        return BuildCigarOps(result.AlignedReference, result.AlignedRead, result.LeftSoftClip, result.RightSoftClip,
            trimPrefix);
    }

    /// <summary>
    /// Builds CIGAR operations from aligned strings directly.
    /// </summary>
    public static (int, char)[] BuildCigarOps(
        string alignedRef,
        string alignedRead,
        int leftClip = 0,
        int rightClip = 0,
        int trimPrefix = 0)
    {
        var len = Math.Max(alignedRef.Length, alignedRead.Length);
        var ops = new List<(int, char)>();

        if (leftClip > 0) ops.Add((leftClip, OpSoftClip));

        var start = trimPrefix < 0 || trimPrefix >= len ? 0 : trimPrefix;

        for (var i = start; i < len; i++)
        {
            var refChar = i < alignedRef.Length ? alignedRef[i] : '-';
            var readChar = i < alignedRead.Length ? alignedRead[i] : '-';

            if (refChar == '-')
                ops.Add((1, OpInsertion));
            else if (readChar == '-')
                ops.Add((1, OpDeletion));
            else
                ops.Add((1, OpMatch));
        }

        if (rightClip > 0) ops.Add((rightClip, OpSoftClip));

        // Merge consecutive same-type operations
        var merged = new List<(int, char)>();
        foreach (var opItem in ops)
        {
            var count = opItem.Item1;
            var op = opItem.Item2;
            if (merged.Count > 0 && merged[merged.Count - 1].Item2 == op)
            {
                var last = merged[merged.Count - 1];
                merged[merged.Count - 1] = (last.Item1 + count, op);
            }
            else
            {
                merged.Add((count, op));
            }
        }

        return merged.ToArray();
    }

    /// <summary>
    /// Converts CIGAR operations to a CIGAR string.
    /// </summary>
    internal static string FormatCigar(List<CigarOpCount> ops)
    {
        if (ops.Count == 0) return "*";

        var sb = new System.Text.StringBuilder();
        foreach (var opEntry in ops) sb.Append(opEntry.Count).Append(opEntry.Op);
        return sb.ToString();
    }

    private static void AppendOp(List<CigarOpCount> ops, char op)
    {
        if (ops.Count > 0 && ops[ops.Count - 1].Op == op)
        {
            var last = ops[ops.Count - 1];
            ops[ops.Count - 1] = new CigarOpCount(last.Count + 1, op);
        }
        else
        {
            ops.Add(new CigarOpCount(1, op));
        }
    }
}

/// <summary>
/// Helper class to accumulate CIGAR operation counts before merging.
/// </summary>
internal class CigarOpCount
{
    public int Count { get; }
    public char Op { get; }

    public CigarOpCount(int count, char op)
    {
        Count = count;
        Op = op;
    }
}
