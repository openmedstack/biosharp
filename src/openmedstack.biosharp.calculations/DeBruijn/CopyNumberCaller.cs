using OpenMedStack.BioSharp.Model;
using OpenMedStack.BioSharp.Model.Alignment;

namespace OpenMedStack.BioSharp.Calculations.DeBruijn;

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

/// <summary>
/// Calls copy-number variants (CNVs) from per-position read depth by:
/// <list type="number">
///   <item>Binning depths into configurable windows.</item>
///   <item>Applying a GC-content correction per window.</item>
///   <item>Segmenting normalised depth with a simple CBS-style change-point algorithm.</item>
///   <item>Calling DEL / DUP / CNV records for segments that deviate from the diploid baseline (CN=2).</item>
/// </list>
///
/// <para>
/// Output variants are expressed as <see cref="LocalVariantResult"/> records with
/// <see cref="LocalVariantResult.IsStructuralVariant"/> = <c>true</c>, using
/// <see cref="SvType.Deletion"/> or <see cref="SvType.Duplication"/>.
/// The <see cref="LocalVariantResult.AdditionalInformation"/> field carries
/// <c>CN=N</c> for the integer copy-number call and
/// the standard SV INFO fields <c>SVLEN</c>, <c>END</c>, <c>CIPOS</c>, <c>CIEND</c>.
/// </para>
/// </summary>
public sealed class CopyNumberCaller
{
    /// <summary>Window size in base-pairs (default 1000 bp).</summary>
    public int WindowSize { get; }

    /// <summary>
    /// Minimum fold-change relative to the baseline to call a DEL (default 0.6 = &lt; 60% of expected depth).
    /// Below this threshold the region is considered a deletion.
    /// </summary>
    public double DeletionThreshold { get; }

    /// <summary>
    /// Minimum fold-change relative to the baseline to call a DUP (default 1.5 = &gt; 150% of expected depth).
    /// Above this threshold the region is considered an amplification.
    /// </summary>
    public double DuplicationThreshold { get; }

    /// <summary>Minimum number of consecutive windows with the same CN call to report a segment.</summary>
    public int MinWindowsPerSegment { get; }

    public CopyNumberCaller(
        int windowSize = 1000,
        double deletionThreshold = 0.6,
        double duplicationThreshold = 1.5,
        int minWindowsPerSegment = 2)
    {
        if (windowSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(windowSize));
        }

