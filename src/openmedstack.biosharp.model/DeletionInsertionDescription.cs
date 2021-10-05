namespace OpenMedStack.BioSharp.Model
{
    /// <summary>
    /// Defines the HGVS deletion/insertion description type.
    /// </summary>
    public record DeletionInsertionDescription(
        string From,
        string To,
        string? Deleted,
        string Inserted,
        ReferenceSequenceKind ReferenceSequenceKind) : HgvsDescription(ReferenceSequenceKind);
}