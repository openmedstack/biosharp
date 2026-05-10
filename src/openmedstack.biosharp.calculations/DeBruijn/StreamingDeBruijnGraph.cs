namespace OpenMedStack.BioSharp.Calculations.DeBruijn;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Model;

/// <summary>
/// Statistics reported after ingesting reads into the streaming De Bruijn graph.
/// </summary>
public sealed class StreamingGraphStats
{
    /// <summary>Current number of distinct k-mer nodes in the graph.</summary>
    public int NodeCount { get; init; }

    /// <summary>Total number of k-mers ingested.</summary>
    public long KmersIngested { get; init; }

    /// <summary>Number of nodes pruned due to low coverage during the build.</summary>
    public int PrunedNodes { get; init; }

    /// <summary>
    /// True when the graph buffers all reads (legacy mode).
    /// Always false for this streaming implementation.
    /// </summary>
    public bool AllReadsBuffered => false;
}

/// <summary>
/// A streaming De Bruijn graph implementation that ingests reads one at a time
/// without buffering the full read set in memory.
///
/// When <see cref="_maxNodes"/> is exceeded, nodes with coverage below
/// <see cref="_minCoverageToKeep"/> are pruned before ingestion continues.
/// </summary>
public sealed class StreamingDeBruijnGraph
{
    private readonly int _k;
    private readonly int _maxNodes;
    private readonly int _minCoverageToKeep;
    private readonly Dictionary<string, KmerNode> _graph = new(StringComparer.Ordinal);
    private long _kmersIngested;
    private int _prunedNodes;

    /// <summary>
    /// Creates a new streaming De Bruijn graph.
    /// </summary>
    /// <param name="k">K-mer size.</param>
    /// <param name="maxNodes">
    /// Maximum node count before pruning. 0 means no cap (unbounded).
    /// Default: 0.
    /// </param>
    /// <param name="minCoverageToKeep">
    /// Minimum outbound-edge total coverage to retain a node during pruning.
    /// Default: 1 (prune only zero-coverage nodes).
    /// </param>
    public StreamingDeBruijnGraph(int k, int maxNodes = 0, int minCoverageToKeep = 1)
    {
        if (k <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(k));
        }

        _k = k;
        _maxNodes = maxNodes;
        _minCoverageToKeep = Math.Max(1, minCoverageToKeep);
    }

    /// <summary>
    /// Streams reads into the graph, processing each read exactly once.
    /// </summary>
    public async Task IngestAsync(
        IAsyncEnumerable<Sequence> reads,
        CancellationToken cancellationToken = default)
    {
        await foreach (var seq in reads.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            IngestRead(seq.GetData().Span);

            // Prune when over the node cap
            if (_maxNodes > 0 && _graph.Count > _maxNodes)
            {
                Prune();
            }
        }
    }

    private void IngestRead(ReadOnlySpan<char> data)
    {
        if (data.Length < _k)
        {
            return;
        }

        for (var i = 0; i <= data.Length - _k; i++)
        {
            var kmer = new string(data.Slice(i, _k));
            var left = kmer[..^1];   // prefix of length k-1
            var right = kmer[1..];    // suffix of length k-1

            _kmersIngested++;

            if (!_graph.TryGetValue(left, out var leftNode))
            {
                leftNode = new KmerNode(left, 0, []);
                _graph[left] = leftNode;
            }

            if (!leftNode.OutboundEdges.Contains(right))
            {
                leftNode.OutboundEdges.Add(right);
            }

            leftNode.OutboundCoverage[right] =
                leftNode.OutboundCoverage.GetValueOrDefault(right, 0) + 1;

            if (!_graph.TryGetValue(right, out var rightNode))
            {
                rightNode = new KmerNode(right, 0, []);
                _graph[right] = rightNode;
            }

            rightNode.InboundEdges++;
        }
    }

    /// <summary>Removes low-coverage nodes to keep memory usage bounded.</summary>
    private void Prune()
    {
        var toRemove = new List<string>();
        foreach (var (key, node) in _graph)
        {
            var totalCoverage = node.OutboundCoverage.Values.Sum();
            if (totalCoverage < _minCoverageToKeep)
            {
                toRemove.Add(key);
            }
        }

        foreach (var key in toRemove)
        {
            _graph.Remove(key);
        }

        _prunedNodes += toRemove.Count;
    }

    /// <summary>Returns current graph statistics.</summary>
    public StreamingGraphStats GetStats() => new()
    {
        NodeCount = _graph.Count,
        KmersIngested = _kmersIngested,
        PrunedNodes = _prunedNodes
    };

    /// <summary>
    /// Assembles contigs from the graph using a greedy highest-coverage walk.
    /// </summary>
    public async Task<IReadOnlyList<string>> AssembleContigsAsync(
        CancellationToken cancellationToken = default)
    {
        var contigs = new List<string>();
        var visited = new HashSet<(string, string)>();

        // Find start nodes (in-degree == 0 or no better option)
        var startNodes = _graph.Values
            .Where(n => n.InboundEdges == 0 && n.OutDegree > 0)
            .ToList();

        if (startNodes.Count == 0)
        {
            startNodes = _graph.Values.Where(n => n.OutDegree > 0).Take(1).ToList();
        }

        foreach (var start in startNodes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sb = new StringBuilder(start.Id);
            var current = start.Id;

            while (_graph.TryGetValue(current, out var node) && node.OutDegree > 0)
            {
                // Greedy: pick highest-coverage outgoing edge
                var bestEdge = node.OutboundCoverage
                    .OrderByDescending(kv => kv.Value)
                    .Select(kv => kv.Key)
                    .FirstOrDefault();

                if (bestEdge == null)
                {
                    break;
                }

                if (!visited.Add((current, bestEdge)))
                {
                    break;
                }

                // Append only the last character of the next k-mer
                sb.Append(bestEdge[^1]);
                current = bestEdge;
            }

            if (sb.Length > _k)
            {
                contigs.Add(sb.ToString());
            }
        }

        await Task.CompletedTask.ConfigureAwait(false);
        return contigs;
    }
}
