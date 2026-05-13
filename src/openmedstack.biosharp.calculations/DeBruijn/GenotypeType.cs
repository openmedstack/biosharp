namespace OpenMedStack.BioSharp.Calculations.DeBruijn;

/// <summary>
/// Genotype type for diploid organisms as represented in VCF GT fields.
/// </summary>
public enum GenotypeType
{
    /// <summary>Homozygous reference — allele 0/0.</summary>
    HomozygousRef,

    /// <summary>Heterozygous — allele 0/1.</summary>
    Heterozygous,

    /// <summary>Homozygous alternate — allele 1/1.</summary>
    HomozygousAlt
}