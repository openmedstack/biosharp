namespace OpenMedStack.Preator;

/// <summary>Options for the <c>coverage</c> command.</summary>
internal sealed record CoverageOptions(
    string BamPath,
    string OutputPath,
    string OutputPrefix,
    string? BedPath,
    bool WritePerPositionTsv);
