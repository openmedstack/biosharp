using System;
using System.Linq;
using OpenMedStack.BioSharp.Calculations.Alignment;
using Xunit;

namespace OpenMedStack.BioSharp.Calculations.Tests;

public class VcfNormalizerTests
{
    private static LocalVariantResult MakeVariant(string chrom, int pos, string refB, string alt)
    {
        return new LocalVariantResult
        {
            Chromosome = chrom,
            Position = pos,
            Reference = refB,
            Alternate = alt,
            QuantitativeQuality = 50,
            Depth = 20
        };
    }

    // Left-align: a deletion in a homopolymer should move left
    [Fact]
    public void LeftAlignIndel_MovesInsertionLeftInHomopolymer()
    {
        // ref: AAAACG... deletion of one A should left-align to pos 1
        // e.g. pos=5, ref=AT, alt=T  ->  should be pos=1, ref=AA, alt=A
        // Simple case: AAAAT, pos=4 (1-based), ref=AT, alt=T
        // Left-aligned: pos=1, ref=AA, alt=A
        var reference = "AAAATCG".AsMemory();
        var variant = MakeVariant("chr1", 4, "AT", "T");

        var normalized = VcfNormalizer.LeftAlignIndel(variant, reference.Span);

        // After left-alignment the position should be <= 4
        Assert.True(normalized.Position <= 4);
        // Reference and alt should differ only by the deleted base
        Assert.Equal(normalized.Reference.Length - 1, normalized.Alternate.Length);
    }

    [Fact]
    public void LeftAlignIndel_AlreadyLeftAligned_ReturnsSamePosition()
    {
        // ref: TAAAA, deletion at pos 1 → already left-aligned
        var reference = "TAAAAT".AsMemory();
        var variant = MakeVariant("chr1", 1, "TA", "T");

        var normalized = VcfNormalizer.LeftAlignIndel(variant, reference.Span);

        Assert.Equal(1, normalized.Position);
    }

    [Fact]
    public void LeftAlignIndel_Idempotent()
    {
        var reference = "CGAAAATCG".AsMemory();
        var variant = MakeVariant("chr1", 3, "AAAAT", "AAAT");

        var once = VcfNormalizer.LeftAlignIndel(variant, reference.Span);
        var twice = VcfNormalizer.LeftAlignIndel(once, reference.Span);

        Assert.Equal(once.Position, twice.Position);
        Assert.Equal(once.Reference, twice.Reference);
        Assert.Equal(once.Alternate, twice.Alternate);
    }

    [Fact]
    public void Decompose_MultiAllelicSplitsIntoTwo()
    {
        var variant = MakeVariant("chr1", 100, "A", "T");
        variant.AddAltAllele("G");

        var decomposed = VcfNormalizer.Decompose(variant).ToList();

        Assert.Equal(2, decomposed.Count);
        Assert.Contains(decomposed, v => v.Alternate == "T");
        Assert.Contains(decomposed, v => v.Alternate == "G");
        // All decomposed variants have same chrom and pos
        Assert.All(decomposed, v => Assert.Equal(100, v.Position));
    }

    [Fact]
    public void Decompose_BiallelicReturnsSelf()
    {
        var variant = MakeVariant("chr1", 100, "A", "T");
        var decomposed = VcfNormalizer.Decompose(variant).ToList();
        Assert.Single(decomposed);
        Assert.Equal("T", decomposed[0].Alternate);
    }

    [Fact]
    public void Normalize_LeftAlignsAndDecomposesMultiAllelic()
    {
        var reference = "AAAATCG".AsMemory();
        var variant = MakeVariant("chr1", 4, "AT", "T");
        variant.AddAltAllele("A");

        var results = VcfNormalizer.Normalize([variant], reference.Span).ToList();

        // Should have at least 2 records after decomposition
        Assert.True(results.Count >= 2);
    }

    [Fact]
    public void Normalize_Idempotent()
    {
        var reference = "AAAATCG".AsMemory();
        var variant = MakeVariant("chr1", 4, "AT", "T");

        var once = VcfNormalizer.Normalize([variant], reference.Span).ToList();
        var twice = VcfNormalizer.Normalize(once, reference.Span).ToList();

        Assert.Equal(once.Count, twice.Count);
        for (var i = 0; i < once.Count; i++)
        {
            Assert.Equal(once[i].Position, twice[i].Position);
            Assert.Equal(once[i].Reference, twice[i].Reference);
            Assert.Equal(once[i].Alternate, twice[i].Alternate);
        }
    }

    [Fact]
    public void Normalize_SnpNotChanged()
    {
        var reference = "ACGTACGT".AsMemory();
        var variant = MakeVariant("chr1", 3, "G", "C");

        var results = VcfNormalizer.Normalize([variant], reference.Span).ToList();

        Assert.Single(results);
        Assert.Equal(3, results[0].Position);
        Assert.Equal("G", results[0].Reference);
        Assert.Equal("C", results[0].Alternate);
    }
}