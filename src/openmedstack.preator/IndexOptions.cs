namespace OpenMedStack.Preator;

/// <summary>Options for the <c>preator index</c> command.</summary>
internal sealed record IndexOptions(
    string? FastaPath,
    string? BamPath,
    string OutputPath,
    string OutputPrefix,
    int FmSampleRate,
    bool AlsoBuildReferenceIndex,
    string? ReferenceIdContains);
