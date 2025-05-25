namespace OpenMedStack.BioSharp.Calculations.DeBruijn;

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
    private readonly Dictionary<string, KmerNode> _graph = new();
    private bool _built = false;
    private bool _filtered = false;

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
            for (var i = 0; i <= line.Length - _k; i++) yield return new string(data.Span.Slice(i, _k).ToArray());
        }
    }

    private async Task BuildGraph(CancellationToken cancellationToken)
    {
        if (_built) return;

        _built = true;
        if (_filtered) return;

        var kmers = await CreateKMers(cancellationToken).ToListAsync(cancellationToken).ConfigureAwait(false);

        foreach (var kmer in kmers.Distinct())
        {
            var left = kmer[..^1];
            var right = kmer[1..];

            // Left node: create if needed, then add edge with coverage
            if (!_graph.TryGetValue(left, out var leftNode))
            {
                leftNode = new KmerNode(left, 0, new List<string>());
                _graph[left] = leftNode;
            }

            if (!leftNode.OutboundEdges.Contains(right)) leftNode.OutboundEdges.Add(right);

            leftNode.OutboundCoverage[right] = leftNode.OutboundCoverage.GetValueOrDefault(right, 0) + 1;

            // Right node: create if needed
            if (!_graph.TryGetValue(right, out var rightNode))
            {
                rightNode = new KmerNode(right, 0, new List<string>());
                _graph[right] = rightNode;
            }

            rightNode.InboundEdges++;
        }
    }

    /// <summary>
    /// Assembles sequences from the De Bruijn graph using a greedy
    /// walk that follows the highest-coverage path at each fork.
    /// For typical short-read data this produces the consensus reference
    /// sequence (or separate bubbles/tips).
    /// </summary>
    public async IAsyncEnumerable<string> Assemble([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await BuildGraph(cancellationToken).ConfigureAwait(false);

        // Build a mutable coverage map so we can decrement as we traverse
        var coverage = new Dictionary<string, Dictionary<string, int>>();
        foreach (var kv in _graph)
        {
            var edges = new Dictionary<string, int>(kv.Value.OutboundCoverage);
            coverage[kv.Key] = edges;
        }

        // Track which edges we've already traversed (each unique edge once)
        var visited = new HashSet<(string from, string to)>();
        var sequences = new List<string>();

        // Find start nodes: prefer nodes with in-degree=0, out-degree>0.
        // If none exist (no clean start), pick the node with the highest
        // in-degree 0 and out-degree > 0.
        // As a fallback, try the node with max (out - in).
        var candidates = _graph
            .Where(n => n.Value.OutDegree > 0)
            .OrderByDescending(n => n.Value.InboundEdges == 0 ? 1000000 : n.Value.OutDegree - n.Value.InboundEdges)
            .ToList();

        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var seq = WalkFrom(candidate.Key, coverage, visited, _k);
            if (seq != null) sequences.Add(seq);
        }

        foreach (var seq in sequences)
            yield return seq;
    }

    /// <summary>
    /// Greedy walk from a given node: at each step pick the neighbor
    /// with the highest remaining coverage. Stop when no unvisited
    /// outgoing edges remain.
    /// </summary>
    private static string? WalkFrom(
        string start,
        Dictionary<string, Dictionary<string, int>> coverage,
        HashSet<(string, string)> visited,
        int kmerLen)
    {
        var cur = start;
        var sb = new StringBuilder(cur.Length);
        sb.Append(cur);

        while (true)
        {
            if (!coverage.TryGetValue(cur, out var edges) || edges.Count == 0) break;

            // Pick the neighbor with the highest remaining coverage;
            // break ties by lexicographic order for determinism.
            var best = edges
                .OrderByDescending(e => e.Value)
                .ThenBy(e => e.Key)
                .First();

            // Skip edges already visited (circular paths)
            if (!visited.Add((cur, best.Key))) break;

            if (best.Key.Length > 0) sb.Append(best.Key[^1]); // append last char of neighbor (overlap is k-2)

            cur = best.Key;
        }

        // Sanity: return only if path has meaningful length
        return sb.Length >= kmerLen ? sb.ToString() : null;
    }

    public async Task<IList<KmerNode>> GetNodes(CancellationToken cancellationToken)
    {
        await BuildGraph(cancellationToken).ConfigureAwait(false);
        return _graph.Values.ToList();
    }

    public async Task<IReadOnlyDictionary<string, KmerNode>> GetGraph(CancellationToken cancellationToken)
    {
        await BuildGraph(cancellationToken).ConfigureAwait(false);
        return new Dictionary<string, KmerNode>(_graph);
    }

    public int K
    {
        get { return _k; }
    }

    /// <summary>
    /// Filters out edges with coverage below the threshold.
    /// By default uses half the median edge coverage; edges below that
    /// are pruned from both the outbound edge list and coverage map.
    /// This removes spurious branches caused by sequencing errors.
    /// </summary>
    public void FilterLowCoverageEdges(int? minCoverage = null)
    {
        BuildGraph(default).GetAwaiter().GetResult();
        var allCoverages = new List<int>();
        foreach (var node in _graph.Values)
        {
            foreach (var cov in node.OutboundCoverage.Values) allCoverages.Add(cov);
        }

        if (allCoverages.Count == 0) return;

        if (minCoverage == null)
        {
            allCoverages.Sort();
            var median = allCoverages[allCoverages.Count / 2];
            minCoverage = Math.Max(1, median / 2);
        }

        foreach (var node in _graph.Values)
        {
            var edgesToRemove = node.OutboundCoverage
                .Where(kvp => kvp.Value < minCoverage.Value)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var edge in edgesToRemove)
            {
                node.OutboundCoverage.Remove(edge);
                node.OutboundEdges.Remove(edge);
            }
        }

        _filtered = true;
    }
}
