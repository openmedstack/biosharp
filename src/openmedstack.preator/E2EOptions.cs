namespace OpenMedStack.Preator;

internal sealed record E2EOptions(
    string? FastqPath,
    string? FastaPath,
    string OutputDirectory,
    string OutputPrefix,
    string ReferencePath,
    string? Chromosome,
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
    int MaxCores,
    string? ReferenceIdContains,
    string TranscriptDatabasePath,
    string? TranscriptId,
    float MinQuality);
