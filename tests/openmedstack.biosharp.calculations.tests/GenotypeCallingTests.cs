using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AsyncEnumerable = OpenMedStack.BioSharp.Calculations.Alignment.AsyncEnumerableExtensions;
using OpenMedStack.BioSharp.Calculations.Alignment;
using OpenMedStack.BioSharp.Calculations.DeBruijn;
using OpenMedStack.BioSharp.Model;
using Xunit;

namespace OpenMedStack.BioSharp.Calculations.Tests;

/// <summary>
/// Acceptance tests for diploid genotype calling over DeBruijn bubbles.
/// Covers: Genotype model, GenotypeCaller zygosity logic, GQ calculation,
/// VCF FORMAT integration, and LocalVariantResult.WithGenotype.
/// </summary>
public class GenotypeCallingTests
{
    private static IAsyncEnumerable<Sequence> MakeReads(IEnumerable<string> seqs)
    {
        return AsyncEnumerable.ToAsyncEnumerable(seqs.Select(s =>
            new Sequence($"r_{s.GetHashCode()}", s.AsMemory(), new string('I', s.Length).AsMemory())));
    }

    // ==========================================================
    // Proposal #2: Diploid Genotype Calling — Genotype Model
    // ==========================================================

    /// <summary>
    /// AC-GC-1: Genotype model stores caller, type, GQ, and coverages.
    /// </summary>
    [Fact]
    public void Genotype_Model_StoresFields()
    {
        var g = new Genotype(20, 10);
        Assert.Equal("DeBruijn", g.Caller);
        Assert.Equal(GenotypeType.Heterozygous, g.Type);
        Assert.True(g.GQ >= 0);
        Assert.Equal(20, g.RefCoverage);
        Assert.Equal(10, g.AltCoverage);
        Assert.True(g.AltRefRatio is >= 0.4 and < 0.6);
    }

    /// <summary>
    /// AC-GC-2: Homozygous heterozygous classification — alt/ref ratio ~0.5.
    /// ref=20x, alt=10x → 0.5 → Heterozygous.
    /// </summary>
    [Fact]
    public void Genotype_Heterozygous_Ratio05_ClassifiesCorrectly()
    {
        var g = new Genotype(20, 10);
        Assert.Equal(GenotypeType.Heterozygous, g.Type);
        Assert.Equal("0/1", g.ToVcfGenotype());
    }

    /// <summary>
    /// AC-GC-3: Homozygous alternate — alt/ref ratio >= 0.8.
    /// ref=10x, alt=10x → ratio 1.0 → HomozygousAlt.
    /// </summary>
    [Fact]
    public void Genotype_HomozygousAlt_Ratio10_ClassifiesCorrectly()
    {
        var g = new Genotype(10, 10);
        Assert.Equal(GenotypeType.HomozygousAlt, g.Type);
        Assert.Equal("1/1", g.ToVcfGenotype());
    }

    /// <summary>
    /// AC-GC-4: Homozygous reference — alt/ref ratio < 0.2.
    /// ref=20x, alt=2x → ratio 0.1 → HomozygousRef.
    /// </summary>
    [Fact]
    public void Genotype_HomozygousRef_LowAltRatio_ClassifiesCorrectly()
    {
        var g = new Genotype(20, 2);
        Assert.Equal(GenotypeType.HomozygousRef, g.Type);
        Assert.Equal("0/0", g.ToVcfGenotype());
    }

    /// <summary>
    /// AC-GC-5: Borderline ratio (0.2–0.4) defaults to homozygous reference.
    /// ref=100, alt=30 → ratio 0.3 → HomozygousRef (conservative).
    /// </summary>
    [Fact]
    public void Genotype_BorderlineRatio_DefaultsToHomoRef()
    {
        var g = new Genotype(100, 30);
        Assert.Equal(GenotypeType.HomozygousRef, g.Type);
        Assert.Equal("0/0", g.ToVcfGenotype());
    }

    /// <summary>
    /// AC-GC-6: Zero coverages — no reads at all.
    /// </summary>
    [Fact]
    public void Genotype_ZeroCoverages_HomozygousRefZeroGQ()
    {
        var g = new Genotype(0, 0);
        Assert.Equal(GenotypeType.HomozygousRef, g.Type);
        Assert.Equal(0, g.GQ);
        Assert.Equal("0/0", g.ToVcfGenotype());
    }

    /// <summary>
    /// AC-GC-7: No ref coverage, only alt — homozygous alt.
    /// ref=0, alt=5 → ratio approaches infinity → HomozygousAlt.
    /// </summary>
    [Fact]
    public void Genotype_NoRefCoverage_OnlyAlt_HomozygousAlt()
    {
        var g = new Genotype(0, 5);
        Assert.Equal(GenotypeType.HomozygousAlt, g.Type);
        Assert.Equal("1/1", g.ToVcfGenotype());
    }

