namespace OpenMedStack.BioSharp.Model
{
    /// <summary>
    /// Defines the HGVS deletion description type.
    /// </summary>
    public record DeletionDescription(
        string From,
        string To,
        ReferenceSequenceKind ReferenceSequenceKind,
        string? Deletion = null) : HgvsDescription(ReferenceSequenceKind);
}