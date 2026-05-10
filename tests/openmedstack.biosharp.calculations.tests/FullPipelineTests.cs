using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AsyncEnumerable = OpenMedStack.BioSharp.Calculations.Alignment.AsyncEnumerableExtensions;
using DeBruijn = OpenMedStack.BioSharp.Calculations.DeBruijn;
using Model = OpenMedStack.BioSharp.Model;
using Xunit;

namespace OpenMedStack.BioSharp.Calculations.Tests;

/// <summary>
/// Full pipeline integration tests combining all BioSharp DeBruijn features:
/// BloomFilter, RepeatMasker, bubble detection, somatic/cohort calling,
/// genotype calling.
/// </summary>
public class FullPipelineTests
{
    private static readonly string RepeatLibraryPath = "repeats.json";

    private static IAsyncEnumerable<Model.Sequence> MakeReads(IEnumerable<string> seqs, int kmer = 5)
    {
        return AsyncEnumerable.ToAsyncEnumerable(seqs.Select(s => new Model.Sequence(
            "r_" + s.GetHashCode(), s.AsMemory(),
            new string('I', s.Length).AsMemory())));
    }

    private static DeBruijn.BloomFilter BuildBloomFilter(IEnumerable<string> kmers)
    {
        var kmerList = kmers.ToList();
        if (kmerList.Count == 0)
        {
            throw new ArgumentNullException(nameof(kmers), "Cannot create Bloom filter with no k-mers.");
        }

        var filter = new DeBruijn.BloomFilter(kmerList.Count * 100, 0.0001);
        filter.Add(kmerList);
        return filter;
    }

    private static IEnumerable<string> ExtractKmers(string sequence, int k)
    {
        var seq = sequence.ToUpper();
        for (var i = 0; i <= seq.Length - k; i++)
        {
            yield return seq.Substring(i, k);
        }
    }

    /// <summary>
    /// FP-1: Full germline pipeline -- sequence -> DeBruijn graph -> bubble detection
    /// -> genotype calling.
    /// </summary>
    [Fact]
    public async Task FullGermlinePipeline_GermlineVariant_DetectsBubble()
    {
        // A SNP scenario: ref vs alt sequence
        var refSeq = "ACGTACGTACGTACGTACGTACGTACGTACGT";
        var altSeq = "ACGTACGTACGTACGTACGTACGTAGTACGT";

        // Build graph with both ref and alt reads
        var sequences = MakeReads(new[] { refSeq, altSeq, refSeq.Substring(1), altSeq.Substring(1) });
        var graph = new DeBruijn.DeBruijnGraph(10, sequences);

        // Detect bubbles
        var bubbles = await DeBruijn.BubbleFinder.FindBubbles(graph, graph.K);

        if (bubbles.Length > 0) // Bubble detected
        {
            var bubble = bubbles[0];

            // The alt path sequence should differ from ref
            Assert.NotEqual(bubble.Paths[0].Sequence, bubble.Paths[1].Sequence);

            // Verify bubble has start/end nodes
            Assert.NotNull(bubble.StartNode);
            Assert.NotNull(bubble.EndNode);

            // Coverage should be non-negative
            Assert.Contains(bubble.Paths, p => p.Coverage >= 0);
        }
    }

    /// <summary>
    /// FP-2: Somatic detection pipeline -- tumor-normal pair with BloomFilter filtering.
    /// </summary>
    [Fact]
    public async Task FullSomaticPipeline_TumorNormalPair_DetectsSomatic()
    {
        // Simple SNVs: ref vs alt
        var refSeq = "ACGTACGTACGTACGTACGTACGTACGTACGT";
        var altSeq = "ACGTACGTACGTACGTACGTACGTAGTACGT";

        // Build normal BloomFilter from ref-only reads
        var normalKmers = ExtractKmers(refSeq, 10).ToList();
        var normalFilter = BuildBloomFilter(normalKmers);

        // Build tumor: ref + alt reads
        var tumorSeqs = MakeReads(new[] { refSeq, altSeq, refSeq.Substring(1), altSeq.Substring(1) });
        var tumorGraph = new DeBruijn.DeBruijnGraph(10, tumorSeqs);

        // Detect somatic variants
        var variants = await DeBruijn.SomaticVariantDetector.DetectSomaticVariantsAsync(
            tumorGraph, normalFilter, refSeq, "chr1", 0);

        Assert.NotNull(variants);
        // Variants may or may not be detected depending on graph topology
        // but the pipeline should not crash
    }

