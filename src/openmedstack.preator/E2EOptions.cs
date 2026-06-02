namespace OpenMedStack.Preator;

public sealed record E2EOptions(
    string? FastqPath,
    string? FastaPath,
    string? BamPath,
    string OutputDirectory,
    string OutputPrefix,
    string ReferencePath,
    string? Chromosome,
    int? MaxReads,
    // Variant calling
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
    // Annotation
    string? TranscriptDatabasePath,
    string? TranscriptId,
    float MinQuality,
    // Adapter trimming (equivalent to preator trim)
    string? Adapter,
    int MinLength,
    int MaxMismatches,
    // Alignment seeder (equivalent to preator align)
    int MinSeedLen,
    int MaxSeedHitsThreshold,
    int SeedStep,
    int WindowPadding,
    int MaxCandidateWindowsPerRead,
    // Duplicate marking (equivalent to preator markdup)
    int OpticalPixelDistance);
