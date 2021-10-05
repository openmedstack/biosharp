namespace OpenMedStack.BioSharp.Calculations.DeBruijn
{
    using System.Collections.Generic;
    using System.Linq;

    internal class KmerNode
    {
        public KmerNode(string id, int inboundEdges, IEnumerable<string> outboundNodes)
        {
            Id = id;
            InboundEdges = inboundEdges;
            OutboundEdges = outboundNodes.ToList();
        }

        public string Id { get; }

        public int InboundEdges { get; set; }

        public List<string> OutboundEdges { get; }

        /// <inheritdoc />
        public override string ToString()
        {
            return $"{InboundEdges}->{Id}->{string.Join(",", OutboundEdges)}";
        }
    }
}