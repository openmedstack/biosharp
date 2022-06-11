namespace OpenMedStack.BioSharp.Calculations.DeBruijn
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Runtime.CompilerServices;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Model;

    public class DeBruijnGraph
    {
        private readonly int _k;
        private readonly IAsyncEnumerable<Sequence> _reads;

        public DeBruijnGraph(int k, IAsyncEnumerable<Sequence> reads)
        {
            _k = k;
            _reads = reads;
        }

        private async IAsyncEnumerable<string> CreateKMers(
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await foreach (var line in _reads.WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                var data = line.GetData();
                foreach (var kmer in Enumerable.Range(0, line.Length + 1 - _k)
                    .AsParallel()
                    .Select(i => string.Join("", data.Span.Slice(i, _k).ToArray())))
                {
                    yield return kmer;
                }
            }
        }

        private async Task<IList<KmerNode>> CreateGraph(CancellationToken cancellationToken)
        {
            var kmers = await CreateKMers(cancellationToken).ToListAsync(cancellationToken).ConfigureAwait(false);
            Dictionary<string, KmerNode> graph = new();

            foreach (var kmer in kmers)
            {
                var left = kmer[..^1];
                var right = kmer[1..];
                if (graph.TryGetValue(left, out var leftNode))
                {
                    leftNode.OutboundEdges.Add(right);
                }
                else
                {
                    graph.Add(left, new KmerNode(left, 0, Enumerable.Empty<string>()));
                }

                if (graph.TryGetValue(right, out var rightNode))
                {
                    rightNode.InboundEdges++;
                }
                else
                {
                    graph.Add(right, new KmerNode(right, 1, Enumerable.Empty<string>()));
                }
            }

            return graph.Values.ToList();
        }

        public async IAsyncEnumerable<string> Assemble([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var interGraph = await CreateGraph(cancellationToken).ConfigureAwait(false);
            foreach (var start in interGraph.Where(x => x.InboundEdges == 0))
            {
                var nodesConsumed = 0;
                cancellationToken.ThrowIfCancellationRequested();
                var s = start;
                var builder = new StringBuilder(start.Id);
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    nodesConsumed++;
                    if (s.OutboundEdges.Count > 0)
                    {
                        var next = s.OutboundEdges.First();
                        s.OutboundEdges.Remove(next);
                        builder.Append(next.AsSpan()[^1]);
                        s = interGraph.First(x => x.Id == next);
                    }
                    else
                    {
                        break;
                    }
                }

                if (nodesConsumed == interGraph.Count)
                {
                    yield return builder.ToString();
                }
            }
        }
    }
}
