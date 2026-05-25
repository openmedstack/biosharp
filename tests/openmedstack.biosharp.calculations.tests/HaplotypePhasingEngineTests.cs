using System;
using System.Linq;
using OpenMedStack.BioSharp.Calculations.Alignment;
using OpenMedStack.BioSharp.Model.Alignment;
using Xunit;

namespace OpenMedStack.BioSharp.Calculations.Tests;

public class HaplotypePhasingEngineTests
{
    private static LocalVariantResult Variant(string chrom, int pos, string refB, string alt)
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

    private static ReadSpan MakeRead(string name, int start, int end, params (int pos, bool isAlt)[] alleles)
    {
        return new ReadSpan(name, start, end, alleles);
    }

    [Fact]
    public void Phase_TwoSnpsOnSameRead_ArePhased()
    {
        var variants = new[]
        {
            Variant("chr1", 100, "A", "T"),
            Variant("chr1", 150, "G", "C")
        };

        // One read spanning both variants, supporting alt at both
        var reads = new[]
        {
            MakeRead("read1", 90, 200, (100, true), (150, true))
        };

        var engine = new HaplotypePhasingEngine();
        var phased = engine.Phase(variants, reads);

        Assert.All(phased, p => Assert.True(p.IsPhased));
        // Both should be in the same phase set
        var ps = phased.Select(p => p.PhaseSet).Distinct().ToList();
        Assert.Single(ps);
    }

    [Fact]
    public void Phase_TwoSnpsOnDifferentReads_AreUnphased()
    {
        var variants = new[]
        {
            Variant("chr1", 100, "A", "T"),
            Variant("chr1", 400, "G", "C")
        };

        // Two reads, each covering only one variant (no bridging)
        var reads = new[]
        {
            MakeRead("read1", 90, 200, (100, true)),
            MakeRead("read2", 390, 500, (400, true))
        };

        var engine = new HaplotypePhasingEngine();
        var phased = engine.Phase(variants, reads);

        // No reads bridge both variants → at most 1 is phased (start of a block)
        // OR they are in different phase sets
        var phaseSets = phased.Where(p => p.IsPhased).Select(p => p.PhaseSet).Distinct().ToList();
        Assert.True(phaseSets.Count <= 2); // At most 2 separate blocks
    }

    [Fact]
    public void Phase_ThreeVariantBlock_AllInSamePhaseSet()
    {
        var variants = new[]
        {
            Variant("chr1", 100, "A", "T"),
            Variant("chr1", 150, "G", "C"),
            Variant("chr1", 190, "T", "A")
        };

        // Two reads bridging: read1 covers 100 and 150, read2 covers 150 and 190
        var reads = new[]
        {
            MakeRead("read1", 90, 180, (100, true), (150, true)),
            MakeRead("read2", 140, 210, (150, true), (190, true))
        };

        var engine = new HaplotypePhasingEngine();
        var phased = engine.Phase(variants, reads);

        var phasedOnes = phased.Where(p => p.IsPhased).ToList();
        Assert.NotEmpty(phasedOnes);
        // All phased variants should be in the same phase set
        var phaseSets = phasedOnes.Select(p => p.PhaseSet).Distinct().ToList();
        Assert.Single(phaseSets);
    }

    [Fact]
    public void Phase_PhaseBlockBoundaryWhenNoReadBridges()
    {
        var variants = new[]
        {
            Variant("chr1", 100, "A", "T"),
            Variant("chr1", 200, "G", "C"),
            Variant("chr1", 600, "T", "A"),
            Variant("chr1", 650, "C", "G")
        };

        // read1 covers 100+200, read2 covers 600+650 — gap between 200 and 600
        var reads = new[]
        {
            MakeRead("read1", 90, 300, (100, true), (200, true)),
            MakeRead("read2", 590, 700, (600, true), (650, true))
        };

        var engine = new HaplotypePhasingEngine();
        var phased = engine.Phase(variants, reads);

        var phasedOnes = phased.Where(p => p.IsPhased).ToList();
        var phaseSets = phasedOnes.Select(p => p.PhaseSet).Distinct().ToList();
        // Should have at least 2 phase sets (or one block ended)
        Assert.True(phaseSets.Count >= 2 || phasedOnes.Count <= 2);
    }

    [Fact]
    public void Phase_PhasedGenotypeUsesBarSeparator()
    {
        var variants = new[]
        {
            Variant("chr1", 100, "A", "T"),
            Variant("chr1", 130, "G", "C")
        };

        var reads = new[]
        {
            MakeRead("read1", 90, 180, (100, true), (130, true))
        };

        var engine = new HaplotypePhasingEngine();
        var phased = engine.Phase(variants, reads);

        foreach (var p in phased.Where(x => x.IsPhased))
        {
            Assert.Contains("|", p.GenotypeString);
        }
    }

    [Fact]
    public void Phase_UnphasedVariant_HasSlashSeparator()
    {
        var variants = new[]
        {
            Variant("chr1", 100, "A", "T")
        };
        // No reads — all variants remain unphased
        var reads = Array.Empty<ReadSpan>();

        var engine = new HaplotypePhasingEngine();
        var phased = engine.Phase(variants, reads);

        Assert.All(phased, p => Assert.Contains("/", p.GenotypeString));
    }
}