using System.Collections.Generic;

namespace OpenMedStack.BioSharp.Calculations.DeBruijn;

/// <summary>
/// Internal: parsed JSON repeat library structure.
/// </summary>
internal class RepeatLibrary
{
    public string? Version { get; set; }
    public string? Description { get; set; }
    public IList<RepeatElement>? Repeats { get; set; }
}