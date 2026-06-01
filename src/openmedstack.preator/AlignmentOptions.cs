namespace OpenMedStack.Preator;

internal sealed record AlignmentOptions(
    string ReferencePath,
    string? FastqPath,
    string? BamPath,
    string OutputPath,
    int? MaxReads,
    int MinAlignmentScore,
    int MinSeedLen,
    int MaxSeedHitsThreshold,
    int SeedStep,
    int WindowPadding,
    int MaxCandidateWindowsPerRead,
    int MaxCores,
    string OutputPrefix);
