namespace OpenMedStack.Preator;

internal sealed record MarkDupOptions(
    string BamPath,
    string OutputPath,
    string OutputPrefix,
    int OpticalPixelDistance);