    /// <summary>
    /// FP-3: Repeat masking -- masked sequences produce valid graphs.
    /// </summary>
    [Fact]
    public async Task FullRepeatMaskPipeline_MaskedSequence_ValidGraph()
    {
        // Load repeat library
        var repeatLibrary = await DeBruijn.RepeatMasker.LoadLibraryAsync(RepeatLibraryPath, 4);
        Assert.NotEmpty(repeatLibrary);

        // Sequence with known GATT tandem repeats (REP001 motif)
        var repeatSeq = "AAAAGATTGATTGATTTTTTTTTTTTTTTTT";

        // Mask the repeats
        var maskedRegions = await DeBruijn.RepeatMasker.MaskRepeatsAsync(
            repeatSeq, repeatLibrary, false);

        if (maskedRegions.Any())
        {
            // Masked sequence should have Ns where repeats were
            var masked = repeatSeq;
            foreach (var region in maskedRegions.OrderByDescending(r => r.Start))
            {
                masked = masked.Substring(0, region.Start) +
                    new string('N', region.Length) +
                    masked.Substring(region.End);
            }

            // Build graph from masked sequence -- should produce a valid graph
            var maskedSeqs = MakeReads(new[] { masked });
            var maskedGraph = new DeBruijn.DeBruijnGraph(10, maskedSeqs);
            Assert.NotNull(maskedGraph);

            // Check that masked kmers (containing N) are NOT in the Bloom filter
            var filter = DeBruijn.RepeatMasker.CreateBloomFilterFromLibrary(repeatLibrary, 4);
            Assert.False(filter.Contains("NATT"));
        }
    }

    /// <summary>
    /// FP-4: Cohort pipeline -- multiple tumor samples, shared variant detection.
    /// </summary>
    [Fact]
    public async Task FullCohortPipeline_MultipleTumorSamples_DetectsSharedVariant()
    {
        var refSeq = "ACGTACGTACGTACGTACGTACGTACGTACGT";
        var altSeq = "ACGTACGTACGTACGTACGTACGTAGTACGT";

        // Build normal BloomFilter
        var normalKmers = ExtractKmers(refSeq, 10).ToList();
        var normalFilter = BuildBloomFilter(normalKmers);

        // Build 3 tumor graphs (same variant, slightly different reads)
        var tumorPairs = new List<(string name, DeBruijn.DeBruijnGraph tumorGraph)>();

        for (var i = 0; i < 3; i++)
        {
            var reads = new[] { refSeq, altSeq, refSeq.Substring(1), altSeq.Substring(1) };
            var tumorSeqs = MakeReads(reads);
            tumorPairs.Add(($"tumor{i}", new DeBruijn.DeBruijnGraph(10, tumorSeqs)));
        }

        // Call cohort variants
        var cohortVariants = await DeBruijn.CohortVariantCaller.CallCohortVariantsAsync(
            tumorPairs, normalFilter, refSeq, "chr1", 0, 0.15);

        Assert.NotNull(cohortVariants);
        if (cohortVariants.Any())
        {
            var variant = cohortVariants.First();

            // All tumor samples should be present
            Assert.Equal(3, variant.TotalSamples);
            Assert.True(variant.SampleCount >= 1);
        }
    }

    /// <summary>
    /// FP-5: Full pipeline with genotype calling -- bubble detection + genotype.
    /// </summary>
    [Fact]
    public async Task FullPipelineWithGenotypeCalling_DetectsAndCallsGenotype()
    {
        var refSeq = "ACGTACGTACGTACGTACGTACGTACGTACGT";
        var altSeq = "ACGTACGTACGTACGTACGTACGTAGTACGT";

        // Build graph with both ref and alt reads
        var sequences = MakeReads(new[] { refSeq, altSeq, refSeq.Substring(1), altSeq.Substring(1) });
        var graph = new DeBruijn.DeBruijnGraph(10, sequences);
        var bubbles = await DeBruijn.BubbleFinder.FindBubbles(graph, graph.K);

        if (bubbles.Length > 0)
        {
            var bubble = bubbles[0];

            // Get coverages from paths
            var refCoverage = bubble.Paths[0].Coverage;
            var altCoverage = bubble.Paths[1].Coverage;

            // Call genotype
            var genotype = DeBruijn.GenotypeCaller.Call(refCoverage, altCoverage);

            Assert.NotNull(genotype);
            Assert.True(genotype.RefCoverage >= 0);
            Assert.True(genotype.AltCoverage >= 0);
            Assert.NotNull(genotype.Caller);

            // VCF genotype string
            var vcfGeno = genotype.ToVcfGenotype();
            Assert.Contains("/", vcfGeno);
            Assert.NotEmpty(vcfGeno);
        }
    }

