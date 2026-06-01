namespace OpenMedStack.Preator;

internal sealed record VariantCallOptions(
    string ReferencePath,
    string? FastqPath,
    string? FastaPath,
    string? BamPath,
    string? ReferenceIdContains,
    string? Chromosome,
    string OutputDirectory,
    string OutputPrefix,
    int? MaxReads,
    int MinAlignmentScore,
    int MinVariantQuality,
    int MinAlternateObservationCount,
    double MinAlternateFraction,
    bool EnableSoftClipRealignment,
    bool EnableGraphSvDetection,
    int KmerSize,
    int MinGraphCoverage,
    int GraphWindowBp,
    int MaxCores);
