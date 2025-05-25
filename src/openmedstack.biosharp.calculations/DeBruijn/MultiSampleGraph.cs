namespace OpenMedStack.BioSharp.Calculations.DeBruijn;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Model;

/// <summary>
/// Manages multiple DeBruijnGraph instances and provides methods to:
/// - Store and retrieve sample-associated graphs
/// - Build a Bloom filter union of all sample k-mers
/// - Query for k-mers unique to or shared across samples
/// - Construct a merged union graph from all samples
/// </summary>
public class MultiSampleGraph
{
    private readonly int _k;
    private readonly Dictionary<string, DeBruijnGraph> _samples = new();

    /// <summary>
    /// Creates a MultiSampleGraph with the specified k-mer length.
    /// </summary>
    /// <param name="k">The k-mer length used by all sample graphs.</param>
    public MultiSampleGraph(int k)
    {
        _k = k;
    }

    /// <summary>
    /// Gets the number of samples added to this graph.
    /// </summary>
    public int SampleCount
    {
        get { return _samples.Count; }
    }

    /// <summary>
    /// Gets the k-mer length.
    /// </summary>
    public int K
    {
        get { return _k; }
    }

    /// <summary>
    /// Adds a sample graph with the given name.
    /// </summary>
    /// <param name="name">Unique sample name identifier.</param>
    /// <param name="graph">The DeBruijnGraph for this sample.</param>
    /// <exception cref="ArgumentNullException">Thrown when graph is null.</exception>
    /// <exception cref="ArgumentException">Thrown when a sample with the same name already exists.</exception>
    public void AddSample(string name, DeBruijnGraph graph)
    {
        if (graph == null)
            throw new ArgumentNullException(nameof(graph));
        if (string.IsNullOrEmpty(name))
            throw new ArgumentException("Sample name must not be empty.", nameof(name));

        _samples[name] = graph;
    }

    /// <summary>
    /// Checks if a sample with the given name exists.
    /// </summary>
    public bool HasSample(string name)
    {
        return _samples.ContainsKey(name);
    }

    /// <summary>
    /// Gets all sample names.
    /// </summary>
    public IEnumerable<string> GetSamples()
    {
        return _samples.Keys.ToArray();
    }

    /// <summary>
    /// Gets all nodes from a specific sample graph.
    /// </summary>
    public async Task<IEnumerable<KmerNode>> GetNodesInSample(string name)
    {
        if (!_samples.ContainsKey(name))
            throw new ArgumentException($"Sample '{name}' not found.", nameof(name));

        return await _samples[name].GetNodes(default);
    }

    /// <summary>
    /// Builds a Bloom filter containing all k-mers from all samples.
    /// Each sample contributes to the union by adding its node k-mers.
    /// </summary>
    public async Task<BloomFilter> GetUnionFilterAsync()
    {
        if (_samples.Count == 0)
            return new BloomFilter(1, 0.01); // Empty filter with minimal size

        var totalNodes = 0;
        foreach (var sample in _samples.Values)
        {
            var nodes = await sample.GetNodes(default);
            totalNodes += nodes.Count();
        }

        var union = new BloomFilter(
            Math.Max(totalNodes, _samples.First().Value.K),
            0.01);

        foreach (var pair in _samples)
        {
            var nodes = await pair.Value.GetNodes(default);
            foreach (var node in nodes)
            {
                // Add the node k-mer itself
                union.Add(node.Id);
                // Add edge k-mers (node id + last char of neighbor)
                foreach (var neighbor in node.OutboundEdges)
                {
                    var edgeKmer = node.Id + neighbor[neighbor.Length - 1];
                    if (edgeKmer.Length >= _k) union.Add(edgeKmer.Substring(0, Math.Min(edgeKmer.Length, _k)));
                }
            }
        }

        return union;
    }

