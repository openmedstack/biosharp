namespace OpenMedStack.Preator;

internal class Options
{
    public string InputFolder { get; set; } = null!;
    public string? OutputFolder { get; set; }
    public string? ReadStructure { get; set; }
    public string Lanes { get; set; } = "1";
}
