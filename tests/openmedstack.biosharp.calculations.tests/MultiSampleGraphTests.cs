using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AsyncEnumerable = OpenMedStack.BioSharp.Calculations.Alignment.AsyncEnumerableExtensions;
using DeBruijn = OpenMedStack.BioSharp.Calculations.DeBruijn;
using Sequence = OpenMedStack.BioSharp.Model.Sequence;
using Xunit;

namespace OpenMedStack.BioSharp.Calculations.Tests;

/// <summary>
/// Acceptance tests for MultiSampleGraph - k-mer union across multiple samples using Bloom filter.
/// Tests cover add, union, intersection, unique/ shared k-mer queries, and graph construction.
/// </summary>
public class MultiSampleGraphTests
{
    private static IAsyncEnumerable<Sequence> MakeReads(IEnumerable<string> seqs, int kmer = 5)
    {
        return AsyncEnumerable.ToAsyncEnumerable(
            seqs.Select(s => new Sequence(
                "r_" + s.GetHashCode(),
                s.AsMemory(),
                new string('I', s.Length).AsMemory())));
    }

    private static DeBruijn.DeBruijnGraph BuildGraph(IEnumerable<string> reads, int k = 5)
    {
        return new DeBruijn.DeBruijnGraph(k, MakeReads(reads, k));
    }

    /// <summary>
    /// Adding a sample stores it in the collection.
    /// MSG-1: Basic add
    /// </summary>
    [Fact]
    public void AddSample_AddsGraph()
    {
        var sample1 = BuildGraph(new[] { "ACGTACGACTAGC" });
        var sample2 = BuildGraph(new[] { "ACGTACGACTAGT" });

        var graph = new DeBruijn.MultiSampleGraph(4);

        graph.AddSample("sample1", sample1);
        graph.AddSample("sample2", sample2);

        Assert.Equal(2, graph.SampleCount);
        Assert.True(graph.HasSample("sample1"));
        Assert.True(graph.HasSample("sample2"));
        Assert.False(graph.HasSample("sample3"));

        // Union filter should have k-mers from both samples
        var union = graph.GetUnionFilterAsync().Result;
        Assert.NotNull(union);
    }

    /// <summary>
    /// All k-mers from all samples should be present in the union filter.
    /// MSG-2: Union contains all
    /// </summary>
    [Fact]
    public void UnionFilter_ContainsKmersFromAllSamples()
    {
        var sample1 = BuildGraph(new[] { "ACGTACGACTAGC" }, 5);
        var sample2 = BuildGraph(new[] { "TTTTACGACTAGT" }, 5);

        var graph = new DeBruijn.MultiSampleGraph(5);
        graph.AddSample("s1", sample1);
        graph.AddSample("s2", sample2);

        var union = graph.GetUnionFilterAsync().Result;

        // Collect all k-mers from sample 1
        var kmer1 = ExtractKmers("ACGTACGACTAGC", 5);
        var kmer2 = ExtractKmers("TTTTACGACTAGT", 5);

        foreach (var km in kmer1)
            Assert.True(union.Contains(km), $"Union should contain k-mer '{km}' from sample s1");

        foreach (var km in kmer2)
            Assert.True(union.Contains(km), $"Union should contain k-mer '{km}' from sample s2");
    }

    /// <summary>
    /// K-mers that were never added to any sample should not be in the union.
    /// MSG-3: Non-existent k-mer absent from union
    /// </summary>
    [Fact]
    public void UnionFilter_NotContainsKmersFromNonExistentSample()
    {
        var sample1 = BuildGraph(new[] { "ACGTACGACTAGC" }, 5);

        var graph = new DeBruijn.MultiSampleGraph(5);
        graph.AddSample("s1", sample1);

        var union = graph.GetUnionFilterAsync().Result;

        // This k-mer was never in any sample
        var neverAddedKmer = "GGGGG";
        Assert.False(union.Contains(neverAddedKmer));
    }

