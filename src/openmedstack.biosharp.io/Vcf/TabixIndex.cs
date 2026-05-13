using System.Collections.Generic;

namespace OpenMedStack.BioSharp.Io.Vcf;

/// <summary>In-memory representation of a tabix index.</summary>
internal sealed class TabixIndex
{
    public TabixIndex(List<string> refOrder, Dictionary<string, PerRefIndex> refData, long noCoord)
    {
        RefOrder = refOrder;
        RefData = refData;
        NoCoord = noCoord;
    }

    public List<string> RefOrder { get; }
    public Dictionary<string, PerRefIndex> RefData { get; }
    public long NoCoord { get; }
}