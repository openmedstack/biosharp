namespace OpenMedStack.BioSharp.Calculations.Alignment;

using System;
using System.Collections.Generic;
using System.Linq;
using Model;
using DeBruijn;

/// <summary>
/// Performs soft-clip realignment on reads to recover variants that are lost by the primary
/// alignment. Reads with significant soft-clips (>=20% of read length) have their clipped
/// regions extracted and re-aligned against a local reference window upstream or downstream
/// of the primary alignment boundary.
///
/// SV Discovery:
/// - Insertions: clipped read re-aligns against reference with extra bases not present at alignment boundary
/// - Inversion breakpoints: clipped read re-aligns in reverse orientation to the reference
/// - Translocations: clipped read fails to align to the current chromosome entirely
/// </summary>
public class ReadRealigner
{
    /// <summary>
    /// Minimum fraction of read length that qualifies as a "significant" soft-clip.
    /// Default: 0.20 (20%).
    /// </summary>
    public float MinClipFraction { get; set; } = 0.20f;

    /// <summary>
    /// Minimum clip size in base pairs. Clips smaller than this are ignored.
    /// Default: 20 bp.
    /// </summary>
    public int MinClipSize { get; set; } = 20;

    /// <summary>
    /// Size of the local reference window to extract for realignment.
    /// Default: 200 bp upstream/downstream from the clip boundary.
    /// </summary>
    public int LocalWindowSize { get; set; } = 200;

    /// <summary>
    /// Minimum alignment score for a realigned clipped region to be considered valid.
    /// Default: 30.
    /// </summary>
    public int MinRealignScore { get; set; } = 30;

    /// <summary>
    /// Result of realigning a soft-clipped read region.
    /// </summary>
    public sealed class RealignmentResult
    {
        /// <summary>Summary of the realignment outcome.</summary>
        public string Summary { get; }

        /// <summary>The clipped sequence that was re-aligned.</summary>
        public string ClipSequence { get; }

        /// <summary>The alignment result for the clipped region.</summary>
        public AlignmentResult? Alignment { get; }

        /// <summary>The reference window used for realignment.</summary>
        public string ReferenceWindow { get; }

        /// <summary>Position in the reference where the clip was re-aligned, 1-based. 0 if no alignment.</summary>
        public int AlignedPosition { get; }

        /// <summary>Whether the clipped region was successfully re-aligned.</summary>
        public bool WasRealigned { get; }

        /// <summary>Whether this realignment represents a structural variant.</summary>
        public bool IsStructuralVariant { get; }

        /// <summary>Type of structural variant, if applicable.</summary>
        public SvType? SvType { get; }

        /// <summary>Position of the clip boundary on the original read (0-based).</summary>
        public int ClipPosition { get; }

        /// <summary>Size of the clipped region in base pairs.</summary>
        public int ClipSize { get; }

        /// <summary>Whether this was a 5' (left) or 3' (right) clip on the read.</summary>
        public bool IsLeftClip { get; }

        /// <summary>Chromosome of the original alignment. Useful for translocation detection.</summary>
        public string? Chromosome { get; }

        public RealignmentResult(
            string summary,
            string clipSequence,
            AlignmentResult? alignment,
            string referenceWindow,
            int alignedPosition,
            bool wasRealigned,
            SvType? svType,
            int clipPosition,
            int clipSize,
            bool isLeftClip,
            string? chromosome,
            bool isSv)
        {
            Summary = summary;
            ClipSequence = clipSequence;
            Alignment = alignment;
            ReferenceWindow = referenceWindow;
            AlignedPosition = alignedPosition;
            WasRealigned = wasRealigned;
            SvType = svType;
            ClipPosition = clipPosition;
            ClipSize = clipSize;
            IsLeftClip = isLeftClip;
            Chromosome = chromosome;
            IsStructuralVariant = isSv;
        }
    }

    /// <summary>
    /// Realigns the soft-clipped regions of an alignment result against a reference.
    /// Returns the clipped reads' realignment analysis.
    /// </summary>
    /// <param name="alignment">The primary alignment result (from SmithWatermanAligner).</param>
    /// <param name="reference">The full reference sequence.</param>
    /// <param name="chromosome">Chromosome name (for translocation detection).</param>
    /// <param name="readSequence">The original read sequence (before alignment).</param>
    /// <returns>Zero or more realignment results for clipped regions.</returns>
    public RealignmentResult[] Realign(
        AlignmentResult alignment,
        Sequence reference,
        string chromosome,
        string readSequence)
    {
        var results = new List<RealignmentResult>();
        var readLen = readSequence.Length;

        // Left clip: bases at the 5' end of the read that were not aligned
        if (alignment.LeftSoftClip > 0)
        {
            var clipLen = alignment.LeftSoftClip;
            if (clipLen >= readLen * MinClipFraction && clipLen >= MinClipSize)
            {
                var clipSeq = readSequence.Substring(0, clipLen);
                var result = RealignClip(clipSeq, clipLen, true, 0, alignment.ReferenceStartPosition,
                    reference, chromosome);
                results.Add(result);
            }
        }

        // Right clip
        if (alignment.RightSoftClip > 0)
        {
            var clipLen = alignment.RightSoftClip;
            if (clipLen >= readLen * MinClipFraction && clipLen >= MinClipSize)
            {
                var clipStartInRead = alignment.LeftSoftClip + alignment.AlignedRead.Length;
                var clipSeq = readSequence.Substring(clipStartInRead, clipLen);
                var refEndPos = alignment.ReferenceStartPosition + alignment.AlignedReference.Count(c => c != '-');
                if (refEndPos < reference.Length)
                {
                    var result = RealignClip(clipSeq, clipLen, false, clipStartInRead, refEndPos,
                        reference, chromosome);
                    results.Add(result);
                }
            }
        }

        return results.ToArray();
    }

