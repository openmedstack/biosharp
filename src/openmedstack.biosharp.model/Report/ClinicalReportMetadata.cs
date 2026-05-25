using System;

namespace OpenMedStack.BioSharp.Model.Report;

/// <summary>
/// Metadata describing the run/sample context for a clinical report.
/// </summary>
public sealed class ClinicalReportMetadata
{
    public string SampleName { get; init; } = string.Empty;
    public DateTimeOffset RunDate { get; init; } = DateTimeOffset.UtcNow;
    public string PipelineVersion { get; init; } = string.Empty;
    public string ReferenceGenome { get; init; } = string.Empty;
    public string LabName { get; init; } = string.Empty;
    public string Operator { get; init; } = string.Empty;
}
