namespace OpenMedStack.BioSharp.Calculations.DeBruijn;

/// <summary>
/// Classifies cohort variant frequency.
/// </summary>
public enum CohortVariantType
{
    /// <summary>Present in a single sample only.</summary>
    SingleSample,

    /// <summary>Present in some but most samples (subclonal).</summary>
    Subclonal,

    /// <summary>Present in roughly half or more of samples.</summary>
    Present,

    /// <summary>Pan-cohort variant, present in nearly all samples.</summary>
    Oncogenic
}