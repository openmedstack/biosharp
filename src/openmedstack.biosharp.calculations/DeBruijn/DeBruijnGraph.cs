namespace OpenMedStack.BioSharp.Calculations.DeBruijn
{
    using System.Collections.Generic;
    using System.Linq;
    using System.Runtime.CompilerServices;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Model;

    internal class KmerNode
    {
        public KmerNode(string id)
        {
            Id = id;
        }

        public string Id { get; }

        public List<string> InboundEdges { get; } = new();
    }

    public class DeBruijnGraph
    {
        private readonly int _k;
        private readonly IAsyncEnumerable<Sequence> _reads;

        public DeBruijnGraph(int k, IAsyncEnumerable<Sequence> reads)
        {
            _k = k;
            _reads = reads;
        }

        private async IAsyncEnumerable<KmerNode> CreateKMers([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await foreach (var line in _reads.WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                var data = line.GetData();
                foreach (var node in Enumerable.Range(0, line.Length - _k).AsParallel()
                    .Select(i => Encoding.ASCII.GetString(data.Span.Slice(i, _k + 1)))
                    .Select(kMer => new KmerNode(kMer)))
                {
                    yield return node;
                }
            }
        }

        private async Task<Dictionary<string, List<KmerNode>>> CreateInterGraph(CancellationToken cancellationToken)
        {
            var kmers = await CreateKMers(cancellationToken).ToListAsync(cancellationToken).ConfigureAwait(false);
            foreach (var kMer in kmers.AsParallel())
            {
                var edge = kMer.Id[1..];
                foreach (var node in kmers.Where(node => node != kMer).Where(node => node.Id.StartsWith(edge)))
                {
                    node.InboundEdges.Add(edge);
                }
            }

            return kmers.SelectMany(x => x.InboundEdges)
                .Distinct()
                .ToDictionary(x => x, x => kmers.Where(k => k.InboundEdges.Contains(x)).ToList());
        }

        public async IAsyncEnumerable<string> CreateGraph([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var interGraph = await CreateInterGraph(cancellationToken).ConfigureAwait(false);
            foreach (var start in interGraph.SelectMany(x => x.Value).Where(x => x.InboundEdges.Count == 0))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var s = start;
                var builder = new StringBuilder(start.Id);
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var hasNext = interGraph.TryGetValue(s.Id[1..], out var nextList);
                    if (hasNext && nextList!.Count > 0)
                    {
                        break;
                    }

                    var next = nextList![0];
                    nextList.Remove(next);
                    builder.Append(next.Id[^1]);
                    s = next;
                }

                yield return builder.ToString();
            }
        }
    }
}
