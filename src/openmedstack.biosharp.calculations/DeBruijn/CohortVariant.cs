using System.Collections.Generic;

namespace OpenMedStack.BioSharp.Calculations.DeBruijn;

/// <summary>
/// Represents a variant detected in a cohort sample with its frequency across the cohort.
/// </summary>
public class CohortVariant
{
    /// <summary>Position in reference (1-based).</summary>
    public int Position { get; set; }

    /// <summary>Reference allele.</summary>
    public string Reference { get; set; } = null!;

    /// <summary>Alternative (variant) allele.</summary>
    public string Alternate { get; set; } = null!;

    /// <summary>Number of samples in the cohort where this variant was detected.</summary>
    public int SampleCount { get; set; }

    /// <summary>Total number of samples in the cohort.</summary>
    public int TotalSamples { get; set; }

    /// <summary>
    /// Fraction of samples carrying this variant (0.0 - 1.0).
    /// </summary>
    public double AlleleFrequency
    {
        get
        {
            return TotalSamples > 0
                ? (double)SampleCount / TotalSamples
                : 0.0;
        }
    }

    /// <summary>
    /// Variant type classification based on cohort frequency.
    /// </summary>
    public CohortVariantType Type
    {
        get
        {
            if (SampleCount == 1)
            {
                return CohortVariantType.SingleSample; // Only in one sample
            }

            if (AlleleFrequency >= 0.9)
            {
                return CohortVariantType.Oncogenic; // Present in most samples
            }

            if (AlleleFrequency >= 0.5)
            {
                return CohortVariantType.Present; // Present in majority
            }

            return CohortVariantType.Subclonal; // Present in minority
        }
    }

    /// <summary>Average quality score across samples.</summary>
    public double AverageQuality { get; set; }

    /// <summary>Average allele fraction across samples.</summary>
    public double AverageAlleleFraction { get; set; }

    /// <summary>Names of samples where this variant was detected.</summary>
    public IList<string> DetectedIn { get; set; } = new List<string>();

    public override string ToString()
    {
        return
            $"Cohort[{Position}: {Reference}->{Alternate}] in {SampleCount}/{TotalSamples} samples ({Type}) Q={AverageQuality:F0}";
    }
}