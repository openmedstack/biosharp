namespace OpenMedStack.BioSharp.Model;

/// <summary>
/// Categories of biological consequence for annotated variants.
/// </summary>
public enum VariantConsequence
{
    /// <summary>
    /// c. change but same amino acid -- no protein effect.
    /// </summary>
    Synonymous,

    /// <summary>
    /// c. change results in a different amino acid.
    /// </summary>
    Missense,

    /// <summary>
    /// c. change creates a premature stop codon (*).
    /// </summary>
    Nonsense,

    /// <summary>
    /// Indel not divisible by 3, shifts reading frame, produces a new stop codon.
    /// </summary>
    Frameshift,

    /// <summary>
    /// Insertion of n nucleotides where n % 3 == 0; adds amino acids.
    /// </summary>
    InframeInsertion,

    /// <summary>
    /// Deletion of n nucleotides where n % 3 == 0; removes amino acids.
    /// </summary>
    InframeDeletion,

    /// <summary>
    /// Variant within 3 bp of an exon-intron boundary.
    /// </summary>
    SpliceSite,

    /// <summary>
    /// Within 3 kb upstream of the start codon.
    /// </summary>
    Upstream,

    /// <summary>
    /// Within 3 kb downstream of the stop codon.
    /// </summary>
    Downstream,

    /// <summary>
    /// Not in any gene or transcript.
    /// </summary>
    Intergenic,

    /// <summary>
    /// Within intronic region (not near splice site).
    /// </summary>
    Intronic,

    /// <summary>
    /// In an untranslated region (5' or 3' UTR).
    /// </summary>
    VariantInUtr,

    /// <summary>
    /// Nonsense in the very last codon -- technically functional.
    /// </summary>
    StopRetained,

    /// <summary>
    /// Variants where the VCF reference allele does not match the expected
    /// transcript reference at that position — annotation is uncertain.
    /// </summary>
    Uncertain,

    /// <summary>
    /// Could not be annotated (e.g. out-of-bounds, unknown transcript).
    /// </summary>
    Unknown,

    /// <summary>
    /// Variant alters the splice site PWM score beyond the configured threshold,
    /// functionally disrupting the canonical donor or acceptor signal.
    /// </summary>
    SpliceSiteDisruptive,

    /// <summary>
    /// Variant restores or creates a new splice site in a non-canonical (typically intronic)
    /// position, potentially activating a cryptic splice donor or acceptor.
    /// </summary>
    CrypticSpliceActivation
}
