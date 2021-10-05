namespace OpenMedStack.BioSharp.Model
{
    /// <summary>
    /// Defines the HGVS substitution description type.
    /// </summary>
    public record SubstitutionDescription(
        string Position,
        char From,
        char To,
        ReferenceSequenceKind ReferenceSequenceKind) : HgvsDescription(ReferenceSequenceKind);
}