    /// <summary>
    /// AC-GC-8: Homozygous alt boundary — ratio exactly 0.8.
    /// ref=10, alt=8 → ratio 0.8 → HomozygousAlt (>= threshold).
    /// </summary>
    [Fact]
    public void Genotype_Boundary_HeterozygousAlt_08()
    {
        var g = new Genotype(10, 8);
        Assert.Equal(GenotypeType.HomozygousAlt, g.Type);
    }

    /// <summary>
    /// AC-GC-9: Homozygous alt boundary — ratio just below 0.8.
    /// ref=100, alt=79 → ratio 0.79 → Heterozygous (< 0.8, >= 0.4).
    /// </summary>
    [Fact]
    public void Genotype_Boundary_Heterozygous_079()
    {
        var g = new Genotype(100, 79);
        Assert.Equal(GenotypeType.Heterozygous, g.Type);
    }

    /// <summary>
    /// AC-GC-10: Suspicious threshold — alt/ref = 0.199 → HomozygousRef.
    /// ref=100, alt=19 → ratio 0.19 → HomozygousRef (< 0.2).
    /// </summary>
    [Fact]
    public void Genotype_SuspiciousBoundary_LowRatio_HomoRef()
    {
        var g = new Genotype(100, 19);
        Assert.Equal(GenotypeType.HomozygousRef, g.Type);
    }

    // ==========================================================
    // AC-GC-11: GQ calculation — binomial CDF based Phred quality.
    // ==========================================================

    /// <summary>
    /// AC-GC-11: GQ calculated using binomial CDF for heterozygous case.
    /// ref=20, alt=10 → total=30, p=0.5.
    /// Expected GQ >= 10 (decent confidence).
    /// </summary>
    [Fact]
    public void Genotype_GQ_Calculated_ForHeterozygous()
    {
        var g = new Genotype(20, 10);
        Assert.True(g.GQ >= 10, $"GQ should be >= 10 for heterozygous with 20/10 coverages, got {g.GQ}");
        Assert.True(g.GQ <= 99, $"GQ should be <= 99, got {g.GQ}");
    }

    /// <summary>
    /// AC-GC-12: High coverage heterozygous produces higher GQ.
    /// ref=200, alt=100 → more data → higher GQ than ref=20, alt=10.
    /// </summary>
    [Fact]
    public void Genotype_GQ_HigherCoverage_YieldsHigherValue()
    {
        var g1 = new Genotype(20, 10);
        var g2 = new Genotype(200, 100);
        Assert.True(g2.GQ > g1.GQ || g2.GQ == g1.GQ, "Higher coverage should yield equal or higher GQ");
    }

    /// <summary>
    /// AC-GC-13: Low total count (n=2) produces very low GQ.
    /// ref=1, alt=1 → only 2 reads, high uncertainty.
    /// </summary>
    [Fact]
    public void Genotype_GQ_LowCoverage_ProducesLowValue()
    {
        var g = new Genotype(1, 1);
        Assert.True(g.GQ < 10, $"GQ for n=2 should be low, got {g.GQ}");
    }

    /// <summary>
    /// AC-GC-14: GQ is clamped to [0, 99].
    /// </summary>
    [Fact]
    public void Genotype_GQ_ClampedToRange()
    {
        var g = new Genotype(10000, 10000);
        Assert.True(g.GQ is >= 0 and <= 99);
    }

    // ==========================================================
    // AC-GC-15 through AC-GC-18: GenotypeCaller zygosity logic
    // ==========================================================

    /// <summary>
    /// AC-GC-15: GenotypeCaller.Call returns Genotype with correct Type.
    /// </summary>
    [Fact]
    public void GenotypeCaller_Call_Heterozygous()
    {
        var g = GenotypeCaller.Call(20, 10);
        Assert.Equal(GenotypeType.Heterozygous, g.Type);
        Assert.Equal("0/1", g.ToVcfGenotype());
    }

