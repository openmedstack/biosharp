namespace OpenMedStack.BioSharp.Model
{
    public record InformationFieldFormat(
        string Id,
        char Number,
        string Type,
        string Description,
        string? Source,
        string? Version) : IVariantMetaInformation;
}