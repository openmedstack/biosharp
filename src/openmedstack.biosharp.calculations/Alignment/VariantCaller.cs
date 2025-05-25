namespace OpenMedStack.BioSharp.Calculations.Alignment;

using System;
using System.Collections.Generic;
using System.Linq;
using Model;

/// <summary>
/// Extracts variant calls from Smith-Waterman alignment results.
/// Converts aligned read/ref sequences into VCF-compatible variant calls.
/// </summary>
public static class VariantCaller
{
    /// <summary>
    /// Call variants from an alignment, returning a list of LocalVariantResult.
    /// Groups consecutive events (SNPs/indels) into multi-base variants per VCF convention.
    /// </summary>
    public static LocalVariantResult[] CallVariants(
        Sequence reference,
        AlignmentResult alignment,
        int minQuality = 30)
    {
        if (reference == null) throw new ArgumentNullException(nameof(reference));

        var refSeq = reference.GetData()!.Span;
        var events = ParseAlignmentEvents(alignment);

        // Compute homopolymer run lengths for indel events
        foreach (var evt in events)
            if (evt.IsIndel)
            {
                var absPos = alignment.ReferenceStartPosition + evt.Position;
                evt.HomopolymerRun = GetHomopolymerRun(refSeq, absPos);
            }

        if (events.Count == 0) return [];

        var groups = GroupVariants(events);

        var results = new List<LocalVariantResult>();
        foreach (var group in groups)
        {
            var variant = BuildVariant(reference.Id, refSeq, alignment.ReferenceStartPosition, group);
            variant.QuantitativeQuality = CalculateQuality(group, alignment.Score);
            if (variant.QuantitativeQuality >= minQuality) results.Add(variant);
        }

        return results.ToArray();
    }

    /// <summary>
    /// Parses alignment strings into atomic events (SNP, insertion, deletion).
    /// </summary>
    private static List<AlignmentEvent> ParseAlignmentEvents(AlignmentResult alignment)
    {
        var events = new List<AlignmentEvent>();
        var refStr = alignment.AlignedReference;
        var readStr = alignment.AlignedRead;

        for (var e = 0; e < refStr.Length; e++)
        {
            var refChar = refStr[e];
            var readChar = readStr[e];

            if (refChar != '-' && readChar == '-')
                events.Add(new AlignmentEvent(EventType.Deletion, e, readChar));
            else if (refChar == '-' && readChar != '-')
                events.Add(new AlignmentEvent(EventType.Insertion, e, readChar));
            else if (char.ToUpper(refChar) != char.ToUpper(readChar))
                events.Add(new AlignmentEvent(EventType.Snp, e, readChar));
        }

        return events;
    }

    /// <summary>
    /// Groups consecutive alignment events into variant groups.
    /// SNPs group with SNPs. Indels group with indels. Mixed stays separate.
    /// </summary>
    private static List<VariantGroup> GroupVariants(List<AlignmentEvent> events)
    {
        var groups = new List<VariantGroup>();
        if (events.Count == 0) return groups;

        var current = new VariantGroup { StartIndex = events[0].Position };
        current.Add(events[0]);

        for (var i = 1; i < events.Count; i++)
        {
            var evt = events[i];
            var gap = evt.Position - events[i - 1].Position;

            // SNP next to an indel at the same position — separate group
            if (current.HasIndel && evt.IsSubstitution)
            {
                groups.Add(current);
                current = new VariantGroup { StartIndex = evt.Position };
                current.Add(evt);
                continue;
            }

            if (gap <= 1 && AreSameCategory(current.FirstEventType, evt.EventType))
            {
                current.Add(evt);
            }
            else
            {
                groups.Add(current);
                current = new VariantGroup { StartIndex = evt.Position };
                current.Add(evt);
            }
        }

        if (current.Events.Count > 0) groups.Add(current);

        return groups;
    }

    private static bool AreSameCategory(EventType a, EventType b)
    {
        if (a == EventType.Snp && b == EventType.Snp) return true;

        if ((a == EventType.Insertion || a == EventType.Deletion) &&
            (b == EventType.Insertion || b == EventType.Deletion))
            return true;

        return false;
    }