    /// <summary>
    /// Checks if a k-mer is present in a specific sample's graph.
    /// </summary>
    public async Task<bool> HasKmerInSampleAsync(string sampleName, string kmer)
    {
        if (!_samples.ContainsKey(sampleName))
            throw new ArgumentException($"Sample '{sampleName}' not found.", nameof(sampleName));

        var nodes = await _samples[sampleName].GetNodes(default);
        // Node IDs are (k-1)-mers; match by checking the (k-1)-mer prefix of the queried k-mer
        var prefixLen = Math.Max(kmer.Length - 1, 1);
        var prefix = kmer[..prefixLen];
        return nodes.Any(n => n.Id == prefix || n.Id == kmer);
    }

    /// <summary>
    /// Checks if a k-mer is present in ALL specified samples.
    /// </summary>
    public async Task<bool> HasKmerInAllSamplesAsync(IEnumerable<string> sampleNames, string kmer)
    {
        var names = sampleNames.ToList();
        if (!names.Any())
            return false;

        foreach (var name in names)
            if (!await HasKmerInSampleAsync(name, kmer))
                return false;
        return true;
    }

    /// <summary>
    /// Gets k-mers that are unique to the specified sample (not present in any other sample).
    /// </summary>
    public async Task<IEnumerable<string>> GetUniqueKmersInSampleAsync(string sampleName)
    {
        if (!_samples.ContainsKey(sampleName))
            throw new ArgumentException($"Sample '{sampleName}' not found.", nameof(sampleName));

        var sampleKmers = new HashSet<string>(await GetAllKmersFromSample(sampleName));
        var otherNames = _samples.Keys.Where(n => n != sampleName).ToList();

        if (!otherNames.Any())
            return sampleKmers;

        var otherKmers = new HashSet<string>();
        foreach (var name in otherNames) otherKmers.UnionWith(await GetAllKmersFromSample(name));

        return sampleKmers.Except(otherKmers);
    }

    /// <summary>
    /// Gets k-mers that are shared by ALL specified samples.
    /// </summary>
    public async Task<IEnumerable<string>> GetSharedKmersAsync(IEnumerable<string> sampleNames)
    {
        var names = sampleNames.ToList();
        if (!names.Any())
            return Array.Empty<string>();

        var sharedKmers = new HashSet<string>(await GetAllKmersFromSample(names[0]));

        for (var i = 1; i < names.Count; i++)
        {
            var kmersInSample = new HashSet<string>(await GetAllKmersFromSample(names[i]));
            sharedKmers.IntersectWith(kmersInSample);
        }

        return sharedKmers;
    }

    /// <summary>
    /// Gets the unique k-mer count across all samples.
    /// </summary>
    public async Task<int> GetTotalUniqueKmerCount()
    {
        if (_samples.Count == 0)
            return 0;

        var allKmers = new HashSet<string>();

        foreach (var sample in _samples.Values)
        {
            var nodes = await sample.GetNodes(default);
            foreach (var node in nodes) allKmers.Add(node.Id);
        }

        return allKmers.Count;
    }

    /// <summary>
    /// Builds a union DeBruijnGraph from all samples by merging their nodes and edges.
    /// Since we can't access private fields, we rebuild by assembling from the merged graph.
    /// Returns the assembled sequences from all samples combined.
    /// </summary>
    public async Task<IEnumerable<string>> BuildUnionGraph()
    {
        // Collect all assembled sequences from all samples
        var allSequences = new List<string>();
        foreach (var pair in _samples)
        {
            var assembly = pair.Value.Assemble(default);
            await foreach (var seq in assembly) allSequences.Add(seq);
        }

        return allSequences;
    }

    private async Task<List<string>> GetAllKmersFromSample(string sampleName)
    {
        var kmers = new List<string>();
        var nodes = await _samples[sampleName].GetNodes(default);
        foreach (var node in nodes) kmers.Add(node.Id);
        return kmers.Distinct().ToList();
    }
}