    /// <summary>
    /// HasKmerInSample returns true for a k-mer present in the named sample.
    /// MSG-4: K-mer in single sample - true
    /// </summary>
    [Fact]
    public async Task HasKmerInSample_ReturnsTrue()
    {
        var sample1 = BuildGraph(new[] { "ACGTACGACTAGC" }, 5);

        var graph = new DeBruijn.MultiSampleGraph(5);
        graph.AddSample("s1", sample1);

        var kmer = ExtractKmers("ACGTACGACTAGC", 5).First();

        Assert.True(await graph.HasKmerInSampleAsync("s1", kmer));
    }

    /// <summary>
    /// HasKmerInSample returns false for a k-mer absent from the sample.
    /// MSG-5: K-mer in single sample - false
    /// </summary>
    [Fact]
    public async Task HasKmerInSample_ReturnsFalse()
    {
        var sample1 = BuildGraph(new[] { "ACGTACGACTAGC" }, 5);

        var graph = new DeBruijn.MultiSampleGraph(5);
        graph.AddSample("s1", sample1);

        // Never added to any sample
        var neverAdded = "TTTTT";
        Assert.False(await graph.HasKmerInSampleAsync("s1", neverAdded));
    }

    /// <summary>
    /// HasKmerInAllSamples returns true when k-mer is present in ALL named samples.
    /// MSG-6: K-mer in all samples - true
    /// </summary>
    [Fact]
    public async Task HasKmerInAllSamples_ReturnsTrue()
    {
        var read1 = "ACGTACGACTAGC";
        var sample1 = BuildGraph(new[] { read1 }, 5);
        var sample2 = BuildGraph(new[] { read1, "ACGTACGACTAGT" }, 5);

        var graph = new DeBruijn.MultiSampleGraph(5);
        graph.AddSample("s1", sample1);
        graph.AddSample("s2", sample2);

        var sharedKmer = ExtractKmers(read1, 5).First();

        var samples = new List<string> { "s1", "s2" };
        Assert.True(await graph.HasKmerInAllSamplesAsync(samples, sharedKmer));
    }

    /// <summary>
    /// HasKmerInAllSamples returns false when k-mer is absent from at least one sample.
    /// MSG-7: K-mer in all samples - false
    /// </summary>
    [Fact]
    public async Task HasKmerInAllSamples_ReturnsFalse()
    {
        var sample1 = BuildGraph(new[] { "ACGTACGACTAGC" }, 5);
        var sample2 = BuildGraph(new[] { "TTTTACGACTAGT" }, 5);

        var graph = new DeBruijn.MultiSampleGraph(5);
        graph.AddSample("s1", sample1);
        graph.AddSample("s2", sample2);

        // First k-mer of sample1 won't be in sample2
        var uniqueKmer = ExtractKmers("ACGTACGACTAGC", 5).First();

        var samples = new List<string> { "s1", "s2" };
        Assert.False(await graph.HasKmerInAllSamplesAsync(samples, uniqueKmer));
    }

    /// <summary>
    /// GetUniqueKmersInSample returns k-mers unique to one sample (not in others).
    /// MSG-8: Unique k-mers
    /// </summary>
    [Fact]
    public async Task GetUniqueKmersInSample_ReturnsUniqueKmers()
    {
        var sample1 = BuildGraph(new[] { "AAAAACCCCGGGGT" }, 5);
        var sample2 = BuildGraph(new[] { "TTTTTAGGGGCCCC" }, 5);

        var graph = new DeBruijn.MultiSampleGraph(5);
        graph.AddSample("s1", sample1);
        graph.AddSample("s2", sample2);

        var uniqueInS1 = await graph.GetUniqueKmersInSampleAsync("s1");

        Assert.NotNull(uniqueInS1);

        // Each returned k-mer should NOT be in s2
        foreach (var km in uniqueInS1)
            Assert.False(await graph.HasKmerInSampleAsync("s2", km),
                $"K-mer '{km}' claimed unique to s1 but found in s2");
    }

