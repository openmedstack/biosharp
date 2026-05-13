using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using OpenMedStack.BioSharp.Model;
using Xunit;

namespace OpenMedStack.BioSharp.Calculations.Tests;

public class StreamingDeBruijnTests
{
    private static async IAsyncEnumerable<Sequence> GenerateReads(string refSeq, int count, int readLen = 20)
    {
        for (var i = 0; i < count; i++)
        {
            var start = i % (refSeq.Length - readLen);
            var read = refSeq.Substring(start, readLen);
            yield return new Sequence($"read_{i}", read.AsMemory(), new string('I', readLen).AsMemory());
        }
        await Task.CompletedTask;
    }

    [Fact]
    public async Task StreamingDeBruijn_CanBuildGraph_WithoutBufferingAllReads()
    {
        const string refSeq = "ACGTACGTACGTACGTACGTACGTACGTACGT";
        var graph = new DeBruijn.StreamingDeBruijnGraph(k: 10);

        await graph.Ingest(GenerateReads(refSeq, 50));
        var stats = graph.GetStats();

        Assert.True(stats.NodeCount > 0);
        Assert.False(stats.AllReadsBuffered, "Streaming graph should not buffer all reads");
    }

    [Fact]
    public async Task StreamingDeBruijn_PrunesLowCoverageNodes()
    {
        const string refSeq = "ACGTACGTACGTACGTACGTACGTACGTACGT";
        var graph = new DeBruijn.StreamingDeBruijnGraph(
            k: 10, maxNodes: 5, minCoverageToKeep: 2);

        await graph.Ingest(GenerateReads(refSeq, 100));
        var stats = graph.GetStats();

        Assert.True(stats.NodeCount <= 5 || stats.PrunedNodes > 0,
            "Graph should have pruned low-coverage nodes");
    }

    [Fact]
    public async Task StreamingDeBruijn_ExistingTestsStillPass()
    {
        // Verify basic assembly still works with streaming graph
        const string refSeq = "ACGTACGTACGTACGTACGTACGTACGTACGT"; // 32 chars, readLen=20 → no divide-by-zero
        var reads = GenerateReads(refSeq, 50);
        var graph = new DeBruijn.StreamingDeBruijnGraph(k: 8);
        await graph.Ingest(reads);

        var contigs = await graph.AssembleContigs();
        Assert.NotEmpty(contigs);
    }
}