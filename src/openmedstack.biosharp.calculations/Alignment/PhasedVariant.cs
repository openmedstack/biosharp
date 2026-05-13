namespace OpenMedStack.BioSharp.Calculations.Alignment;

/// <summary>
/// The result of read-backed phasing for a single variant.
/// </summary>
public sealed class PhasedVariant
{
    /// <summary>The original (unphased) variant.</summary>
    public LocalVariantResult Variant { get; }

    /// <summary>
    /// True if this variant was assigned to a phase block by at least one bridging read.
    /// </summary>
    public bool IsPhased { get; }

    /// <summary>
    /// The 1-based VCF position of the leftmost variant in the phase block,
    /// used as the PS (phase-set) tag.  Zero if <see cref="IsPhased"/> is false.
    /// </summary>
    public int PhaseSet { get; }

    /// <summary>
    /// Haplotype index within the phase block: 0 for the reference haplotype,
    /// 1 for the alternate haplotype. -1 if unphased.
    /// </summary>
    public int Haplotype { get; }

    /// <summary>
    /// VCF GT string: phased form uses '|' (e.g. "1|0" or "0|1");
    /// unphased form uses '/' (e.g. "0/1").
    /// </summary>
    public string GenotypeString { get; }

    public PhasedVariant(LocalVariantResult variant, bool isPhased, int phaseSet, int haplotype)
    {
        Variant = variant;
        IsPhased = isPhased;
        PhaseSet = phaseSet;
        Haplotype = haplotype;
        GenotypeString = isPhased
            ? haplotype == 1 ? "1|0" : "0|1"
            : "0/1";
    }
}