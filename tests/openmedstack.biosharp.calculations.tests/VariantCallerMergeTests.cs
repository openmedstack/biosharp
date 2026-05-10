namespace OpenMedStack.BioSharp.Calculations.Tests;

using Alignment;
using Xunit;

/// <summary>
/// Tests for VariantCaller.MergeVariants — correctness of the VariantKey deduplication
/// and handling of multi-allelic (overlapping) loci.
/// Covers the Task 9 specification requirement: "Add unit tests for merge correctness
/// and multi-allelic edge cases."
/// </summary>
public class VariantCallerMergeTests
{
    // ── helpers ──────────────────────────────────────────────────────────────

    private static LocalVariantResult V(string chr, int pos, string @ref, string alt, int quality = 40) =>
        new() { Chromosome = chr, Position = pos, Reference = @ref, Alternate = alt, QuantitativeQuality = quality };

    // ── single-allele deduplication ──────────────────────────────────────────

    [Fact]
    public void MergeVariants_EmptyInput_ReturnsEmpty()
    {
        var result = VariantCaller.MergeVariants([]);
        Assert.Empty(result);
    }

    [Fact]
    public void MergeVariants_SingleVariant_ReturnsSameVariant()
    {
        var v = V("chr1", 100, "A", "T");
        var result = VariantCaller.MergeVariants([v]);
        Assert.Single(result);
        Assert.Equal("A", result[0].Reference);
        Assert.Equal("T", result[0].Alternate);
    }

    [Fact]
    public void MergeVariants_DuplicateVariants_MergesIntoOne()
    {
        var v1 = V("chr1", 100, "A", "T", quality: 30);
        var v2 = V("chr1", 100, "A", "T", quality: 40);
        var v3 = V("chr1", 100, "A", "T", quality: 25);

        var result = VariantCaller.MergeVariants([v1, v2, v3]);

        Assert.Single(result);
        // Depth should be incremented once per duplicate beyond the first.
        Assert.True(result[0].Depth >= 2);
        // Quality should be the maximum observed.
        Assert.Equal(40, result[0].QuantitativeQuality);
    }

    [Fact]
    public void MergeVariants_DifferentAlleles_AreNotMerged()
    {
        var snp  = V("chr1", 100, "A", "T");
        var ins  = V("chr1", 100, "A", "AG");
        var del  = V("chr1", 100, "AC", "A");

        var result = VariantCaller.MergeVariants([snp, ins, del]);

        Assert.Equal(3, result.Length);
    }

    // ── multi-allelic edge cases ──────────────────────────────────────────────

    [Fact]
    public void MergeVariants_TwoAllelesAtSamePosition_BothSurvive()
    {
        // Two distinct ALTs at the same locus (multi-allelic site).
        var alt1 = V("chr1", 200, "G", "A");
        var alt2 = V("chr1", 200, "G", "T");

        var result = VariantCaller.MergeVariants([alt1, alt2]);

        Assert.Equal(2, result.Length);
        var alleles = new System.Collections.Generic.HashSet<string>(
            System.Linq.Enumerable.Select(result, r => r.Alternate));
        Assert.Contains("A", alleles);
        Assert.Contains("T", alleles);
    }

    [Fact]
    public void MergeVariants_MultiAllelicWithDuplicates_DeduplicatesEachAllele()
    {
        // chr2:300 has two distinct ALTs; each observed twice.
        var a1 = V("chr2", 300, "C", "G");
        var a2 = V("chr2", 300, "C", "G");
        var b1 = V("chr2", 300, "C", "A");
        var b2 = V("chr2", 300, "C", "A");
        var b3 = V("chr2", 300, "C", "A");

        var result = VariantCaller.MergeVariants([a1, a2, b1, b2, b3]);

        Assert.Equal(2, result.Length);
        var byAlt = System.Linq.Enumerable.ToDictionary(result, r => r.Alternate);

        // C>G observed twice: depth incremented once beyond first => Depth >= 1.
        Assert.True(byAlt["G"].Depth >= 1);
        // C>A observed three times: depth incremented twice => Depth >= 2.
        Assert.True(byAlt["A"].Depth >= 2);
    }

    // ── key distinctness across chromosomes / positions ───────────────────────

    [Fact]
    public void MergeVariants_SameAlleleDifferentChromosome_AreNotMerged()
    {
        var chr1 = V("chr1", 50, "T", "C");
        var chr2 = V("chr2", 50, "T", "C");

        var result = VariantCaller.MergeVariants([chr1, chr2]);

        Assert.Equal(2, result.Length);
    }

    [Fact]
    public void MergeVariants_SameAlleleDifferentPosition_AreNotMerged()
    {
        var pos1 = V("chr1", 10, "A", "G");
        var pos2 = V("chr1", 11, "A", "G");

        var result = VariantCaller.MergeVariants([pos1, pos2]);

        Assert.Equal(2, result.Length);
    }

    // ── quality handling ──────────────────────────────────────────────────────

    [Fact]
    public void MergeVariants_TakesMaxQualityAcrossDuplicates()
    {
        var low  = V("chrX", 400, "T", "A", quality: 10);
        var high = V("chrX", 400, "T", "A", quality: 60);
        var mid  = V("chrX", 400, "T", "A", quality: 35);

        var result = VariantCaller.MergeVariants([low, high, mid]);

        Assert.Single(result);
        Assert.Equal(60, result[0].QuantitativeQuality);
    }
}
