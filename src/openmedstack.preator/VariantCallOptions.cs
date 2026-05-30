namespace OpenMedStack.Preator;

internal sealed record VariantCallOptions(
    string BamPath,
    string ReferencePath,
    string? ReferenceIdContains,
    string? Chromosome,
    string OutputDirectory,
    string OutputPrefix,
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

