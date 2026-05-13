namespace OpenMedStack.BioSharp.Calculations.Report;

public class VariantReport
{
    public required string Chromosome { get; set; }
    public int Position { get; set; }
    public required string Reference { get; set; }
    public required string Alternate { get; set; }
    public string? HgvsNotation { get; set; }
    public required string Consequence { get; set; }
    public required string ClinicalSignificance { get; set; }
    public required string AffectedGene { get; set; }
    public string? HgvsProtein { get; set; }
}