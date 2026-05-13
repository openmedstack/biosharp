namespace OpenMedStack.BioSharp.Model;

public record UncertainPositionDescription(
    string Position,
    ReferenceSequenceKind ReferenceSequenceKind)
    : HgvsDescription(ReferenceSequenceKind);
