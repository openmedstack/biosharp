namespace OpenMedStack.BioSharp.Model;

/// <summary>
/// Full annotation for a single variant, including predicted consequence,
/// HGVS notations at c. and p. levels, and helper methods to build the
/// protein change string.
/// </summary>
public record VariantAnnotation
{
    /// <summary>Chromosome or reference name.</summary>
    public string Chromosome { get; init; } = string.Empty;

    /// <summary>1-based position on the reference.</summary>
    public int Position { get; init; }

    /// <summary>Reference allele.</summary>
    public string Reference { get; init; } = string.Empty;

    /// <summary>Alternate allele.</summary>
    public string Alternate { get; init; } = string.Empty;

    /// <summary>
    /// Combined HGVS notation string (e.g. "NM_001.1:c.100A>T").
    /// Convenience alias for HgvsCoding when set.
    /// </summary>
    public required string HgvsNotation { get; init; }

    /// <summary>
    /// Clinical significance (from ClinVar or manual annotation).
    /// </summary>
    public string ClinicalSignificance { get; init; } = string.Empty;
    /// <summary>
    /// Gene or transcript identifier the annotation was made against.
    /// </summary>
    public string? AffectedGene { get; init; }

    /// <summary>
    /// Biological consequence category.
    /// </summary>
    public VariantConsequence Consequence { get; init; }

    /// <summary>
    /// HGVS coding DNA notation, e.g. <c>c.76A&gt;G</c>.
    /// </summary>
    public string? HgvsCoding { get; init; }

    /// <summary>
    /// HGVS protein change string, e.g. <c>p.A25V</c>, <c>p.Arg25*</c>, <c>p.Gly25del</c>.
    /// </summary>
    public string? HgvsProtein { get; init; }

    /// <summary>
    /// Original amino acid at the variant position, or null for non-coding changes.
    /// </summary>
    public AminoAcid? AffectedAminoAcid { get; init; }

    /// <summary>
    /// Amino acid after the variant, or null if the consequence is non-coding / frameshift (pre-new-stop).
    /// </summary>
    public AminoAcid? ResultingAminoAcid { get; init; }

    /// <summary>
    /// The codon nucleotide change, e.g. <c>GCT&gt;GTT</c>, or null if not applicable.
    /// </summary>
    public string? CodonChange { get; init; }

    /// <summary>
    /// Frameshift offset: number of amino acids from the shift position to the new stop codon.
    /// Only valid when Consequence is Frameshift.
    /// </summary>
    public int? FrameshiftOffset { get; init; }

    /// <summary>
    /// Returns the HGVS protein string (convenience access).
    /// </summary>
    public string ToProteinString()
    {
        return HgvsProtein ?? "(no protein change)";
    }
}