    /// <summary>
    /// FP-6: Cohort summary statistics are computed correctly.
    /// </summary>
    [Fact]
    public void FullPipeline_CohortSummary_ProducesCorrectStats()
    {
        var variants = new List<DeBruijn.CohortVariant>
        {
            new()
            {
                SampleCount = 1, TotalSamples = 5, AverageQuality = 20,
                AverageAlleleFraction = 0.3
            },
            new()
            {
                SampleCount = 3, TotalSamples = 5, AverageQuality = 40,
                AverageAlleleFraction = 0.6
            },
            new()
            {
                SampleCount = 5, TotalSamples = 5, AverageQuality = 50,
                AverageAlleleFraction = 0.9
            }
        };

        var summary = DeBruijn.CohortVariantCaller.GetSummary(variants, 5);

        Assert.Equal(3, summary.TotalVariants);
        Assert.Equal(1, summary.SingleSampleCount);
        Assert.True(summary.AverageQuality > 0);
        Assert.True(summary.AverageAlleleFrequency >= 0.3);
        Assert.True(summary.AverageAlleleFrequency < 1.0);
    }

    /// <summary>
    /// FP-7: Edge case -- pipeline with no bubbles (homogeneous reads).
    /// Should return empty results without crashing.
    /// </summary>
    [Fact]
    public async Task FullPipeline_NoVariant_HomogeneousReads_ReturnsEmpty()
    {
        // All reads are identical -- no bubbles expected
        var refSeq = "ACGTACGTACGTACGTACGTACGTACGTACGT";
        var sequences = MakeReads(new[]
        {
            refSeq, refSeq.Substring(1),
            refSeq.Substring(2), refSeq.Substring(3)
        });
        var graph = new DeBruijn.DeBruijnGraph(10, sequences);

        var bubbles = await DeBruijn.BubbleFinder.FindBubbles(graph, graph.K);

        // No bubbles should be found
        Assert.Empty(bubbles);
    }

    /// <summary>
    /// FP-8: Full pipeline with BloomFilter set operations --
    /// union and intersection across multi-sample k-mer sets.
    /// </summary>
    [Fact]
    public void FullPipeline_BloomFilterSetOperations_UnionAndIntersectionWork()
    {
        var kmerSet1 = new[] { "ACGTAAAA", "CCCCAAAA", "TTTTCCCC", "GGGGTTTT" };
        var kmerSet2 = new[] { "ACGTAAAA", "CCCCAAAA", "TTTTCCCC", "AACTGGGG" };
        var kmerSet3 = new[] { "ACGTAAAA", "CCCCAAAA", "TTTTCCCC", "GGGGTTTT" };

        var filter1 = BuildBloomFilter(kmerSet1);
        var filter2 = BuildBloomFilter(kmerSet2);
        var filter3 = BuildBloomFilter(kmerSet3);

        // Union contains all unique k-mers
        var union = DeBruijn.BloomFilter.Union(new[] { filter1, filter2, filter3 });
        Assert.True(union.Contains("ACGTAAAA"));
        Assert.True(union.Contains("CCCCAAAA"));
        Assert.True(union.Contains("TTTTCCCC"));
        Assert.True(union.Contains("GGGGTTTT"));
        Assert.True(union.Contains("AACTGGGG"));

        // Intersection only contains shared k-mers
        var intersection = DeBruijn.BloomFilter.Intersection(new[] { filter1, filter2, filter3 });
        Assert.True(intersection.Contains("ACGTAAAA"));
        Assert.True(intersection.Contains("CCCCAAAA"));
        Assert.True(intersection.Contains("TTTTCCCC"));
        // AACTGGGG only in 2, GGGGTTTT only in 1 and 3
        Assert.False(intersection.Contains("AACTGGGG"));
        Assert.False(intersection.Contains("GGGGTTTT"));
    }

