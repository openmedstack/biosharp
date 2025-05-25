namespace OpenMedStack.BioSharp.Model;

public record InformationFieldFormat(
    string Id,
    char Number,
    string Type,
    string Description,
    string? Source = null,
    string? Version = null) : IVariantMetaInformation;
