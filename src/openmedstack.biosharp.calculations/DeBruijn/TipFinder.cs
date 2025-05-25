namespace OpenMedStack.BioSharp.Calculations.DeBruijn;

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Detects tips (dead-end branches) in a De Bruijn graph.
/// Short tips (<=2k) are likely sequencing errors. Long tips (&gt;2k) may represent insertions
/// not present in the reference.
/// </summary>
public static class TipFinder
{
    /// <summary>
    /// Finds all tips in the graph.
    /// </summary>
    /// <param name="graph">The De Bruijn graph.</param>
    /// <param name="longTipThreshold">Minimum length (in bases) for a tip to be
    /// classified as a "long tip" (potential insertion).
    /// </param>
    /// <returns>An array of tips, each with its sequence, length, and length classification.</returns>
    public static async Task<Tip[]> FindTips(DeBruijnGraph graph, int longTipThreshold)
    {
        var nodes = await graph.GetGraph(CancellationToken.None).ConfigureAwait(false);

        var tips = new List<Tip>();

        // Find nodes with in-degree > 0 and no outbound edges (dead ends).
        // Then trace back to see if they are connected to a branching point.
        var deadEnds = nodes.Values
            .Where(n => n is { InDegree: > 0, OutDegree: 0 })
            .ToList();

        foreach (var deadEnd in deadEnds)
        {
            // Trace back. ONLY return a tip if we hit a branch (branching start)
            // or merge (alternate path converging back). If we hit the chain start
            // (in-degree=0, no predecessors), it's just the end of a linear chain.
            var tipSequence = TraceTip(deadEnd, nodes);
            if (!string.IsNullOrEmpty(tipSequence) && tipSequence.Length < 1000)
            {
                var isLong = tipSequence.Length >= longTipThreshold;
                tips.Add(new Tip(tipSequence, tipSequence.Length, isLong));
            }
        }

        return tips.ToArray();
    }

    /// <summary>
    /// Traces a tip backwards from a dead-end node.
    /// Returns the sequence ONLY if we reach a branch node (outDegree>1) or converge
    /// at a node with inDegree>1. If we reach a chain start (inDegree=0) first,
    /// returns null — this is just a chain terminal, not an alt path tip.
    /// </summary>
    private static string? TraceTip(KmerNode deadEnd, IReadOnlyDictionary<string, KmerNode> nodes)
    {
        var tipNodes = new List<string>();
        var current = deadEnd;
        var visited = new HashSet<string>();
        var hitBranchOrMerge = false;

        while (current != null)
        {
            if (!visited.Add(current.Id)) break; // cycle detected

            // If we hit a branch (out-degree > 1), this dead-end is a genuine tip
            if (current.OutDegree > 1)
            {
                hitBranchOrMerge = true;
                break;
            }

            // If we hit a merge (multiple unique predecessors AND we have some tip nodes),
            // the alternate path converged back — this node is the merge point.
            // Use unique predecessor count, not edge multiplicity, to avoid
            // false positives on high-coverage linear nodes (e.g. AGC with in=4
            // but only one predecessor TAG).
            if (HasMultiplePredecessors(current, nodes) && tipNodes.Count > 0)
            {
                hitBranchOrMerge = true;
                // Don't include the merge node itself in the tip.
                break;
            }

            // If we hit a chain start (in-degree = 0, no predecessors),
            // this dead-end is just the end of a linear chain. Not a tip.
            if (current.InDegree == 0)
                // Chain start — return null (not a tip)
                return null;

            tipNodes.Add(current.Id);
            var pred = GetPredecessor(current, nodes);
            current = pred;
        }

        if (tipNodes.Count == 0 || !hitBranchOrMerge) return null; // Not a genuine tip

        // Build sequence (reverse: tipNodes = [leaf, ..., next-to-branch])
        var sb = new System.Text.StringBuilder();
        for (var i = tipNodes.Count - 1; i >= 0; i--)
        {
            var kmer = tipNodes[i];
            if (sb.Length == 0)
                sb.Append(kmer);
            else
                sb.Append(kmer[^1]); // overlap = k-1
        }

        return sb.ToString();
    }

    /// <summary>
    /// Gets the unique predecessor of a node (in-degree = 1 → exactly one pred).
    /// </summary>
    private static KmerNode? GetPredecessor(KmerNode node, IReadOnlyDictionary<string, KmerNode> nodes)
    {
        foreach (var pair in nodes)
            if (pair.Value.OutboundEdges.Contains(node.Id))
                return pair.Value;

        return null;
    }

    /// <summary>
    /// True if the node has more than one unique predecessor node.
    /// Uses the actual predecessor graph, not the edge multiplicity
    /// stored in InDegree, to avoid false positives on high-coverage
    /// linear paths.
    /// </summary>
    private static bool HasMultiplePredecessors(KmerNode node, IReadOnlyDictionary<string, KmerNode> nodes)
    {
        var count = 0;
        foreach (var pair in nodes)
            if (pair.Value.OutboundEdges.Contains(node.Id))
            {
                count++;
                if (count > 1) return true;
            }

        return count > 1;
    }
}
