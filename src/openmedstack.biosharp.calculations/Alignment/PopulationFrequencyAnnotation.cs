using OpenMedStack.BioSharp.Model.Alignment;

namespace OpenMedStack.BioSharp.Calculations.Alignment;

/// <summary>
/// The result of annotating a variant with population allele frequency data.
/// </summary>
public sealed class PopulationFrequencyAnnotation
{
    /// <summary>The original variant that was annotated.</summary>
    public LocalVariantResult Variant { get; }

    /// <summary>gnomAD / ExAC allele frequency (AF). Zero if absent from the database.</summary>
    public double GnomadAf { get; }

    /// <summary>Maximum AF across populations (AF_popmax). Zero if absent.</summary>
    public double GnomadAfPopmax { get; }

    /// <summary>Total allele number (AN). Zero if absent.</summary>
    public int GnomadAn { get; }

    /// <summary>Alternate allele count (AC). Zero if absent.</summary>
    public int GnomadAc { get; }

    public PopulationFrequencyAnnotation(
        LocalVariantResult variant,
        double gnomadAf,
        double gnomadAfPopmax,
        int gnomadAn,
        int gnomadAc)
    {
        Variant = variant;
        GnomadAf = gnomadAf;
        GnomadAfPopmax = gnomadAfPopmax;
        GnomadAn = gnomadAn;
        GnomadAc = gnomadAc;
    }
}