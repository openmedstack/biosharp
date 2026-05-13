namespace OpenMedStack.BioSharp.Calculations.DeBruijn;

/// <summary>
/// Holds information about a somatic variant detected from tumor-normal comparison.
/// </summary>
public class SomaticVariant
{
    /// <summary>Simplified VCF-style position (1-based).</summary>
    public int Position { get; set; }

    /// <summary>Reference allele.</summary>
    public string Reference { get; set; } = null!;

    /// <summary>Alternative (tumor-specific) allele.</summary>
    public string Alternate { get; set; } = null!;

    /// <summary>Tumor coverage at this locus.</summary>
    public int TumorCoverage { get; set; }

    /// <summary>Normal coverage at this locus.</summary>
    public int NormalCoverage { get; set; }

    /// <summary>
    /// Local tumor allele fraction (alt / total).
    /// </summary>
    public double TumorAlleleFraction
    {
        get
        {
            var total = TumorCoverage + NormalCoverage;
            return total > 0 ? (double)TumorCoverage / total : 0.0;
        }
    }

    /// <summary>Phred-scaled quality estimate.</summary>
    public int Quality { get; set; }

    /// <summary>
    /// Estimated confidence level based on coverage and allele fraction.
    /// </summary>
    public BubbleConfidence Confidence { get; set; } = BubbleConfidence.Medium;

    public override string ToString()
    {
        return
            $"Somatic[{Position}: {Reference}->{Alternate}] Tumor={TumorCoverage} Normal={NormalCoverage} Q={Quality} {Confidence}";
    }
}