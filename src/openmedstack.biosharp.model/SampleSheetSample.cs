namespace OpenMedStack.BioSharp.Model;

/// <summary>
/// Represents a single sample row in the <c>[Data]</c> section of an Illumina sample sheet.
/// </summary>
public sealed class SampleSheetSample
{
    public int Lane { get; init; }
    public string SampleId { get; init; } = string.Empty;
    public string SampleName { get; init; } = string.Empty;
    public string SamplePlate { get; init; } = string.Empty;
    public string SampleWell { get; init; } = string.Empty;
    public string I7IndexId { get; init; } = string.Empty;
    public string Index1 { get; init; } = string.Empty;
    public string I5IndexId { get; init; } = string.Empty;
    public string Index2 { get; init; } = string.Empty;
    public string Project { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
}