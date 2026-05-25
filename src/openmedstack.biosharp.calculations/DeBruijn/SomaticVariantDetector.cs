using OpenMedStack.BioSharp.Model;

namespace OpenMedStack.BioSharp.Calculations.DeBruijn;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

/// <summary>
/// Detects somatic (tumor-specific) variants by comparing a tumor De Bruijn graph
/// against a normal-sample Bloom filter. A bubble whose alt path is absent from
/// the normal Bloom filter is classified as somatic.
/// </summary>
public static class SomaticVariantDetector
{
    /// <summary>
    /// Minimum alt-allele fraction in the tumor to call a variant somatic (default 0.30 = 30%).
    /// </summary>
    public const double DefaultMinAlleleFraction = 0.30;

    /// <summary>
    /// Minimum tumor coverage to consider a locus (default 5).
    /// </summary>
    public const int DefaultMinTumorCoverage = 5;

    /// <summary>
    /// Minimum normal coverage to confirm absence (if 0, means normal didn't read
    /// that region at all — still somatic).
    /// </summary>
    public const int DefaultMinNormalCoverage = 0;

    /// <summary>
    /// Detects somatic variants from a tumor-normal pair.
    /// </summary>
    /// <param name="tumorGraph">DeBruijn graph built from tumor reads.</param>
    /// <param name="normalFilter">Bloom filter built from normal-sample k-mers. K-mers
    /// present here are considered germline/common and filtered out.</param>
    /// <param name="reference">Reference sequence for position alignment.</param>
    /// <param name="refStart">Reference start position.</param>
    /// <param name="minAlleleFraction">Minimum alt fraction in tumor (default 0.30).</param>
    /// <param name="minTumorCoverage">Minimum tumor coverage (default 5).</param>
    /// <returns>List of somatic variants detected.</returns>
    public static async Task<IList<SomaticVariant>> DetectSomaticVariants(
        DeBruijnGraph tumorGraph,
        BloomFilter normalFilter,
        string reference,
        int refStart,
        double? minAlleleFraction = null,
        int? minTumorCoverage = null)
    {
        if (tumorGraph == null)
        {
            throw new ArgumentNullException(nameof(tumorGraph));
        }

        if (normalFilter == null)
        {
            throw new ArgumentNullException(nameof(normalFilter));
        }

        if (string.IsNullOrEmpty(reference))
        {
            throw new ArgumentNullException(nameof(reference));
        }

        var maf = minAlleleFraction ?? DefaultMinAlleleFraction;
        var minTumorCov = minTumorCoverage ?? DefaultMinTumorCoverage;

        var variants = new List<SomaticVariant>();

        var bubbles = await BubbleFinder.FindBubbles(tumorGraph, tumorGraph.K);
        foreach (var bubble in bubbles)
        {
            // Build k-mer counts from bubble paths to determine alt/ref
            _ = BuildBubbleKmerCounts(bubble, tumorGraph.K);

            // Identify reference path (highest alignment score) and alt paths
            var scoredPaths = bubble.Paths
                .Select(p => new
                {
                    Path = p,
                    AlignScore = ScoreAlignmentToRef(p.Sequence, reference)
                })
                .OrderByDescending(x => x.AlignScore)
                .ThenByDescending(x => x.Path.Coverage)
                .ToList();

            if (scoredPaths.Count < 2)
            {
                continue;
            }

            var refPath = scoredPaths[0].Path;
            var altPaths = scoredPaths.Skip(1).ToList();

            if (altPaths.Count == 0)
            {
                continue;
            }

            // Combine alt paths via consensus
            var altSequence = ConsensusFromAltPaths(altPaths.Select(x => x.Path).ToList());

            var refCoverage = refPath.Coverage;
            var altCoverage = altPaths.Sum(x => x.Path.Coverage);
            var totalCoverage = refCoverage + altCoverage;

            if (totalCoverage == 0 || altCoverage == 0)
            {
                continue;
            }

            // Check somatic: alt k-mers should NOT be in normal Bloom filter
            var altKmers = ExtractKmers(altSequence, tumorGraph.K);
            var absentFromNormal = altKmers.Keys.All(km => !normalFilter.Contains(km));

            if (!absentFromNormal)
            {
                continue; // germline — present in normal
            }

            // Check allele fraction threshold
            var alleleFraction = (double)altCoverage / totalCoverage;
            if (alleleFraction < maf)
            {
                continue;
            }

            // Check tumor coverage threshold
            if (altCoverage < minTumorCov)
            {
                continue;
            }

            var position = FindPositionInReference(refPath.Sequence, reference, refStart);
            if (position < 0)
            {
                continue;
            }

            var quality = ComputePhredQuality(alleleFraction);

            var confidence = DetermineSomaticConfidence(alleleFraction, altCoverage, refCoverage);

            variants.Add(new SomaticVariant
            {
                Position = position,
                Reference = refPath.Sequence,
                Alternate = altSequence,
                TumorCoverage = altCoverage,
                NormalCoverage = refCoverage,
                Quality = quality,
                Confidence = confidence
            });
        }

        variants.Sort((a, b) => a.Position.CompareTo(b.Position));
        return variants;
    }

