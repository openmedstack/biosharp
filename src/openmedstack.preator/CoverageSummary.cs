namespace OpenMedStack.Preator;

/// <summary>JSON-serializable summary of a coverage depth run.</summary>
internal sealed class CoverageSummary
{
    public string InputBam { get; init; } = "";
    public string? BedFile { get; init; }
    public int ReferenceLength { get; init; }
    public int TargetBasesCovered { get; init; }
    public double MeanDepth { get; init; }
    public double MedianDepth { get; init; }
    public double MinDepth { get; init; }
    public double MaxDepth { get; init; }
    public double FractionAt10x { get; init; }
    public double FractionAt20x { get; init; }
    public double FractionAt30x { get; init; }
    public double FractionAt100x { get; init; }
    public double FractionAt500x { get; init; }
    public double CoefficientOfVariation { get; init; }
    public string? DepthTsvPath { get; init; }
    public string SummaryJsonPath { get; init; } = "";
}
