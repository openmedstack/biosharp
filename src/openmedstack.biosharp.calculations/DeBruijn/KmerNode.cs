namespace OpenMedStack.BioSharp.Calculations.DeBruijn;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

public class KmerNode
{
    public KmerNode(string id, int inboundEdges, IEnumerable<string> outboundNodes)
    {
        Id = id ?? throw new ArgumentNullException(nameof(id));
        InboundEdges = inboundEdges;
        OutboundEdges = outboundNodes?.ToList() ?? new List<string>();
        OutboundCoverage = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    }

    public string Id { get; }

    public int InboundEdges { get; set; }

    public List<string> OutboundEdges { get; }

    public Dictionary<string, int> OutboundCoverage { get; }

    public int OutDegree
    {
        get { return OutboundEdges.Count; }
    }

    public int InDegree
    {
        get { return InboundEdges; }
    }

    public override string ToString()
    {
        var sb = new StringBuilder();
        sb.Append($"{InboundEdges}->{Id}->");
        if (OutboundEdges.Count > 0)
        {
            var edges = OutboundEdges.Select(e =>
            {
                OutboundCoverage.TryGetValue(e, out var c);
                return $"{e}(c={c})";
            });
            sb.Append($"[{string.Join(", ", edges)}]");
        }

        return sb.ToString();
    }
}
