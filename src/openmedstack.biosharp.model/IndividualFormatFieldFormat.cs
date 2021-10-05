namespace OpenMedStack.BioSharp.Model
{
    public record IndividualFormatFieldFormat
        (string Id, char Number, string Type, string Description) : IVariantMetaInformation;
}