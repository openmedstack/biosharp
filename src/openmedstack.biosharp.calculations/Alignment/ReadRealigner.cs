using OpenMedStack.BioSharp.Model.Alignment;

namespace OpenMedStack.BioSharp.Calculations.Alignment;

using System;
using System.Collections.Generic;
using Model;

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
    private readonly Dictionary<(bool IsLeftClip, int BoundaryPosition), (int Start, int Length)> _windowCache = new();

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
    /// Skip clip realignment when the clip is dominated by a single base.
    /// </summary>
    public bool SkipLowComplexityClips { get; set; } = true;

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

        /// <summary>
        /// True when the clip was rejected by a heuristic (low complexity or below size/fraction
        /// threshold) rather than being attempted and failing to align.
        /// The pipeline uses this to distinguish heuristic skips from genuine unmapped clips.
        /// </summary>
        public bool IsSkippedByHeuristic { get; }

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
            bool isSv,
            bool isSkippedByHeuristic = false)
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
            IsSkippedByHeuristic = isSkippedByHeuristic;
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
        Sequence read)
    {
        var results = new List<RealignmentResult>();
        var readData = read.GetData().Span;
        var readLen = readData.Length;

        // Left clip: bases at the 5' end of the read that were not aligned
        if (alignment.LeftSoftClip > 0)
        {
            var clipLen = alignment.LeftSoftClip;
            if (clipLen >= readLen * MinClipFraction && clipLen >= MinClipSize)
            {
                var result = RealignClip(readData[..clipLen], clipLen, true, 0, alignment.ReferenceStartPosition,
                    reference, chromosome);
                results.Add(result);
            }
            else
            {
                // Below threshold — record as a heuristic skip so the pipeline can count it.
                results.Add(new RealignmentResult(
                    $"Left clip at 0 ({clipLen} bp): below size/fraction threshold",
                    new string(readData[..clipLen]), null, string.Empty,
                    alignment.ReferenceStartPosition, false, null, 0, clipLen, true, chromosome, false,
                    isSkippedByHeuristic: true));
            }
        }

        // Right clip
        if (alignment.RightSoftClip > 0)
        {
            var clipLen = alignment.RightSoftClip;
            if (clipLen >= readLen * MinClipFraction && clipLen >= MinClipSize)
            {
                var clipStartInRead = readLen - clipLen;
                var refEndPos = alignment.ReferenceStartPosition +
                    DnaEncoding.CountNonGap(alignment.AlignedReference.AsSpan());
                if (refEndPos < reference.Length && clipStartInRead >= 0)
                {
                    var result = RealignClip(readData.Slice(clipStartInRead, clipLen), clipLen, false, clipStartInRead,
                        refEndPos,
                        reference, chromosome);
                    results.Add(result);
                }
            }
            else
            {
                var clipStartInRead = readLen - clipLen;
                results.Add(new RealignmentResult(
                    $"Right clip at {clipStartInRead} ({clipLen} bp): below size/fraction threshold",
                    new string(readData.Slice(clipStartInRead, clipLen)), null, string.Empty,
                    alignment.ReferenceStartPosition, false, null, clipStartInRead, clipLen, false, chromosome, false,
                    isSkippedByHeuristic: true));
            }
        }

        return results.ToArray();
    }

    public RealignmentResult[] Realign(
        AlignmentResult alignment,
        Sequence reference,
        string chromosome,
        string readSequence)
    {
        var syntheticRead = new Sequence(
            $"realign_{alignment.ReferenceStartPosition}",
            readSequence.AsMemory(),
            new string('I', readSequence.Length).AsMemory());

        return Realign(alignment, reference, chromosome, syntheticRead);
    }

    private RealignmentResult RealignClip(
        ReadOnlySpan<char> clipSequence,
        int clipSize,
        bool isLeftClip,
        int clipPositionInRead,
        int boundaryPosition,
        Sequence reference,
        string chromosome)
    {
        if (SkipLowComplexityClips && DnaEncoding.IsLowComplexity(clipSequence))
        {
            return new RealignmentResult(
                $"Clip at {clipPositionInRead} ({clipSize} bp, left={isLeftClip}): skipped low-complexity clip",
                new string(clipSequence), null, string.Empty, boundaryPosition, false, null,
                clipPositionInRead, clipSize, isLeftClip, chromosome, false, isSkippedByHeuristic: true);
        }

        (int Start, int Length) window = default;
        lock (_windowCache)
        {
            if (!_windowCache.TryGetValue((isLeftClip, boundaryPosition), out window))
            {
                var windowStart = isLeftClip
                    ? Math.Max(0, boundaryPosition - LocalWindowSize)
                    : boundaryPosition;
                var windowEnd = isLeftClip
                    ? boundaryPosition
                    : Math.Min(reference.Length, boundaryPosition + LocalWindowSize);
                window = (windowStart, windowEnd - windowStart);
                _windowCache.Add((isLeftClip, boundaryPosition), window);
            }
        }

        if (window.Length <= 0)
        {
            return new RealignmentResult(
                "Clip at boundary: no reference window available", new string(clipSequence), null,
                string.Empty, window.Start, false, null, clipPositionInRead, clipSize, isLeftClip, chromosome, false);
        }

        var refWindowSpan = reference.GetData().Span.Slice(window.Start, window.Length);

        AlignmentResult? alignResult;
        var isSv = false;
        SvType? svType = null;

        try
        {
            alignResult = SmithWatermanAligner.Align(
                refWindowSpan,
                clipSequence,
                2,
                -3,
                -5,
                -2,
                MinRealignScore);
        }
        catch
        {
            return new RealignmentResult(
                "Realignment failed", new string(clipSequence), null, new string(refWindowSpan),
                window.Start, false, null, clipPositionInRead, clipSize, isLeftClip, chromosome, false);
        }

        if (alignResult != null && alignResult.Score >= MinRealignScore)
        {
            const bool wasRealigned = true;

            // Check for structural variant indicators
            var refConsumed = DnaEncoding.CountNonGap(alignResult.AlignedReference.AsSpan());
            var readConsumed = DnaEncoding.CountNonGap(alignResult.AlignedRead.AsSpan());
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
            else //if (alignResult is { AlignedReference: not null, AlignedRead: not null })
            {
                // Check for inversion: do the clipped bases match the reverse complement of the reference?
                var altMatched = DnaEncoding.ExtractUngapped(alignResult.AlignedRead.AsSpan());
                var refMatched = DnaEncoding.ExtractUngapped(alignResult.AlignedReference.AsSpan());
                if (altMatched.Length == refMatched.Length && altMatched.Length > 0)
                {
                    var altRevComp = DnaEncoding.ReverseComplement(altMatched.AsSpan());
                    if (altRevComp == refMatched)
                    {
                        isSv = true;
                        svType = SvType.Inversion;
                    }
                }
            }

            // Get the position where the clipped region aligns
            var alignPos = alignResult.ReferenceStartPosition + window.Start;

            var summary =
                $"Clip at {clipPositionInRead} ({clipSize} bp, left={isLeftClip}): realigned with score {alignResult.Score}";

            return new RealignmentResult(summary, new string(clipSequence), alignResult,
                new string(refWindowSpan), alignPos, wasRealigned, svType,
                clipPositionInRead, clipSize, isLeftClip, chromosome, isSv);
        }

        return new RealignmentResult(
            $"Clip at {clipPositionInRead} ({clipSize} bp, left={isLeftClip}): no realignment found",
            new string(clipSequence), null, new string(refWindowSpan), window.Start, false, null,
            clipPositionInRead, clipSize, isLeftClip, chromosome, false);
    }
}
