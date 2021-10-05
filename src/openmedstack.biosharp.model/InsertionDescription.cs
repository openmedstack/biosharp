namespace OpenMedStack.BioSharp.Model
{
    /// <summary>
    /// Defines the HGVS insertion description type.
    /// </summary>
    public record InsertionDescription(
        string From,
        string To,
        string Insertion,
        ReferenceSequenceKind ReferenceSequenceKind) : HgvsDescription(ReferenceSequenceKind);
}