    /// <summary>
    /// Build VCF alleles from a variant group using the leftmost-anchor convention.
    /// Per VCF spec:
    ///   - SNP: REF = anchor + refBase, ALT = anchor + altBase
    ///   - Deletion: REF = anchor + delBases, ALT = anchor
    ///   - Insertion: REF = anchor, ALT = anchor + insBases
    /// </summary>
    private static LocalVariantResult BuildVariant(
        string chrom,
        ReadOnlySpan<char> refSeq,
        int refStart,
        VariantGroup group)
    {
        var firstRelPos = group.StartIndex; // relative to alignment start
        var anchorRefPos = refStart + firstRelPos - 1; // 0-based index for anchor (1 before first event)
        var vcfPos1Based = refStart + firstRelPos + 1; // 1-based VCF position

        var variant = new LocalVariantResult
        {
            Chromosome = chrom,
            Position = vcfPos1Based
        };

        // Check if we have a valid anchor
        if (anchorRefPos < 0 || anchorRefPos >= refSeq.Length || refSeq[anchorRefPos] == '-')
        {
            // No anchor — fall back to simple alleles (not VCF-compliant but better than nothing)
            BuildAllelesNoAnchor(chrom, refSeq, refStart, group, variant);
            return variant;
        }

        var anchor = char.ToUpper(refSeq[anchorRefPos]);

        var delBases = new System.Text.StringBuilder();
        var insBases = new System.Text.StringBuilder();
        var snpRefBases = new System.Text.StringBuilder();
        var snpAltBases = new System.Text.StringBuilder();

        foreach (var evt in group.Events)
        {
            var absPos = refStart + evt.Position;

            if (evt.EventType == EventType.Snp)
            {
                if (absPos < refSeq.Length && refSeq[absPos] != '-')
                {
                    snpRefBases.Append(char.ToUpper(refSeq[absPos]));
                    snpAltBases.Append(char.ToUpper(evt.Base));
                }
            }
            else if (evt.EventType == EventType.Deletion)
            {
                if (absPos < refSeq.Length && refSeq[absPos] != '-') delBases.Append(char.ToUpper(refSeq[absPos]));
            }
            else if (evt.EventType == EventType.Insertion)
            {
                insBases.Append(char.ToUpper(evt.Base));
            }
        }

        var hasIns = insBases.Length > 0;
        var hasDel = delBases.Length > 0;

        if (hasDel && !hasIns)
        {
            // Deletion: REF = anchor + delBases, ALT = anchor
            variant.Reference = anchor + delBases.ToString();
            variant.Alternate = anchor.ToString();
        }
        else if (hasIns && !hasDel)
        {
            // Insertion: REF = anchor, ALT = anchor + insBases
            variant.Reference = anchor.ToString();
            variant.Alternate = anchor + insBases.ToString();
        }
        else if (snpRefBases.Length > 0)
        {
            // SNP(s): REF = anchor + refBases, ALT = anchor + altBases
            variant.Reference = anchor + snpRefBases.ToString();
            variant.Alternate = anchor + snpAltBases.ToString();
        }
        else if (hasDel && hasIns)
        {
            // Mixed del+ins (delins): REF = anchor + delBases, ALT = anchor + insBases
            variant.Reference = anchor + delBases.ToString();
            variant.Alternate = anchor + insBases.ToString();
        }

        return variant;
    }

    /// <summary>
    /// Fallback when there's no valid anchor (e.g., variant at position 1 of reference).
    /// Build alleles without the anchor base.
    /// </summary>
    private static void BuildAllelesNoAnchor(
        string chrom,
        ReadOnlySpan<char> refSeq,
        int refStart,
        VariantGroup group,
        LocalVariantResult variant)
    {
        var delBases = new System.Text.StringBuilder();
        var insBases = new System.Text.StringBuilder();
        var snpRefBases = new System.Text.StringBuilder();
        var snpAltBases = new System.Text.StringBuilder();

        foreach (var evt in group.Events)
        {
            var absPos = refStart + evt.Position;

            if (evt.EventType == EventType.Snp)
            {
                if (absPos < refSeq.Length && refSeq[absPos] != '-')
                {
                    snpRefBases.Append(char.ToUpper(refSeq[absPos]));
                    snpAltBases.Append(char.ToUpper(evt.Base));
                }
            }
            else if (evt.EventType == EventType.Deletion)
            {
                if (absPos < refSeq.Length && refSeq[absPos] != '-') delBases.Append(char.ToUpper(refSeq[absPos]));
            }
            else if (evt.EventType == EventType.Insertion)
            {
                insBases.Append(char.ToUpper(evt.Base));
            }
        }

        if (snpRefBases.Length > 0)
        {
            variant.Reference = snpRefBases.Length > 0 ? snpRefBases.ToString() : "N";
            variant.Alternate = snpAltBases.Length > 0 ? snpAltBases.ToString() : "N";
        }
        else if (delBases.Length > 0)
        {
            variant.Reference = delBases.Length > 0 ? delBases.ToString() : "N";
            variant.Alternate = "";
        }
        else if (insBases.Length > 0)
        {
            variant.Reference = "N";
            variant.Alternate = insBases.ToString();
        }

        variant.Chromosome = chrom;
    }

