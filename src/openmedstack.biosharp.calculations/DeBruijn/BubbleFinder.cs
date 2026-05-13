namespace OpenMedStack.BioSharp.Calculations.DeBruijn;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Detects bubbles in a De Bruijn graph.
/// A bubble = divergent paths between a shared start (branch node, out-degree >= 2)
/// and a shared end (convergence node, reachable from 2+ divergent successors).
/// </summary>
public static class BubbleFinder
{
    /// <summary>
    /// Finds all bubbles in the graph.
    /// </summary>
    public static async Task<Bubble[]> FindBubbles(DeBruijnGraph graph, int k)
    {
        var nodes = await graph.GetGraph(CancellationToken.None).ConfigureAwait(false);
        var branches = nodes.Values.Where(n => n.OutDegree >= 2).ToList();
        var bubbles = new List<Bubble>();
        var seenKeys = new HashSet<string>();

        foreach (var branch in branches)
        {
            var succs = branch.OutboundEdges.ToList();
            if (succs.Count < 2)
            {
                continue;
            }

            // For each successor, find all reachable nodes (excluding the branch itself)
            var reachableBySucc = new Dictionary<string, HashSet<string>>();
            foreach (var succ in succs)
            {
                var reachable = new HashSet<string>();
                var visited = new HashSet<string> { succ };
                var queue = new Queue<string>([succ]);
                while (queue.Count > 0)
                {
                    var node = queue.Dequeue();
                    if (node != branch.Id)
                    {
                        reachable.Add(node);
                    }

                    if (nodes.TryGetValue(node, out var nd) && nd.OutDegree > 0)
                    {
                        foreach (var nx in nd.OutboundEdges.Where(nx => visited.Add(nx)))
                        {
                            queue.Enqueue(nx);
                        }
                    }
                }

                reachableBySucc[succ] = reachable;
            }

            // Find convergence points reachable from 2+ different successors
            var allReachable = reachableBySucc.Values.SelectMany(s => s).Distinct();
            var convergencePoints = allReachable
                .Where(node => reachableBySucc.Count(kv => kv.Value.Contains(node)) >= 2)
                .ToList();

            foreach (var converge in convergencePoints)
            {
                var paths = new List<SequencePath>();
                foreach (var succ in reachableBySucc.Keys)
                {
                    if (reachableBySucc[succ].Contains(converge))
                    {
                        var path = BuildPath(succ, converge, nodes);
                        if (path != null)
                        {
                            // Full path: branch -> succ -> ... -> converge
                            // Nodes are (k-1)-mers and overlap by k-2 characters.
                            // Trim k-2 chars from the END of each assembled path before dedup comparison.
                            // This prevents identical paths with different-length branches from colliding.
                            var strip = Math.Max(k - 2, 0);
                            var strippedPath = path.Length > strip
                                ? path[..^strip]
                                : path;
                            var fullPath = branch.Id + strippedPath;

                            // Deduplicate: same start, end, and full path = same bubble
                            var key = $"{branch.Id}-->{converge}:{fullPath}";
                            if (seenKeys.Add(key))
                            {
                                paths.Add(new SequencePath(fullPath));
                            }
                        }
                    }

                    if (paths.Count >= 2)
                    {
                        break;
                    }
                }

                if (paths.Count >= 2)
                {
                    bubbles.Add(new Bubble(branch.Id, converge, paths.ToArray()));
                }
            }
        }

        return [.. bubbles];
    }

    /// <summary>
    /// BFS from start to end through the graph.
    /// Returns the full sequence string, or null if no path exists.
    /// </summary>
    private static string? BuildPath(string start, string end, IReadOnlyDictionary<string, KmerNode> nodes)
    {
        var visited = new HashSet<string> { start };
        var pred = new Dictionary<string, string> { { start, "" } };
        var queue = new Queue<string>();
        queue.Enqueue(start);

        while (queue.Count > 0)
        {
            var cur = queue.Dequeue();
            if (cur == end)
            {
                return Reconstruct(pred, end);
            }

            if (!nodes.TryGetValue(cur, out var nd) || nd.OutDegree <= 0)
            {
                continue;
            }

            foreach (var nx in nd.OutboundEdges.Where(visited.Add))
            {
                pred[nx] = cur;
                queue.Enqueue(nx);
            }
        }

        return null;
    }

    /// <summary>
    /// Reconstructs the sequence string from a predecessor map.
    /// Traces from the end node (the one with predecessor "") back to the start,
    /// collecting all nodes, then reverses.
    /// Overlapping k-mers share k-1 bases, so each additional node contributes 1 base.
    /// </summary>
    private static string Reconstruct(Dictionary<string, string> pred, string endNode)
    {
        var path = new List<string>();
        var node = endNode;
        while (true)
        {
            path.Add(node);
            if (!pred.TryGetValue(node, out var p) || p == "")
            {
                break;
            }

            node = p;
        }

        path.Reverse();

        var sb = new System.Text.StringBuilder();
        for (var i = 0; i < path.Count; i++)
        {
            if (i == 0)
            {
                sb.Append(path[0]);
            }
            else
            {
                sb.Append(path[i][path[i].Length - 1]);
            }
        }

        return sb.ToString();
    }
}
