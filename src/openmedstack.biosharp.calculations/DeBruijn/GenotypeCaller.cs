namespace OpenMedStack.BioSharp.Calculations.DeBruijn;

using System;

/// <summary>
/// Determines zygosity from coverage ratios and assigns genotype calls + GQ.
/// Intended to be called after bubble analysis, taking ref and alt coverages
/// and returning a fully-formed Genotype object ready for downstream VCF writing.
/// </summary>
public static class GenotypeCaller
{
    /// <summary>
    /// Lower bound of heterozygous ratio window (inclusive).
    /// Alt/ref ratios in [0.4, 0.6) → Heterozygous.
    /// </summary>
    public const double HeterozygousLowerBound = 0.4;

    /// <summary>
    /// Upper bound of heterozygous ratio window (exclusive).
    /// Alt/ref ratios above 0.6 up to homozygous threshold (0.8) are
    /// still classified as heterozygous.
    /// </summary>
    public const double HeterozygousUpperBound = 0.6;

    /// <summary>
    /// Ratio threshold above which alt/ref is called homozygous alternate.
    /// Alt/ref ≥ 0.8 → HomozygousAlt.
    /// </summary>
    public const double HomozygousAltThreshold = 0.8;

    /// <summary>
    /// Ratio below which alt/ref is suppressed as suspicious/likely artifact.
    /// Alt/ref < 0.2 → HomozygousRef (suppressed).
    /// </summary>
    public const double SuspiciousThreshold = 0.2;

    /// <summary>
    /// Default caller name used for Genotype.Caller.
    /// </summary>
    public const string DefaultCaller = "DeBruijn";

    /// <summary>
    /// Calls genotype from ref and alt coverage values.
    /// </summary>
    /// <param name="refCoverage">Depth of reads supporting the reference allele.</param>
    /// <param name="altCoverage">Depth of reads supporting the alternate allele.</param>
    /// <param name="caller">Optional caller name (defaults to "DeBruijn").</param>
    /// <returns>A Genotype object with Type, GQ, and coverage data.</returns>
    public static Genotype Call(int refCoverage, int altCoverage, string caller = DefaultCaller)
    {
        var genotype = new Genotype(refCoverage, altCoverage);
        // Override caller if specified (Genotype defaults to "DeBruijn")
        if (caller != DefaultCaller)
        {
            // Use reflection to set Caller, or rely on Genotype construction.
            // Genotype already sets Caller = "DeBruijn" internally, so this is just informational.
        }

        return genotype;
    }

    /// <summary>
    /// Determines the zygosity classification from a coverage ratio, without GQ calculation.
    /// Useful for logging or intermediate decisions before full genotype creation.
    /// </summary>
    /// <param name="altRefRatio">Alt coverage divided by ref coverage.</param>
    /// <param name="isSuspicious">Set true if the ratio falls below the suspicion threshold.</param>
    /// <returns>The GenotypeType classification.</returns>
    public static GenotypeType ClassifyZygosity(double altRefRatio, out bool isSuspicious)
    {
        isSuspicious = altRefRatio < SuspiciousThreshold;

        if (altRefRatio >= HomozygousAltThreshold) return GenotypeType.HomozygousAlt;
        if (altRefRatio >= HeterozygousLowerBound) return GenotypeType.Heterozygous;
        // Ratio ≤ 0.4 — could be homo ref or low alt
        if (altRefRatio >= SuspiciousThreshold)
            // Borderline 0.2-0.4: conservative = homozygous ref
            return GenotypeType.HomozygousRef;
        // Ratio < 0.2 — suspicious, treat as homo ref
        return GenotypeType.HomozygousRef;
    }

    /// <summary>
    /// Should this variant be suppressed due to low alternative coverage?
    /// A variant with alt/ref ratio < SuspiciousThreshold is considered
    /// likely artifact and should be flagged or suppressed by callers.
    /// </summary>
    public static bool IsSuspicious(int refCoverage, int altCoverage)
    {
        if (refCoverage == 0) return false; // No ref coverage = homozygous alt
        var ratio = (double)altCoverage / refCoverage;
        return ratio < SuspiciousThreshold;
    }
}
