namespace OpenMedStack.BioSharp.Model;

using System;
using System.Collections.Generic;

/// <summary>
/// Contextual information about a transcript used during variant consequence classification.
/// Holds CDS boundaries and optional intron/gene region data so non-coding consequence types
/// (SpliceSite, Upstream, Downstream, Intronic, VariantInUtr) can be returned for positions
/// outside the protein-coding region.
/// </summary>
public record AnnotationContext
{
    /// <summary>
    /// 1-based inclusive start of the CDS on the transcript.
    /// Positions within [CdsStart, CdsEnd] are considered coding.
    /// </summary>
    public int CdsStart { get; init; }

    /// <summary>
    /// 1-based inclusive end of the CDS on the transcript.
    /// </summary>
    public int CdsEnd { get; init; }

    /// <summary>
    /// Total length of the transcript sequence.
    /// </summary>
    public int TranscriptLength { get; init; }

    /// <summary>
    /// Optional gene boundaries. If null, gene is assumed to match the transcript.
    /// </summary>
    public (int Start, int End)? GeneBoundaries { get; init; }

    /// <summary>
    /// Optional intron coordinates (pairs of start/end positions).
    /// Positions that fall within introns and outside the splice window are Intronic.
    /// </summary>
    public IReadOnlyList<(int Start, int End)>? Introns { get; init; }

    /// <summary>
    /// Optional exon coordinate pairs (1-based inclusive start and end).
    /// Used by the splice site predictor to locate exon-intron boundaries for PWM scoring.
    /// </summary>
    public IReadOnlyList<(int Start, int End)>? ExonBoundaries { get; init; }

    /// <summary>
    /// Creates a basic AnnotationContext from CDS boundaries and transcript length.
    /// </summary>
    public static AnnotationContext FromCdsBoundaries(int cdsStart, int cdsEnd, int transcriptLength)
    {
        if (cdsStart < 1)
        {
            throw new ArgumentException("CDS start must be >= 1.", nameof(cdsStart));
        }

        if (cdsEnd < cdsStart)
        {
            throw new ArgumentException("CDS end must be >= CDS start.", nameof(cdsEnd));
        }

        if (transcriptLength < cdsEnd)
        {
            throw new ArgumentException("Transcript length must be >= CDS end.", nameof(transcriptLength));
        }

        return new AnnotationContext
        {
            CdsStart = cdsStart,
            CdsEnd = cdsEnd,
            TranscriptLength = transcriptLength
        };
    }

    /// <summary>
    /// Creates an AnnotationContext with full gene boundary and intron information.
    /// </summary>
    public static AnnotationContext FromCdsAndGeneBoundaries(
        int cdsStart,
        int cdsEnd,
        int geneStart,
        int geneEnd,
        IReadOnlyList<(int Start, int End)>? introns = null)
    {
        if (cdsStart < 1)
        {
            throw new ArgumentException("CDS start must be >= 1.", nameof(cdsStart));
        }

        if (cdsEnd < cdsStart)
        {
            throw new ArgumentException("CDS end must be >= CDS start.", nameof(cdsEnd));
        }

        if (geneStart < 1)
        {
            throw new ArgumentException("Gene start must be >= 1.", nameof(geneStart));
        }

        if (geneEnd < geneStart)
        {
            throw new ArgumentException("Gene end must be >= gene start.", nameof(geneEnd));
        }

        if (geneEnd < cdsEnd)
        {
            throw new ArgumentException("Gene end must be >= CDS end.", nameof(geneEnd));
        }

        return new AnnotationContext
        {
            CdsStart = cdsStart,
            CdsEnd = cdsEnd,
            TranscriptLength = geneEnd,
            GeneBoundaries = (geneStart, geneEnd),
            Introns = introns
        };
    }

