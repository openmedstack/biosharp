namespace OpenMedStack.BioSharp.Model
{
    using System.Collections.Immutable;

    public interface IVariantMetaInformation
    {
    }

    public record ContigFieldFormat(ImmutableDictionary<string, string> Values) : IVariantMetaInformation;

    public record KeyValueMetaInformation(string Key, string Value) : IVariantMetaInformation;

    public record InformationFieldFormat(
        string Id,
        char Number,
        string Type,
        string Description,
        string? Source,
        string? Version) : IVariantMetaInformation;

    public record FilterFieldFormat(string Id, string Description) : IVariantMetaInformation;

    public record IndividualFormatFieldFormat
        (string Id, char Number, string Type, string Description) : IVariantMetaInformation;

    public record AlternativeAlleleFieldFormat(AlternativeAlleleKind Type, string Description) : IVariantMetaInformation;

    public enum AlternativeAlleleKind : byte
    {
        /// <summary>
        /// Deletion relative to the reference
        /// </summary>
        Del,
        /// <summary>
        /// Deletion of mobile element relative to the reference
        /// </summary>
        DelMe,
        /// <summary>
        /// Insertion of novel sequence relative to the reference
        /// </summary>
        Ins,
        /// <summary>
        /// Insertion of a mobile element relative to the reference
        /// </summary>
        InsMe,
        /// <summary>
        /// Region of elevated copy number relative to the reference
        /// </summary>
        Dup,
        /// <summary>
        /// Tandem duplication
        /// </summary>
        DupTandem,
        /// <summary>
        /// Inversion of reference sequence
        /// </summary>
        Inv,
        /// <summary>
        /// Copy number variable region (may be both deletion and duplication). The CNV category should not be used when a more specific category can be applied.
        /// </summary>
        Cnv
    }
}
