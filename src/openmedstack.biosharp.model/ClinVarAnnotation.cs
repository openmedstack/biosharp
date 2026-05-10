namespace OpenMedStack.BioSharp.Model;

/// <summary>
/// Annotation data retrieved from a ClinVar VCF for a single variant.
/// </summary>
public sealed class ClinVarAnnotation
{
    /// <summary>
    /// ClinVar clinical significance classification, e.g. "Pathogenic", "Benign", "Uncertain_significance".
    /// </summary>
    public string ClinicalSignificance { get; }

    /// <summary>
    /// ClinVar disease name (CLNDN field).
    /// </summary>
    public string DiseaseName { get; }

    /// <summary>
    /// ClinVar review status (CLNREVSTAT field).
    /// </summary>
    public string ReviewStatus { get; }

    public ClinVarAnnotation(string clinicalSignificance, string diseaseName, string reviewStatus)
    {
        ClinicalSignificance = clinicalSignificance;
        DiseaseName = diseaseName;
        ReviewStatus = reviewStatus;
    }
}