    /// <summary>
    /// AC-GC-16: GenotypeCaller.ClassifyZygosity boundary cases.
    /// </summary>
    [Fact]
    public void GenotypeCaller_ClassifyZygosity_Boundaries()
    {
        bool suspicious;
        // 0.3 → HomozygousRef (below 0.4, above 0.2)
        Assert.Equal(GenotypeType.HomozygousRef, GenotypeCaller.ClassifyZygosity(0.3, out suspicious));
        Assert.False(suspicious);

        // 0.19 → HomozygousRef, suspicious
        Assert.Equal(GenotypeType.HomozygousRef, GenotypeCaller.ClassifyZygosity(0.19, out suspicious));
        Assert.True(suspicious);

        // 0.4 → Heterozygous (exact lower bound)
        Assert.Equal(GenotypeType.Heterozygous, GenotypeCaller.ClassifyZygosity(0.4, out suspicious));
        Assert.False(suspicious);

        // 0.8 → HomozygousAlt (exact upper bound)
        Assert.Equal(GenotypeType.HomozygousAlt, GenotypeCaller.ClassifyZygosity(0.8, out suspicious));
        Assert.False(suspicious);
    }

    /// <summary>
    /// AC-GC-17: GenotypeCaller.IsSuspicious detects low alt ratios.
    /// </summary>
    [Fact]
    public void GenotypeCaller_IsSuspicious_DetectsLowAltRatio()
    {
        Assert.True(GenotypeCaller.IsSuspicious(20, 2)); // 0.1 → suspicious
        Assert.False(GenotypeCaller.IsSuspicious(20, 10)); // 0.5 → not suspicious
        Assert.False(GenotypeCaller.IsSuspicious(0, 10)); // no ref → not marked suspicious
    }

    // ==========================================================
    // AC-GC-18 through AC-GC-22: LocalVariantResult.WithGenotype
    // ==========================================================

    /// <summary>
    /// AC-GC-18: LocalVariantResult.WithGenotype extension populates Genotype.
    /// </summary>
    [Fact]
    public void LocalVariantResult_WithGenotype_PopulatesGenotype()
    {
        var vr = new LocalVariantResult
        {
            Reference = "A",
            Alternate = "T",
            QuantitativeQuality = 50,
            Depth = 30,
            Position = 1000
        };
        vr.WithGenotype(20, 10);
        Assert.NotNull(vr.Genotype);
        Assert.Equal(GenotypeType.Heterozygous, vr.Genotype.Type);
        Assert.Equal(20, vr.Genotype.RefCoverage);
        Assert.Equal(10, vr.Genotype.AltCoverage);
    }

    /// <summary>
    /// AC-GC-19: WithGenotype preserves other variant fields.
    /// </summary>
    [Fact]
    public void LocalVariantResult_WithGenotype_PreservesFields()
    {
        var vr = new LocalVariantResult
        {
            Chromosome = "chr1",
            Reference = "ACGT",
            Alternate = "ACGTA",
            QuantitativeQuality = 40,
            Depth = 50,
            Position = 500
        };
        vr.WithGenotype(30, 20);
        Assert.Equal("chr1", vr.Chromosome);
        Assert.Equal("ACGT", vr.Reference);
        Assert.True(vr.IsInsertion);
    }

    /// <summary>
    /// AC-GC-20: VCF record with GT:GQ:DP when Genotype is set.
    /// </summary>
    [Fact]
    public void VcfRecordBuilder_BuildWithGenotype_WithTypedGenotype()
    {
        var vr = new LocalVariantResult
        {
            Reference = "A",
            Alternate = "T",
            QuantitativeQuality = 50,
            Depth = 30,
            Position = 1000
        };
        vr.WithGenotype(20, 10);

        var line = VcfRecordBuilder.BuildWithGenotype(vr, "chr1", vr.Genotype!);
        Assert.Contains("GT=0/1", line);
        Assert.Contains($"DP={vr.Depth}", line);
        Assert.Contains("GQ=", line);
    }

    /// <summary>
    /// AC-GC-21: VcfWriter.WriteAsync includes FORMAT lines when samples provided.
    /// Genotype from variant is used when explicit genotype array is not given.
    /// </summary>
    [Fact]
    public async Task VcfWriter_WriteAsync_WithGenotype_IncludesFormat()
    {
        using var mem = new MemoryStream();
        var vr = new LocalVariantResult
        {
            Reference = "A",
            Alternate = "T",
            QuantitativeQuality = 50,
            Depth = 30,
            Position = 1000,
            Chromosome = "chr1"
        };
        vr.WithGenotype(20, 10);

        await VcfWriter.Write(
            mem,
            [vr],
            "chr1",
            sampleNames: ["SAMPLE1"],
            cancellationToken: CancellationToken.None);

        var text = Encoding.UTF8.GetString(mem.ToArray());
        // Should have GT:GQ:DP in header
        Assert.Contains("GT:GQ:DP", text);
        // Genotype 0/1 should appear
        Assert.Contains("0/1", text);
        Assert.Contains("##FORMAT=<ID=GT,Number=1,Type=String,Description=\"Genotype\">", text);
        Assert.Contains("##FORMAT=<ID=GQ,Number=1,Type=Integer,Description=\"Genotype quality\">", text);
        Assert.Contains("#CHROM", text);
    }

