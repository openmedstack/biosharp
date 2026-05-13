namespace OpenMedStack.Preator;

internal sealed record AnnotateOptions(
    string VcfPath,
    string TranscriptDatabasePath,
    string? TranscriptId,
    float MinQuality,
    string OutputDirectory,
    string OutputPrefix);
