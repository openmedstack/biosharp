using System.IO;

namespace OpenMedStack.Preator;

internal sealed class BclOptions
{
    public DirectoryInfo InputFolder { get; set; } = null!;
    public DirectoryInfo? OutputFolder { get; set; }
    public string? ReadStructure { get; set; }
    public string Lanes { get; set; } = "1";
}
