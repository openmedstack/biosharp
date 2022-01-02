namespace OpenMedStack.Preator;

using CommandLine;

internal class Options
{
    [Option('t', "threads", Required = false, Default = 4, HelpText = "Set amount of threads to use for processing.")]
    public int Threads { get; set; }

    [Option('i', "input", Required = true, HelpText = "Set the data input folder (can be relative).")]
    public string InputFolder { get; set; } = null!;

    [Option('o', "output", Required = false, Default = null, HelpText = "Set the data output folder (can be relative).")]
    public string? OutputFolder { get; set; }

    [Option('r', "readstructure", Required = false, Default = null, HelpText = "Set the read structure for the data.")]
    public string? ReadStructure { get; set; }
}