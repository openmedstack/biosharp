namespace OpenMedStack.Preator;

using CommandLine;

internal class Options
{
    [Option('i', "input", Required = true, HelpText = "Set the data input folder (can be relative).")]
    public string InputFolder { get; set; } = null!;

    [Option(
        'o',
        "output",
        Required = false,
        Default = null,
        HelpText = "Set the data output folder (can be relative).")]
    public string? OutputFolder { get; set; }

    [Option('r', "readstructure", Required = false, Default = null, HelpText = "Set the read structure for the data.")]
    public string? ReadStructure { get; set; }

    [Option(
        'l',
        "lanes",
        Required = false,
        Default = "1",
        HelpText = "Comma separated list of lanes to reads. Use * to read all.")]
    public string Lanes { get; set; } = null!;
}
