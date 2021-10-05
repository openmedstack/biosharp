namespace OpenMedStack.BioSharp.Model
{
    public record AlternativeAlleleFieldFormat(AlternativeAlleleKind Type, string Description) : IVariantMetaInformation;
}