    /// <summary>
    /// Estimate quality from alignment score and event count.
    /// Returns Phred-scaled score capped at 255.
    /// </summary>
    private static int CalculateQuality(VariantGroup group, int totalAlignmentScore)
    {
        if (group.Events.Count == 0) return 0;

        var baseQuality = totalAlignmentScore / Math.Max(1, group.Events.Count);
        baseQuality = Math.Max(10, Math.Min(40, baseQuality));

        if (group.HasIndel) baseQuality = Math.Max(10, baseQuality - 5); // indels are less reliable

        // Homopolymer quality penalty: indels in repetitive runs (AAAA, TTTTT, etc.)
        // are common sequencing errors and need aggressive quality filtering per spec.
        var isHomopolymer = IsHomopolymerEvent(group);
        if (isHomopolymer) baseQuality = Math.Max(10, baseQuality - 10); // extra penalty for homopolymer indels

        return Math.Min(255, baseQuality);
    }

    /// <summary>
    /// Checks if a variant group contains a homopolymer indel event.
    /// Homopolymer runs (e.g. AAAAA, TTTTT) are common sources of
    /// sequencing errors in indel calling and should be downweighted.
    /// </summary>
    private static bool IsHomopolymerEvent(VariantGroup group)
    {
        // Only relevant for indels, not SNPs
        if (!group.HasIndel) return false;

        // Check if any event occurs in a homopolymer region of the reference
        foreach (var evt in group.Events)
            if (evt.EventType == EventType.Insertion || evt.EventType == EventType.Deletion)
                // Check the ref context: are we within a run of 4+ identical bases?
                if (evt.HomopolymerRun >= 4)
                    return true;

        return false;
    }

    /// <summary>
    /// Returns the length of the homopolymer run at the given position in the reference.
    /// E.g., ref[5..10] = "AAAAA" → returns 5 if position is inside that run.
    /// </summary>
    public static int GetHomopolymerRun(ReadOnlySpan<char> refSeq, int position)
    {
        if (refSeq.Length == 0) return 0;

        var refBase = char.ToUpper(refSeq[Math.Clamp(position, 0, refSeq.Length - 1)]);
        if (refBase != 'A' && refBase != 'C' && refBase != 'G' && refBase != 'T') return 0;

        // Count backwards
        var start = position;
        while (start > 0 && char.ToUpper(refSeq[start - 1]) == refBase) start--;

        // Count forwards
        var end = position;
        while (end < refSeq.Length - 1 && char.ToUpper(refSeq[end + 1]) == refBase) end++;

        return end - start + 1;
    }

    /// <summary>
    /// Merge variant calls from multiple reads at the same locus.
    /// Takes the highest quality and aggregates depth.
    /// </summary>
    public static LocalVariantResult[] MergeVariants(LocalVariantResult[] variants)
    {
        var merged = new Dictionary<string, LocalVariantResult>();

        foreach (var variant in variants)
        {
            var key = $"{variant.Chromosome}:{variant.Position}:{variant.Reference}:{variant.Alternate}";
            if (!merged.TryGetValue(key, out var existing))
            {
                merged[key] = variant;
            }
            else
            {
                existing.Depth++;
                if (variant.QuantitativeQuality > existing.QuantitativeQuality)
                    existing.QuantitativeQuality = variant.QuantitativeQuality;
            }
        }

        return merged.Values.ToArray();
    }
}
