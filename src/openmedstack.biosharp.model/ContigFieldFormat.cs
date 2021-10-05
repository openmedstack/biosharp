namespace OpenMedStack.BioSharp.Model
{
    using System.Collections.Immutable;

    public record ContigFieldFormat(ImmutableDictionary<string, string> Values) : IVariantMetaInformation;
}