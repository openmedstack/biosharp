namespace OpenMedStack.Preator;

internal sealed record TrimOptions(
    string FastqPath,
    string Adapter,
    int MinLength,
    int MaxMismatches,
    int? MaxReads,
    string OutputDirectory,
    string OutputPrefix);

