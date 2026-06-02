namespace OpenMedStack.Preator;

internal sealed record RepeatMaskOptions(
    string FastaPath,
    string? LibraryPath,
    int? MinMotifLength,
    string OutputPath,
    string OutputPrefix);
