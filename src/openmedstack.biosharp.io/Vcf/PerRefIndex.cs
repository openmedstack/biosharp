using System.Collections.Generic;

namespace OpenMedStack.BioSharp.Io.Vcf;

/// <summary>Per-reference sequence index data.</summary>
internal sealed class PerRefIndex
{
    public Dictionary<int, List<(ulong Begin, ulong End)>> Bins { get; } = [];
    public List<ulong> LinearIndex { get; } = [];
}