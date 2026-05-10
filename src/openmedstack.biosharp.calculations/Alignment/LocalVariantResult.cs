using System;
using System.Collections.Generic;
using OpenMedStack.BioSharp.Calculations.DeBruijn;

namespace OpenMedStack.BioSharp.Calculations.Alignment;

/// <summary>
/// A variant call derived from local alignment of a read against a reference,
/// or from De Bruijn graph analysis.
/// </summary>
public class LocalVariantResult
{
    /// <summary>Chromosome or reference name.</summary>
    public string Chromosome { get; set; } = null!;

    /// <summary>
    /// 1-based genomic position of the variant (leftmost position in VCF convention).
    /// </summary>
    public int Position { get; set; }

    /// <summary>The 1-based end position of a structural variant; 0 for SNPs/indels.</summary>
    public int EndPosition { get; set; }

    /// <summary>Reference allele (what is in the reference sequence).</summary>
    public string Reference { get; set; } = null!;

    /// <summary>Primary alternate allele (what the read shows for simple variants).</summary>
    public string Alternate { get; set; } = null!;

    /// <summary>
    /// Additional alternate alleles for multi-allelic variants.
    /// Only populated when multiple distinct alternate alleles are observed.
    /// Does not include the primary Alternate (index 0).
    /// </summary>
    public List<string> AdditionalAltAlleles { get; } = [];

    /// <summary>
    /// All alternate alleles including the primary one.
    /// </summary>
    public IReadOnlyList<string> AllAlternateAlleles
    {
        get
        {
            if (AdditionalAltAlleles.Count == 0)
            {
                return [Alternate];
            }

            var list = new List<string> { Alternate };
            list.AddRange(AdditionalAltAlleles);
            return list;
        }
    }

    /// <summary>
    /// True if this variant has multiple alternate alleles.
    /// </summary>
    public bool IsMultiAllelic
    {
        get { return AdditionalAltAlleles.Count > 0; }
    }

    /// <summary>
    /// Adds an alternate allele to this variant for multi-allelic VCF output.
    /// </summary>
    public void AddAltAllele(string altAllele)
    {
        if (string.IsNullOrWhiteSpace(altAllele))
        {
            throw new ArgumentException("Alternate allele must be non-empty.", nameof(altAllele));
        }

        if (altAllele.Equals(Alternate, StringComparison.OrdinalIgnoreCase))
        {
            return; // Skip duplicates
        }

        AdditionalAltAlleles.Add(altAllele);
    }

    /// <summary>Phred-scaled quality score (0-255, higher = more confident).</summary>
    public int QuantitativeQuality { get; set; }

    /// <summary>
    /// True if QUAL >= FilterThreshold (default 30); false otherwise.
    /// </summary>
    public bool IsPass
    {
        get { return QuantitativeQuality >= FilterThreshold; }
    }

    /// <summary>Minimum quality score to be considered PASS.</summary>
    public const int FilterThreshold = 30;

    /// <summary>
    /// True if this is an insertion (read has an extra base vs reference).
    /// </summary>
    public bool IsInsertion
    {
        get { return Alternate.Length > Reference.Length; }
    }

    /// <summary>
    /// True if this is a deletion (reference has extra base(s) vs read).
    /// </summary>
    public bool IsDeletion
    {
        get { return Reference.Length > Alternate.Length; }
    }

    /// <summary>
    /// True if this is a substitution/SNP (same length, different base).
    /// </summary>
    public bool IsSubstitution
    {
        get { return Reference.Length == Alternate.Length && Reference != Alternate; }
    }

    /// <summary>Read depth supporting this variant.</summary>
    public int Depth { get; set; } = 1;

    // ====== Structural variant fields ======

    /// <summary>
    /// True if this variant was detected by De Bruijn graph analysis as a structural variant
    /// (bubble with length diff >= 5, or a long tip).
    /// </summary>
    public bool IsStructuralVariant { get; set; }

    /// <summary>
    /// When IsStructuralVariant is true, the type of structural variant.
    /// </summary>
    public SvType? SvType { get; set; }

    /// <summary>
    /// Information about the graph assembly that produced this variant.
    /// </summary>
    public AssemblyInfo? AssemblyInfo { get; set; }

    /// <summary>
    /// Soft-clip information from the read alignment, if this variant came from a realigned clipped region.
    /// </summary>
    public ClipInfo? ClipInfo { get; set; }

    /// <summary>
    /// Genotype call for diploid organisms, computed from ref and alt coverage ratios.
    /// Null if genotype calling was not performed.
    /// </summary>
    public Genotype? Genotype { get; set; }

    /// <summary>
    /// Bubble confidence score from RepetitivenessAnalyzer.
    /// Set when a variant was detected from De Bruijn bubble analysis.
    /// Null if no bubble confidence was computed.
    /// </summary>
    public RepetitivenessScore? BubbleConfidence { get; set; }

    /// <summary>
    /// Optional free-text additional INFO field content for VCF output.
    /// Used by callers (e.g. CopyNumberCaller) to attach extra key=value pairs.
    /// </summary>
    public string? AdditionalInformation { get; set; }
}

/// <summary>
/// Information about the De Bruijn graph assembly that produced a variant.
/// </summary>
public class AssemblyInfo
{
    /// <summary>Average coverage across all paths in the bubble/tip.</summary>
    public int Coverage { get; }

    /// <summary>Length of the alternative path sequence.</summary>
    public int AltPathLength { get; }

    /// <summary>Number of distinct alt paths contributing to consensus.</summary>
    public int AltPathCount { get; }

    public AssemblyInfo(int coverage, int altPathLength, int altPathCount = 1)
    {
        Coverage = coverage;
        AltPathLength = altPathLength;
        AltPathCount = altPathCount;
    }

    public override string ToString()
    {
        return $"[cov:{Coverage}, altLen:{AltPathLength}, paths:{AltPathCount}]";
    }
}

/// <summary>
/// Soft-clip information for a variant detected from a realigned clipped region.
/// </summary>
public class ClipInfo
{
    /// <summary>Position of the clip boundary on the original read (0-based).</summary>
    public int ClipPosition { get; }

    /// <summary>Number of bases in the clipped region.</summary>
    public int ClipSize { get; }

    /// <summary>Whether this was a 5' (left) or 3' (right) clip.</summary>
    public bool IsLeftClip { get; }

    /// <summary>Did the clipped region successfully realign?</summary>
    public bool WasRealigned { get; }

    /// <summary>
    /// Alignment score of the clipped region re-alignment.
    /// </summary>
    public int RealignScore { get; }

    public ClipInfo(int clipPosition, int clipSize, bool isLeftClip, bool wasRealigned, int realignScore)
    {
        ClipPosition = clipPosition;
        ClipSize = clipSize;
        IsLeftClip = isLeftClip;
        WasRealigned = wasRealigned;
        RealignScore = realignScore;
    }

    public override string ToString()
    {
        return
            $"[clip:{ClipPosition}, size:{ClipSize}, left:{IsLeftClip}, realigned:{WasRealigned}, score:{RealignScore}]";
    }
}