    /// <summary>
    /// Classifies a genomic position relative to the CDS and optional intron/gene annotation,
    /// returning the appropriate VariantConsequence for non-coding variants.
    /// Returns null for positions within the CDS (those will be further classified by coding-variant logic).
    /// </summary>
    public VariantConsequence? ClassifyPosition(int position)
    {
        const int spliceWindow = 3;
        const int regionWindow = 3000;

        // Positions before the transcript are unknown
        if (position < 1)
        {
            return VariantConsequence.Unknown;
        }

        // Gene/intron annotation present: apply full logic
        if (GeneBoundaries != null)
        {
            var (geneStart, geneEnd) = GeneBoundaries.Value;

            // Position outside the entire gene -- intergenic
            if (position < geneStart || position > geneEnd)
            {
                return VariantConsequence.Intergenic;
            }

            // Multi-exon transcripts: check introns first (position may be between two coding exons)
            // Only do this when ExonBoundaries are available and we have more than one exon,
            // because in that case the position can be between CdsStart and CdsEnd yet be intronic.
            if (ExonBoundaries is { Count: > 1 })
            {
                foreach (var (intronStart, intronEnd) in Introns ?? [])
                {
                    if (position >= intronStart && position <= intronEnd)
                    {
                        // Check splice site proximity to nearest exon boundary
                        if (position - intronStart < spliceWindow || intronEnd - position < spliceWindow)
                        {
                            return VariantConsequence.SpliceSite;
                        }

                        return VariantConsequence.Intronic;
                    }
                }
            }

            // Splice site: within 3bp of exon-intron boundary (at CDS edges)
            if (position >= CdsStart - spliceWindow && position < CdsStart)
            {
                return VariantConsequence.SpliceSite;
            }

            if (position > CdsEnd && position <= CdsEnd + spliceWindow)
            {
                return VariantConsequence.SpliceSite;
            }

            // Within CDS -- coding region; let coding-variant classifier handle it
            if (position >= CdsStart && position <= CdsEnd)
            {
                return null;
            }

            // Introns have priority over upstream/downstream (single-exon or no ExonBoundaries)
            foreach (var (intronStart, intronEnd) in Introns ?? [])
            {
                if (position < intronStart || position > intronEnd)
                {
                    continue;
                }

                if (position - intronStart < spliceWindow || intronEnd - position < spliceWindow)
                {
                    return VariantConsequence.SpliceSite;
                }

                return VariantConsequence.Intronic;
            }

            // Check if position is upstream or downstream of CDS
            if (position >= geneStart && position < CdsStart)
            {
                return CdsStart - position <= regionWindow
                    ? VariantConsequence.Upstream
                    : VariantConsequence.Intergenic;
            }

            if (position > CdsEnd && position <= geneEnd)
            {
                return position - CdsEnd <= regionWindow
                    ? VariantConsequence.Downstream
                    : VariantConsequence.Intergenic;
            }

            // Fallback: in gene but not upstream/downstream
            return VariantConsequence.Intergenic;
        }

        // No gene/intron annotation: transcript-level logic (no splice window)
        // Within CDS — coding region; let coding-variant classifier handle it
        if (position >= CdsStart && position <= CdsEnd)
        {
            return null;
        }

        // Beyond transcript -- intergenic
        if (position > TranscriptLength)
        {
            return VariantConsequence.Intergenic;
        }

        // Within 3kb before CDS -- upstream
        if (position < CdsStart && CdsStart - position <= regionWindow)
        {
            return VariantConsequence.Upstream;
        }

        // Within 3kb after CDS -- downstream
        if (position > CdsEnd && position - CdsEnd <= regionWindow)
        {
            return VariantConsequence.Downstream;
        }

        // Within transcript but outside CDS and beyond 3kb -> Intergenic
        // (without gene boundaries, anything more than 3kb from CDS is intergenic)
        if (position < CdsStart)
        {
            return CdsStart - position > regionWindow ? VariantConsequence.Intergenic : VariantConsequence.VariantInUtr;
        }

        if (position > CdsEnd)
        {
            return position - CdsEnd > regionWindow ? VariantConsequence.Intergenic : VariantConsequence.VariantInUtr;
        }

        // Fallback (within transcript, outside CDS but within 3kb)
        return VariantConsequence.VariantInUtr;
    }
}
