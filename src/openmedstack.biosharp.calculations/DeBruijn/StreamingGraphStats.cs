namespace OpenMedStack.BioSharp.Calculations.DeBruijn;

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
    public bool AllReadsBuffered
    {
        get { return false; }
    }
}