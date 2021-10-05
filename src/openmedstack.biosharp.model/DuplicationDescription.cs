namespace OpenMedStack.BioSharp.Model
{
    /// <summary>
    /// Defines the HGVS duplication description type.
    /// </summary>
    public record DuplicationDescription(
        string From,
        string To,
        ReferenceSequenceKind ReferenceSequenceKind,
        string? Duplication = null) : HgvsDescription(ReferenceSequenceKind);
}