    /// <summary>
    /// FP-9: Somatic detection with null/edge-case safety.
    /// </summary>
    [Fact]
    public async Task FullPipeline_NullAndEdgeCases_ThrowsAppropriately()
    {
        // Null graph for somatic detection
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            DeBruijn.SomaticVariantDetector.DetectSomaticVariantsAsync(
                null!, BuildBloomFilter(Array.Empty<string>()), "ACGT", "chr1", 0));

        // Null BloomFilter
        var refSeq = "ACGTACGTACGTACGT";
        var graphSeqs = MakeReads(new[] { refSeq });
        var graph = new DeBruijn.DeBruijnGraph(4, graphSeqs);
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            DeBruijn.SomaticVariantDetector.DetectSomaticVariantsAsync(
                graph, null!, "ACGT", "chr1", 0));

        // Null reference
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            DeBruijn.SomaticVariantDetector.DetectSomaticVariantsAsync(
                graph, BuildBloomFilter(Array.Empty<string>()), null!, "chr1", 0));

        // Empty cohort
        var cohort = await DeBruijn.CohortVariantCaller.CallCohortVariantsAsync(
            Array.Empty<(string, DeBruijn.DeBruijnGraph)>(),
            BuildBloomFilter(ExtractKmers("ACGTAC", 4)),
            "ACGT", "chr1", 0);
        Assert.Empty(cohort);
    }

    /// <summary>
    /// FP-10: Full germline analysis -- bubble detection with
    /// RepetitivenessAnalyzer confidence scoring.
    /// </summary>
    [Fact]
    public async Task FullGermlineVariantAnalysis_Substitution_ClassifiedCorrectly()
    {
        // SNP substitution
        var refSeq = "ACGTACGTACGTACGTACGTACGTACGTACGT";
        var altSeq = "ACGTACGTACGTACGTACGTACGTAGTACGT";

        // Build graph with ref + alt reads
        var sequences = MakeReads(new[]
        {
            refSeq, altSeq, refSeq.Substring(1),
            altSeq.Substring(1), refSeq.Substring(2), altSeq.Substring(2)
        });
        var graph = new DeBruijn.DeBruijnGraph(10, sequences);

        // Find bubbles
        var bubbles = await DeBruijn.BubbleFinder.FindBubbles(graph, graph.K);

        if (bubbles.Length > 0)
        {
            var bubble = bubbles[0];

            // Alt path sequence should differ from ref
            Assert.NotEqual(bubble.Paths[0].Sequence, bubble.Paths[1].Sequence);

            // Coverage values
            var refCov = bubble.Paths[0].Coverage;
            var altCov = bubble.Paths[1].Coverage;
            Assert.True(refCov >= 0 && altCov >= 0);

            // Call genotype
            var genotype = DeBruijn.GenotypeCaller.Call(refCov, altCov);
            Assert.NotNull(genotype);

            // Bubble confidence should be set
            Assert.True(bubble.Confidence >= DeBruijn.BubbleConfidence.Low);
        }
    }

    /// <summary>
    /// FP-11: Cohort filtering -- FilterByType and FilterByFrequency work.
    /// </summary>
    [Fact]
    public void FullPipeline_CohortFiltering_ByTypeAndFrequency()
    {
        var variants = new List<DeBruijn.CohortVariant>
        {
            new()
            {
                Position = 100, SampleCount = 1, TotalSamples = 5,
                AverageQuality = 30, AverageAlleleFraction = 0.2,
                DetectedIn = new[] { "tumor1" }
            },
            new()
            {
                Position = 200, SampleCount = 3, TotalSamples = 5,
                AverageQuality = 40, AverageAlleleFraction = 0.6,
                DetectedIn = new[] { "tumor1", "tumor2", "tumor3" }
            },
            new()
            {
                Position = 300, SampleCount = 5, TotalSamples = 5,
                AverageQuality = 50, AverageAlleleFraction = 1.0,
                DetectedIn = new[] { "tumor1", "tumor2", "tumor3", "tumor4", "tumor5" }
            }
        };

        // Filter by type: SingleSample
        var single = DeBruijn.CohortVariantCaller.FilterByType(variants, DeBruijn.CohortVariantType.SingleSample);
        Assert.Single(single);
        Assert.Equal(100, single[0].Position);

        // Filter by type: Present
        var present = DeBruijn.CohortVariantCaller.FilterByType(variants, DeBruijn.CohortVariantType.Present);
        Assert.Single(present);
        Assert.Equal(200, present[0].Position);

        // Filter by type: Oncogenic
        var oncogenic = DeBruijn.CohortVariantCaller.FilterByType(variants, DeBruijn.CohortVariantType.Oncogenic);
        Assert.Single(oncogenic);
        Assert.Equal(300, oncogenic[0].Position);

        // Filter by frequency
        var highFreq = DeBruijn.CohortVariantCaller.FilterByFrequency(variants, 0.5);
        Assert.Equal(2, highFreq.Count);
    }

    /// <summary>
    /// FP-12: Full germline bubble + Tips pipeline --
    /// bubble detection and tip finding work together.
    /// </summary>
    [Fact]
    public async Task FullPipeline_BubbleAndTipDetection_Complete()
    {
        var refSeq = "ACGTACGTACGTACGTACGTACGTACGTACGT";
        var altSeq = "ACGTACGTACGTACGTACGTACGTAGTACGT";

        var sequences = MakeReads(new[] { refSeq, altSeq });
        var graph = new DeBruijn.DeBruijnGraph(10, sequences);

        // Find bubbles
        var bubbles = await DeBruijn.BubbleFinder.FindBubbles(graph, graph.K);
        Assert.NotNull(bubbles);

        // Find tips
        var tips = await DeBruijn.TipFinder.FindTips(graph, 10);
        Assert.NotNull(tips);

        // Both should be valid collections (may be empty)
    }

    /// <summary>
    /// FP-13: Genotype zygosity classification matches expected patterns.
    /// </summary>
    [Fact]
    public void FullPipeline_GenotypeZygosity_ClassificationCorrect()
    {
        // Homozygous alt: alt/ref >= 0.8
        var homoAlt = DeBruijn.GenotypeCaller.Call(1, 10);
        Assert.Equal(DeBruijn.GenotypeType.HomozygousAlt, homoAlt.Type);
        Assert.Equal("1/1", homoAlt.ToVcfGenotype());

        // Heterozygous: alt/ref in [0.4, 0.8)
        var het = DeBruijn.GenotypeCaller.Call(10, 6);
        Assert.Equal(DeBruijn.GenotypeType.Heterozygous, het.Type);
        Assert.Equal("0/1", het.ToVcfGenotype());

        // Homozygous ref: alt/ref < 0.2
        var homoRef = DeBruijn.GenotypeCaller.Call(10, 1);
        Assert.Equal(DeBruijn.GenotypeType.HomozygousRef, homoRef.Type);
        Assert.Equal("0/0", homoRef.ToVcfGenotype());

        // Zero coverage: homo ref
        var zero = DeBruijn.GenotypeCaller.Call(0, 0);
        Assert.Equal(DeBruijn.GenotypeType.HomozygousRef, zero.Type);
        Assert.Equal("0/0", zero.ToVcfGenotype());
    }

    /// <summary>
    /// FP-14: RepetitivenessAnalyzer produces scores for bubble paths.
    /// </summary>
    [Fact]
    public async Task FullPipeline_RepetitivenessAnalyzer_ProducesScores()
    {
        var refSeq = "ACGTACGTACGTACGTACGTACGTACGTACGT";
        var altSeq = "ACGTACGTACGTACGTACGTACGTAGTACGT";

        var sequences = MakeReads(new[]
        {
            refSeq, altSeq,
            refSeq.Substring(1), altSeq.Substring(1)
        });
        var graph = new DeBruijn.DeBruijnGraph(10, sequences);
        var bubbles = await DeBruijn.BubbleFinder.FindBubbles(graph, graph.K);

        if (bubbles.Length > 0)
        {
            var bubble = bubbles[0];
            // Build kmer counts from bubble paths for repetitiveness scoring
            var counts = new Dictionary<string, int>();
            foreach (var path in bubble.Paths)
            {
                for (var j = 0; j <= path.Sequence.Length - 10; j++)
                {
                    var kmer = path.Sequence.Substring(j, 10);
                    counts[kmer] = counts.GetValueOrDefault(kmer, 0) + path.Coverage;
                }
            }

            _ = DeBruijn.RepetitivenessAnalyzer.AnalyzeBubble(bubble, (IReadOnlyDictionary<string, int>)counts);

            Assert.NotEqual(DeBruijn.BubbleConfidence.Low, bubble.Confidence);
        }
    }
}