        WindowSize = windowSize;
        DeletionThreshold = deletionThreshold;
        DuplicationThreshold = duplicationThreshold;
        MinWindowsPerSegment = minWindowsPerSegment;
    }

    /// <summary>
    /// Calls CNVs from per-position depth and reference sequence.
    /// </summary>
    /// <param name="referenceSequence">Full reference sequence, 0-based.</param>
    /// <param name="depths">Per-position depth, 0-based, same length as reference.</param>
    /// <param name="chromosome">Chromosome name for output records.</param>
    /// <returns>CNV calls as <see cref="LocalVariantResult"/> records.</returns>
    public IEnumerable<LocalVariantResult> Call(
        ReadOnlyMemory<char> referenceSequence,
        int[] depths,
        string chromosome)
    {
        var refLen = referenceSequence.Length;

        if (depths.Length < refLen)
        {
            throw new ArgumentException("depths must cover the entire reference.", nameof(depths));
        }

        var windowCount = (int)Math.Ceiling((double)refLen / WindowSize);
        var windowDepths = new double[windowCount];
        var windowGc = new double[windowCount];
        var refArray = referenceSequence;  // copy to avoid ref-struct capture issue

        // ── Step 1: bin depths and compute GC per window ──────────────────────
        Parallel.For(0, windowCount, w =>
        {
            var wStart = w * WindowSize;
            var wEnd = Math.Min(wStart + WindowSize, refLen);
            var wLen = wEnd - wStart;
            var localRef = refArray.Span;

            var sumDepth = 0L;
            var gcCount = 0;
            for (var i = wStart; i < wEnd; i++)
            {
                sumDepth += depths[i];
                var b = char.ToUpperInvariant(localRef[i]);
                if (b is 'G' or 'C')
                {
                    gcCount++;
                }
            }

            windowDepths[w] = wLen > 0 ? (double)sumDepth / wLen : 0.0;
            windowGc[w] = wLen > 0 ? (double)gcCount / wLen : 0.5;
        });

        // ── Step 2: GC correction ─────────────────────────────────────────────
        var corrected = GcCorrect(windowDepths, windowGc);

        // ── Step 3: estimate baseline (median of corrected depths) ────────────
        var baseline = Median(corrected);
        if (baseline <= 0)
        {
            baseline = 1.0; // guard
        }

        // ── Step 4: segment (CBS-lite: binary change-point search) ────────────
        var segments = Segment(corrected, baseline);

        // ── Step 5: call CNVs from segments ───────────────────────────────────
        var results = new List<LocalVariantResult>();
        foreach (var seg in segments)
        {
            var ratio = seg.MeanNormalisedDepth / baseline;
            var cn = (int)Math.Round(ratio * 2.0);
            cn = Math.Max(0, cn);

            SvType? svType;
            if (ratio < DeletionThreshold)
            {
                svType = SvType.Deletion;
            }
            else if (ratio > DuplicationThreshold)
            {
                svType = SvType.CopyNumber;
            }
            else
            {
                continue; // diploid — no call
            }

            if (seg.WindowCount < MinWindowsPerSegment)
            {
                continue;
            }

            var startPos = seg.StartWindow * WindowSize + 1;   // 1-based
            var endPos = Math.Min((seg.EndWindow + 1) * WindowSize, refLen); // 1-based inclusive
            var svLen = endPos - startPos + 1;

            var variant = new LocalVariantResult
            {
                Chromosome = chromosome,
                Position = startPos,
                EndPosition = endPos,
                Reference = new string(referenceSequence.Span[startPos - 1], 1),
                Alternate = svType == SvType.Deletion ? "<DEL>" : "<DUP>",
                QuantitativeQuality = ComputeQuality(ratio, seg.WindowCount),
                Depth = (int)seg.MeanNormalisedDepth,
                IsStructuralVariant = true,
                SvType = svType,
                AssemblyInfo = new AssemblyInfo(
                    (int)seg.MeanNormalisedDepth,
                    svLen,
                    seg.WindowCount),
                // Store CN in AdditionalInformation (used for INFO field in VCF)
                AdditionalInformation = $"CN={cn};SVLEN={svLen}"
            };

            results.Add(variant);
        }

        return results;
    }

    // ── GC correction ─────────────────────────────────────────────────────────

    /// <summary>
    /// Corrects window depths for GC bias using a LOESS-like binned correction.
    /// Groups windows into 20 GC bins, computes the median depth per bin,
    /// normalises each window by (globalMedian / binMedian).
    /// </summary>
    private static double[] GcCorrect(double[] windowDepths, double[] windowGc)
    {
        const int gcBins = 20;
        var binSums = new double[gcBins];
        var binCounts = new int[gcBins];

        for (var w = 0; w < windowDepths.Length; w++)
        {
            var bin = Math.Min((int)(windowGc[w] * gcBins), gcBins - 1);
            binSums[bin] += windowDepths[w];
            binCounts[bin]++;
        }

        var globalMedian = Median(windowDepths);

        var corrected = new double[windowDepths.Length];
        for (var w = 0; w < windowDepths.Length; w++)
        {
            var bin = Math.Min((int)(windowGc[w] * gcBins), gcBins - 1);
            var binMean = binCounts[bin] > 0 ? binSums[bin] / binCounts[bin] : globalMedian;
            var factor = binMean > 0 ? globalMedian / binMean : 1.0;
            corrected[w] = windowDepths[w] * factor;
        }

        return corrected;
    }

    // ── Segmentation (CBS-lite) ───────────────────────────────────────────────

    private readonly record struct CnvSegment(int StartWindow, int EndWindow, double MeanNormalisedDepth)
    {
        public int WindowCount
        {
            get { return EndWindow - StartWindow + 1; }
        }
    }

    /// <summary>
    /// Iteratively splits the window array into segments where the mean depth
    /// differs significantly from the global baseline, using a greedy sliding
    /// window approach similar to a single-pass CBS.
    /// </summary>
    private static List<CnvSegment> Segment(double[] corrected, double baseline)
    {
        var segments = new List<CnvSegment>();

        if (corrected.Length == 0)
        {
            return segments;
        }

        // Use a two-pointer scan: accumulate windows until the running mean
        // crosses a change threshold (>20% deviation from baseline), then emit.
        var start = 0;

        while (start < corrected.Length)
        {
            var end = start;
            var sum = corrected[start];

            while (end + 1 < corrected.Length)
            {
                var nextMean = corrected[end + 1];

                // If the next window's depth is dramatically different from the
                // current block mean (>30% change), start a new segment.
                if (Math.Abs(nextMean - sum / (end - start + 1)) > 0.3 * baseline)
                {
                    break;
                }

                end++;
                sum += corrected[end];
            }

            var mean = sum / (end - start + 1);
            segments.Add(new CnvSegment(start, end, mean));
            start = end + 1;
        }

        // Merge adjacent segments with similar mean (within 20% of baseline)
        return MergeSegments(segments, baseline);
    }

    private static List<CnvSegment> MergeSegments(List<CnvSegment> segments, double baseline)
    {
        if (segments.Count <= 1)
        {
            return segments;
        }

        var merged = new List<CnvSegment> { segments[0] };

        for (var i = 1; i < segments.Count; i++)
        {
            var last = merged[^1];
            var curr = segments[i];

            var lastRatio = last.MeanNormalisedDepth / baseline;
            var currRatio = curr.MeanNormalisedDepth / baseline;

            // Merge if both are in the same copy-number tier (within 15% of each other)
            if (Math.Abs(lastRatio - currRatio) < 0.15)
            {
                var totalWindows = last.WindowCount + curr.WindowCount;
                var newMean = (last.MeanNormalisedDepth * last.WindowCount +
                               curr.MeanNormalisedDepth * curr.WindowCount) / totalWindows;
                merged[^1] = new CnvSegment(last.StartWindow, curr.EndWindow, newMean);
            }
            else
            {
                merged.Add(curr);
            }
        }

        return merged;
    }

    // ── Utilities ─────────────────────────────────────────────────────────────

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int ComputeQuality(double foldChange, int windowCount)
    {
        // Simple quality: proportional to deviation from 1.0 and coverage count
        var deviation = Math.Abs(foldChange - 1.0);
        var q = (int)(deviation * 30 * Math.Log10(windowCount + 1));
        return Math.Min(99, Math.Max(10, q));
    }

    private static double Median(double[] values)
    {
        if (values.Length == 0)
        {
            return 0.0;
        }

        var copy = new double[values.Length];
        values.AsSpan().CopyTo(copy);
        Array.Sort(copy);

        var mid = copy.Length / 2;
        return copy.Length % 2 == 0
            ? (copy[mid - 1] + copy[mid]) / 2.0
            : copy[mid];
    }
}