    /// <summary>
    /// Analyzes a tumor-normal pair with the MultiSampleGraph pattern.
    /// Builds the union Bloom filter and uses the normal subset as the germline filter.
    /// </summary>
    public static async Task<IList<SomaticVariant>> AnalyzeTumorNormalPair(
        string tumorName,
        DeBruijnGraph tumorGraph,
        string normalName,
        DeBruijnGraph normalGraph,
        string reference,
        string chromosome,
        int refStart,
        double? minAlleleFraction = null,
        int? minTumorCoverage = null)
    {
        var multiGraph = new MultiSampleGraph(tumorGraph.K);
        multiGraph.AddSample(tumorName, tumorGraph);
        multiGraph.AddSample(normalName, normalGraph);

        var normalFilter = await BuildNormalFilter(multiGraph, normalName);

        return await DetectSomaticVariants(
            tumorGraph, normalFilter, reference, refStart,
            minAlleleFraction, minTumorCoverage);
    }

    /// <summary>
    /// Builds a Bloom filter containing only the normal-sample k-mers.
    /// </summary>
    private static async Task<BloomFilter> BuildNormalFilter(
        MultiSampleGraph multiGraph,
        string normalName)
    {
        var normalKmers = new HashSet<string>();
        var nodes = await multiGraph.GetNodesInSample(normalName);
        foreach (var node in nodes)
        {
            normalKmers.Add(node.Id);
            foreach (var neighbor in node.OutboundEdges)
            {
                var edgeKmer = node.Id + neighbor[^1];
                if (edgeKmer.Length >= multiGraph.K)
                {
                    normalKmers.Add(edgeKmer[..Math.Min(edgeKmer.Length, multiGraph.K)]);
                }
            }
        }

        // Estimate: 1.5x to account for false negatives
        var estimated = Math.Max(normalKmers.Count * 2, multiGraph.K);
        var filter = new BloomFilter(estimated);
        foreach (var km in normalKmers)
        {
            filter.Add(km);
        }

        return filter;
    }

    private static int ScoreAlignmentToRef(string seq, string reference)
    {
        if (string.IsNullOrEmpty(seq) || seq.Length > reference.Length)
        {
            return 0;
        }

        var best = 0;
        var window = Math.Min(seq.Length, reference.Length);
        for (var i = 0; i <= reference.Length - window; i++)
        {
            var matches = 0;
            for (var j = 0; j < window; j++)
            {
                if (char.ToUpper(reference[i + j]) == char.ToUpper(seq[j]))
                {
                    matches++;
                }
            }

            if (matches > best)
            {
                best = matches;
            }
        }

        return best;
    }

