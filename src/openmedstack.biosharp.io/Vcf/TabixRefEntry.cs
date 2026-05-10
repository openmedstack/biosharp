namespace OpenMedStack.BioSharp.Io.Vcf;

using System.Collections.Generic;

/// <summary>Per-reference data from a parsed tabix index.</summary>
internal sealed class TabixRefEntry
{
    public Dictionary<int, List<(ulong Begin, ulong End)>> Bins { get; } = [];
    public List<ulong> LinearIndex { get; } = [];
}
