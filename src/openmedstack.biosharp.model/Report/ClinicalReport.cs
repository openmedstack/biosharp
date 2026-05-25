using OpenMedStack.BioSharp.Model.Alignment;

namespace OpenMedStack.BioSharp.Model.Report;

public class ClinicalReport
{
    public required string SampleName { get; set; }
    public required string RunDate { get; set; }
    public required string PipelineVersion { get; set; }
    public required string ReferenceGenome { get; set; }
    public required string LabName { get; set; }
    public required string Operator { get; set; }
    public QcSummary? QcMetrics { get; set; }
    public VariantReport[] Variants { get; set; } = [];
}
