namespace OpenMedStack.BioSharp.Model;

/// <summary>
/// Annotation data retrieved from a dbSNP VCF for a single variant, containing the rsID.
/// </summary>
public sealed class DbSnpAnnotation
{
    /// <summary>
    /// The dbSNP rsID (e.g. "rs1234567").
    /// </summary>
    public string RsId { get; }

    public DbSnpAnnotation(string rsId)
    {
        RsId = rsId;
    }
}
