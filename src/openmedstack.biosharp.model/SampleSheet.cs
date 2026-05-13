namespace OpenMedStack.BioSharp.Model;

using System.Collections.Generic;

/// <summary>
/// Parsed Illumina sample sheet with Header, Reads, Settings, and Data sections.
/// </summary>
public sealed class SampleSheet
{
    /// <summary>Key-value pairs from the <c>[Header]</c> section.</summary>
    public Dictionary<string, string> Header { get; init; } = new();

    /// <summary>Read lengths from the <c>[Reads]</c> section.</summary>
    public List<int> ReadLengths { get; init; } = [];

    /// <summary>Key-value pairs from the <c>[Settings]</c> section.</summary>
    public Dictionary<string, string> Settings { get; init; } = new();

    /// <summary>Sample rows from the <c>[Data]</c> section.</summary>
    public List<SampleSheetSample> Samples { get; init; } = [];
}