# De Bruijn Graph Assembly

## Introduction

Instead of aligning reads to a reference genome, the De Bruijn graph approach builds an assembly graph directly from 
the reads themselves — a process known as "de novo" assembly. This method is essential when no reference genome exists 
(e.g., for novel or highly divergent organisms) and also serves as the foundation for graph-based 
[variant detection](bubble-tip-detection.md). Unlike the alignment-based [variant calling pipeline](variant-calling-pipeline.md), which maps reads to a known 
reference, the De Bruijn approach constructs the graph from first principles using only the read sequences.

In this approach, each read is split into overlapping k-mers (substrings of length k). Each unique k-mer becomes a node 
in the graph, and edges connect k-mers that overlap by k-1 bases. The resulting graph represents all possible 
assemblies compatible with the read set — a structure that is also the foundation for graph-based 
[bubble and tip detection](bubble-tip-detection.md), where [structural variants](structural-variant-detection.md) are identified as divergent graph paths.

Traversing this graph — starting from nodes with no incoming edges and following the highest-coverage edges — produces 
assembled contigs (longer sequences composed of multiple overlapping k-mers). Coverage information on edges (how many 
reads support each transition) helps disambiguate repeats and choose the most likely paths. Low-coverage edges can be 
pruned with [Bloom filter](bloom-filter.md)-style confidence estimation to eliminate sequencing error branches.

This graph structure is also the foundation for variant detection: bubbles (divergent paths) in the graph represent 
genetic differences, and tips (dead-end branches) can indicate insertions not present in the reference. The De Bruijn 
graph is a powerful data structure that serves both assembly and variant calling purposes.

```csharp
using OpenMedStack.BioSharp.Calculations.DeBruijn;

// Create reads: (name, sequence, quality)
var refs = AsyncEnumerableExtensions.ToAsyncEnumerable(
    new[]
    {
        new Sequence("r1", "ACGTACGTAGCTACGTACGTACGT".AsMemory(), "III".AsMemory()),
        new Sequence("r2", "ACGTACGTAGCTAGGTACGTACGT".AsMemory(), "III".AsMemory()),
        new Sequence("r3", "ACGTACGTAGCT".AsMemory(), "III".AsMemory()),
        new Sequence("r4", "TACGTACGTAGCTACGTACGT".AsMemory(), "III".AsMemory()),
    }
);

// Build a k=10 De Bruijn graph from reads
var graph = new DeBruijnGraph(k: 10, refs);

// Assemble -- walks the graph from start nodes following highest-coverage edges
await foreach (var seq in graph.Assemble())
{
    Console.WriteLine($"Assembled: {seq}");
}

// Or inspect the graph structure directly
var nodes = await graph.GetGraph();
foreach (var node in nodes.Values)
{
    Console.WriteLine($"{node.InDegree}->{node.Id}->{node.OutDegree} [{string.Join(", ", node.OutboundEdges)}]");
}
```

**Graph construction**: Each k-mer in each read creates a node. An edge from node A to node B exists when a k-mer's 
first (k-1) bases match node A's last (k-1) bases and node B. Edge weights equal coverage (how many times that 
transition appears in the data). For multi-sample analysis, see [Multi-Sample Graph Management](multi-sample-graph.md) to manage multiple 
De Bruijn graphs across tumor and normal samples.

**Low-coverage filtering**: `graph.FilterLowCoverageEdges()` prunes edges below median coverage / 2 by default, 
removing spurious branches caused by sequencing errors.