    private RealignmentResult RealignClip(
        string clipSequence,
        int clipSize,
        bool isLeftClip,
        int clipPositionInRead,
        int boundaryPosition,
        Sequence reference,
        string chromosome)
    {
        // Extract local reference window around the clip boundary
        var windowStart = 0;
        var windowEnd = reference.Length;

        if (isLeftClip)
        {
            // Left clip: look upstream from the alignment start
            windowStart = Math.Max(0, boundaryPosition - LocalWindowSize);
            windowEnd = boundaryPosition;
        }
        else
        {
            // Right clip: look downstream from the alignment end
            windowStart = boundaryPosition;
            windowEnd = Math.Min(reference.Length, boundaryPosition + LocalWindowSize);
        }

        var windowLen = windowEnd - windowStart;
        if (windowLen <= 0)
            return new RealignmentResult(
                "Clip at boundary: no reference window available", clipSequence, null,
                "", windowStart, false, null, clipPositionInRead, clipSize, isLeftClip, chromosome, false);

        var refWindowSpan = reference.GetData()!.Span.Slice(windowStart, windowLen);
        var refWindowStr = new string(refWindowSpan.ToArray());

        // Re-align the clipped region against the local reference window
        var readSeq = new Sequence($"clip_{clipPositionInRead}",
            clipSequence.AsMemory(), new string('I', clipSequence.Length).AsMemory());

        AlignmentResult? alignResult = null;
        var wasRealigned = false;
        var isSv = false;
        SvType? svType = null;

        try
        {
            alignResult = SmithWatermanAligner.Align(
                new Sequence("refWindow", refWindowStr.AsMemory(), new string('I', refWindowStr.Length).AsMemory()),
                readSeq,
                2,
                -3,
                -5,
                -2,
                MinRealignScore);
        }
        catch
        {
            return new RealignmentResult(
                "Realignment failed", clipSequence, null, refWindowStr,
                windowStart, false, null, clipPositionInRead, clipSize, isLeftClip, chromosome, false);
        }

        if (alignResult != null && alignResult.Score >= MinRealignScore)
        {
            wasRealigned = true;

            // Check for structural variant indicators
            var refConsumed = alignResult.AlignedReference.Count(c => c != '-');
            var readConsumed = alignResult.AlignedRead.Count(c => c != '-');
            var insBases = readConsumed - refConsumed;

            if (insBases > 0)
            {
                // Clipped region has more bases than ref window -> insertion
                isSv = true;
                svType = SvType.Insertion;
            }
            else if (Math.Abs(insBases) >= 50)
            {
                // Large deletion in clipped region
                isSv = true;
                svType = SvType.Deletion;
            }
            else if (alignResult is { AlignedReference: not null, AlignedRead: not null })
            {
                // Check for inversion: do the clipped bases match the reverse complement of the reference?
                var altMatched = new string(alignResult.AlignedRead.Where(c => c != '-').ToArray());
                var refMatched = new string(alignResult.AlignedReference.Where(c => c != '-').ToArray());
                if (altMatched.Length == refMatched.Length && altMatched.Length > 0)
                {
                    var altRevComp = ReverseComplement(altMatched);
                    if (altRevComp == refMatched)
                    {
                        isSv = true;
                        svType = SvType.Inversion;
                    }
                }
            }

            // Get the position where the clipped region aligns
            var alignPos = alignResult.ReferenceStartPosition + windowStart;

            var summary =
                $"Clip at {clipPositionInRead} ({clipSize} bp, left={isLeftClip}): realigned with score {alignResult.Score}";

            return new RealignmentResult(summary, clipSequence, alignResult,
                refWindowStr, alignPos, wasRealigned, svType,
                clipPositionInRead, clipSize, isLeftClip, chromosome, isSv);
        }

        return new RealignmentResult(
            $"Clip at {clipPositionInRead} ({clipSize} bp, left={isLeftClip}): no realignment found",
            clipSequence, null, refWindowStr, windowStart, false, null,
            clipPositionInRead, clipSize, isLeftClip, chromosome, false);
    }

    /// <summary>
    /// Returns the reverse complement of a DNA sequence.
    /// Used to detect inversions (clipped read aligns in reverse complement).
    /// </summary>
    private static string ReverseComplement(string sequence)
    {
        if (string.IsNullOrEmpty(sequence)) return sequence;

        var complement = new char[sequence.Length];
        for (var i = 0; i < sequence.Length; i++)
        {
            var baseChar = char.ToUpper(sequence[sequence.Length - 1 - i]);
            complement[i] = baseChar switch
            {
                'A' => 'T',
                'T' => 'A',
                'C' => 'G',
                'G' => 'C',
                _ => baseChar
            };
        }

        return new string(complement);
    }
}