    private static string ConsensusFromAltPaths(List<SequencePath> altPaths)
    {
        if (altPaths == null || altPaths.Count == 0)
        {
            return string.Empty;
        }

        if (altPaths.Count == 1)
        {
            return altPaths[0].Sequence;
        }

        var maxLen = altPaths.Max(p => p.Sequence.Length);
        var consonants = new[] { 'A', 'C', 'G', 'T' };
        var consensus = new char[maxLen];

        for (var i = 0; i < maxLen; i++)
        {
            var baseCounts = new int[4]; // A, C, G, T
            for (var j = 0; j < altPaths.Count; j++)
            {
                if (i < altPaths[j].Sequence.Length)
                {
                    switch (char.ToUpper(altPaths[j].Sequence[i]))
                    {
                        case 'A': baseCounts[0] += altPaths[j].Coverage; break;
                        case 'C': baseCounts[1] += altPaths[j].Coverage; break;
                        case 'G': baseCounts[2] += altPaths[j].Coverage; break;
                        case 'T': baseCounts[3] += altPaths[j].Coverage; break;
                        default: baseCounts[0]++; break;
                    }
                }
            }

            var maxIdx = 0;
            for (var b = 1; b < baseCounts.Length; b++)
            {
                if (baseCounts[b] > baseCounts[maxIdx])
                {
                    maxIdx = b;
                }
            }

            consensus[i] = consonants[maxIdx];
        }

        return new string(consensus);
    }

    private static int FindPositionInReference(string seq, string reference, int refStart)
    {
        if (string.IsNullOrEmpty(seq) || seq.Length > reference.Length)
        {
            return -1;
        }

        var bestPos = -1;
        var bestMatches = 0;
        var window = Math.Min(seq.Length, reference.Length);
        for (var i = 0; i <= reference.Length - window; i++)
        {
            var matches = 0;
            for (var j = 0; j < window; j++)
            {
                if (char.ToUpper(reference[i + j]) == char.ToUpper(seq[j]))
                {
                    matches++;
                }
            }

            if (matches > bestMatches)
            {
                bestMatches = matches;
                bestPos = refStart + i;
            }
        }

        if (bestPos < 0 || bestMatches < window * 0.6)
        {
            return -1;
        }

        return bestPos;
    }

    private static int ComputePhredQuality(double ratio)
    {
        if (ratio <= 0)
        {
            return 0;
        }

        if (ratio >= 1.0)
        {
            return 40;
        }

        var errorProb = 1.0 - ratio;
        if (errorProb >= 1.0)
        {
            return 0;
        }

        var q = -10.0 * Math.Log10(errorProb);
        return Math.Clamp((int)q, 10, 40);
    }

    private static BubbleConfidence DetermineSomaticConfidence(
        double alleleFraction,
        int altCoverage,
        int refCoverage)
    {
        var total = altCoverage + refCoverage;
        if (total < 5 || alleleFraction < 0.2)
        {
            return BubbleConfidence.Low;
        }

        if (total >= 8 && alleleFraction is >= 0.25 and <= 0.75)
        {
            return BubbleConfidence.High; // expected range for somatic heterozygous calls
        }

        return BubbleConfidence.Medium;
    }

    private static Dictionary<string, int> ExtractKmers(string sequence, int k)
    {
        var kmers = new Dictionary<string, int>();
        var seq = sequence.ToUpper();
        for (var i = 0; i <= seq.Length - k; i++)
        {
            var kmer = seq.Substring(i, k);
            kmers[kmer] = kmers.GetValueOrDefault(kmer, 0) + 1;
        }

        return kmers;
    }

    private static Dictionary<string, int> BuildBubbleKmerCounts(Bubble bubble, int k)
    {
        var counts = new Dictionary<string, int>();
        foreach (var path in bubble.Paths)
        {
            if (path == null || string.IsNullOrEmpty(path.Sequence))
            {
                continue;
            }

            var seq = path.Sequence.ToUpper();
            if (seq.Length < k)
            {
                counts.TryGetValue(seq, out var c);
                counts[seq] = c + path.Coverage;
                continue;
            }

            for (var i = 0; i <= seq.Length - k; i++)
            {
                var kmer = seq.Substring(i, k);
                counts.TryGetValue(kmer, out var existing);
                counts[kmer] = existing + path.Coverage;
            }
        }

        return counts;
    }
}
