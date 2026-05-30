namespace OpenMedStack.Preator;

internal sealed record QcOptions(
    string FastqPath,
    string? Adapter,
    int? MaxReads,
    string OutputDirectory,
    string OutputPrefix);