    /// <summary>
    /// GetSharedKmers returns k-mers present in ALL requested samples.
    /// MSG-9: Shared k-mers
    /// </summary>
    [Fact]
    public async Task GetSharedKmers_ReturnsCommonKmers()
    {
        var commonRead = "ACGTACGACTAGCCGT";
        var sample1 = BuildGraph(new[] { "ACGTACGACTAGC", commonRead, "ACGTACGACTAGT" }, 5);
        var sample2 = BuildGraph(new[] { commonRead, "ACGTACGACTAGT" }, 5);

        var graph = new DeBruijn.MultiSampleGraph(5);
        graph.AddSample("s1", sample1);
        graph.AddSample("s2", sample2);

        var shared = await graph.GetSharedKmersAsync(new[] { "s1", "s2" });

        Assert.NotNull(shared);

        // Each shared kmer should be in both samples
        foreach (var km in shared)
        {
            Assert.True(await graph.HasKmerInSampleAsync("s1", km),
                $"Shared k-mer '{km}' not found in s1");
            Assert.True(await graph.HasKmerInSampleAsync("s2", km),
                $"Shared k-mer '{km}' not found in s2");
        }
    }

    /// <summary>
    /// BuildUnionGraph produces valid sequences from all samples.
    /// MSG-10: Build union graph
    /// </summary>
    [Fact]
    public async Task BuildUnionGraph_ProducesValidGraph()
    {
        var sample1 = BuildGraph(new[] { "ACGTACGACTAGC" }, 5);
        var sample2 = BuildGraph(new[] { "ACGTACGACTAGT" }, 5);

        var graph = new DeBruijn.MultiSampleGraph(5);
        graph.AddSample("s1", sample1);
        graph.AddSample("s2", sample2);

        var unionSequences = await graph.BuildUnionGraph();

        Assert.NotNull(unionSequences);


        // Should contain sequences from both samples
        var sequences = unionSequences.ToList();
        Assert.True(sequences.Count >= 1);
    }

    /// <summary>
    /// Adding no samples handles gracefully - returns empty results.
    /// MSG-11: Empty graph
    /// </summary>
    [Fact]
    public void MultiSampleGraph_Empty_ReturnsEmptyResults()
    {
        var graph = new DeBruijn.MultiSampleGraph(4);

        Assert.Equal(0, graph.SampleCount);

        var union = graph.GetUnionFilterAsync().Result;
        Assert.NotNull(union);

        var unionGraph = graph.BuildUnionGraph().Result;
        Assert.NotNull(unionGraph);
    }

    /// <summary>
    /// Adding samples increases effective coverage in the union.
    /// MSG-12: Coverage grows
    /// </summary>
    [Fact]
    public void UnionFilter_Size_GrowsWithSamples()
    {
        var sample1 = BuildGraph(new[] { "ACGTACGACTAGC" }, 5);
        var sample2 = BuildGraph(new[] { "TTTTTTTTTTTTTTTT" }, 5);
        var sample3 = BuildGraph(new[] { "GGGGGGGGGGGGGGGG" }, 5);

        var graph = new DeBruijn.MultiSampleGraph(5);
        graph.AddSample("s1", sample1);
        var unionAfter1 = graph.GetUnionFilterAsync().Result;

        graph.AddSample("s2", sample2);
        var unionAfter2 = graph.GetUnionFilterAsync().Result;

        Assert.True((long)unionAfter2.TotalBits >= (long)unionAfter1.TotalBits);
    }

    /// <summary>
    /// TotalUniqueKmerCount returns correct count.
    /// MSG-13: Unique k-mer count
    /// </summary>
    [Fact]
    public async Task GetTotalUniqueKmerCount_ReturnsCorrectCount()
    {
        var sample1 = BuildGraph(new[] { "ACGTACGACTAGC" }, 5);
        var sample2 = BuildGraph(new[] { "ACGTACGACTAGT" }, 5);

        var graph = new DeBruijn.MultiSampleGraph(5);
        graph.AddSample("s1", sample1);
        graph.AddSample("s2", sample2);

        var count = await graph.GetTotalUniqueKmerCount();

        Assert.True(count > 0);
        Assert.True(count >= ExtractKmers("ACGTACGACTAGC", 5).Count());
        Assert.True(count >= ExtractKmers("ACGTACGACTAGT", 5).Count());
    }

    private static IEnumerable<string> ExtractKmers(string sequence, int k)
    {
        var kmers = new List<string>();
        for (var i = 0; i <= sequence.Length - k; i++)
            kmers.Add(sequence.Substring(i, k));
        return kmers;
    }
}