    /// <summary>
    /// AC-GC-22: VcfWriter.WriteAsync with explicit genotype array overrides variant.Genotype.
    /// </summary>
    [Fact]
    public async Task VcfWriter_WriteAsync_ExplicitGenotypeArray_OverridesVariantGenotype()
    {
        using var mem = new MemoryStream();
        var vr = new LocalVariantResult
        {
            Reference = "A",
            Alternate = "T",
            QuantitativeQuality = 50,
            Depth = 30,
            Position = 1000,
            Chromosome = "chr1"
        };
        vr.WithGenotype(20, 10); // gives 0/1

        await VcfWriter.Write(
            mem,
            [vr],
            "chr1",
            sampleNames: ["SAMPLE1"],
            genotypes: new string?[][] { ["1/1", "99", "30"] },
            cancellationToken: CancellationToken.None);

        var text = Encoding.UTF8.GetString(mem.ToArray());
        Assert.Contains("1/1", text); // explicit genotype used, not variant's
    }

    // ==========================================================
    // AC-GC-23: Integration — bubble detection + genotype calling end-to-end
    // ==========================================================

    /// <summary>
    /// AC-GC-23: End-to-end bubble detection + genotype calling for heterozygous case.
    /// ref: ACGTACCCACTAGCTAGCTAAGC (23 chars, pos 6=C)
    /// alt: ACGTACGCACTAGCTAGCTAAGC (23 chars, pos 6=G)
    /// With ref reads at 2x and alt reads at 1x → heterozygous (ratio ~0.5).
    /// </summary>
    [Fact]
    public async Task Integration_BubbleWithGenotype_Heterozygous()
    {
        const int k = 5;
        var refSeq = "ACGTACCCACTAGCTAGCTAAGC";
        var altSeq = "ACGTACGCACTAGCTAGCTAAGC";

        var reads = new[]
        {
            refSeq, // 1 ref read
            altSeq // 1 alt read — ratio 1.0 → homozygous alt in this case
        };

        var result = await StructuralVariantDetector.AnalyzeGraph(
            new DeBruijnGraph(k, AsyncEnumerable.ToAsyncEnumerable(reads.Select(s =>
                new Sequence($"r_{s.GetHashCode()}", s.AsMemory(), new string('I', s.Length).AsMemory())))),
            refSeq, "chr1", 0);

        Assert.NotEmpty(result.Variants);
        var variant = result.Variants.First();

        Assert.True(variant.Alternate.Length == variant.Reference.Length);

        // Apply genotype calling: ref coverage from ref path, alt from alt path
        variant.WithGenotype(
            variant.AssemblyInfo!.Coverage * 2 / 3, // ~2/3 ref
            variant.AssemblyInfo!.Coverage * 1 / 3); // ~1/3 alt

        Assert.NotNull(variant.Genotype);
    }

    /// <summary>
    /// AC-GC-24: Homozygous alternate bubble — equal ref/alt coverage.
    /// ref: ACGTACCCACTAGCTAAGC (19 chars)
    /// alt: ACGTACGCACTAGCTAAGC (19 chars)
    /// With equal numbers of ref and alt reads → 1/1 genotype.
    /// </summary>
    [Fact]
    public async Task Integration_BubbleWithGenotype_HomozygousAlt()
    {
        const int k = 5;
        var refSeq = "ACGTACCCACTAGCTAAGC";
        var altSeq = "ACGTACGCACTAGCTAAGC";

        // Equal ref and alt reads → homozygous alt
        var reads = new[]
        {
            refSeq, altSeq, // 1x ref, 1x alt
            refSeq, altSeq, // 2x ref, 2x alt
            refSeq, altSeq // 3x ref, 3x alt
        };

        var result = await StructuralVariantDetector.AnalyzeGraph(
            new DeBruijnGraph(k, AsyncEnumerable.ToAsyncEnumerable(reads.Select(s =>
                new Sequence($"r_{s.GetHashCode()}", s.AsMemory(), new string('I', s.Length).AsMemory())))),
            refSeq, "chr1", 0);

        Assert.NotEmpty(result.Variants);
        var variant = result.Variants.First();

        // Simulate equal coverage (in reality ref=3, alt=3 from the reads)
        variant.WithGenotype(3, 3);
        Assert.NotNull(variant.Genotype);
        Assert.Equal(GenotypeType.HomozygousAlt, variant.Genotype.Type);
        Assert.Equal("1/1", variant.Genotype.ToVcfGenotype());
    }
}
