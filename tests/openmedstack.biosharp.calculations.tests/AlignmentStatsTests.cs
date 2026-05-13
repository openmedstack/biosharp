using System.Text.Json;
using OpenMedStack.BioSharp.Calculations.Alignment;
using OpenMedStack.BioSharp.Io.Sam;
using Xunit;

namespace OpenMedStack.BioSharp.Calculations.Tests;

public class AlignmentStatsTests
{
    private static AlignmentSection MakeAlignment(
        string qname,
        int position,
        string sequence,
        string quality,
        byte mapq = 60,
        AlignmentSection.AlignmentFlag flag = AlignmentSection.AlignmentFlag.None,
        int rnext = 0,
        int pnext = 0,
        int tlen = 0,
        (uint, CigarOp)[]? cigar = null)
        => new AlignmentSection(
            qname: qname,
            flag: flag,
            rname: "chr1",
            position: position,
            mapq: mapq,
            cigar: cigar ?? [((uint)sequence.Length, CigarOp.Match)],
            rnext: rnext,
            pnext: pnext,
            templatelength: tlen,
            sequence: sequence,
            quality: quality,
            tags: []);

    [Fact]
    public void AlignmentStats_CountsTotal_Mapped_Unmapped()
    {
        var mapped = MakeAlignment("r1", 100, "ACGT", "IIII");
        var unmapped = MakeAlignment("r2", 0, "*", "*",
            flag: AlignmentSection.AlignmentFlag.SegmentUnmapped);

        var stats = AlignmentStatsCalculator.Compute([mapped, unmapped]);

        Assert.Equal(2, stats.TotalReads);
        Assert.Equal(1, stats.MappedReads);
        Assert.Equal(1, stats.UnmappedReads);
        Assert.Equal(0.5, stats.MappingRate, 2);
    }

    [Fact]
    public void AlignmentStats_ProperlyPaired_Singletons()
    {
        var paired1 = MakeAlignment("r1", 100, "ACGT", "IIII",
            flag: AlignmentSection.AlignmentFlag.MultipleSegments |
            AlignmentSection.AlignmentFlag.EachSegmentProperlyAligned);
        var singleton = MakeAlignment("r2", 200, "ACGT", "IIII");

        var stats = AlignmentStatsCalculator.Compute([paired1, singleton]);

        Assert.Equal(1, stats.ProperlyPairedReads);
        Assert.Equal(1, stats.SingletonReads);
    }

    [Fact]
    public void AlignmentStats_DuplicateCount()
    {
        var normal = MakeAlignment("r1", 100, "ACGT", "IIII");
        var dup = MakeAlignment("r2", 100, "ACGT", "IIII",
            flag: AlignmentSection.AlignmentFlag.PcrOrOpticalDuplicate);

        var stats = AlignmentStatsCalculator.Compute([normal, dup]);

        Assert.Equal(1, stats.DuplicateReads);
    }

    [Fact]
    public void AlignmentStats_AverageMappingQuality()
    {
        var a1 = MakeAlignment("r1", 100, "ACGT", "IIII", mapq: 60);
        var a2 = MakeAlignment("r2", 200, "ACGT", "IIII", mapq: 20);

        var stats = AlignmentStatsCalculator.Compute([a1, a2]);

        Assert.Equal(40.0, stats.AverageMappingQuality, 1);
    }

    [Fact]
    public void AlignmentStats_ReadLengthDistribution()
    {
        var a1 = MakeAlignment("r1", 100, "ACGT", "IIII");        // len=4
        var a2 = MakeAlignment("r2", 200, "ACGTACGT", "IIIIIIII"); // len=8

        var stats = AlignmentStatsCalculator.Compute([a1, a2]);

        Assert.True(stats.ReadLengthDistribution.ContainsKey(4));
        Assert.True(stats.ReadLengthDistribution.ContainsKey(8));
    }

    [Fact]
    public void AlignmentStats_SerializesToJson()
    {
        var a = MakeAlignment("r1", 100, "ACGT", "IIII");
        var stats = AlignmentStatsCalculator.Compute([a]);

        var json = JsonSerializer.Serialize(stats);
        Assert.Contains("TotalReads", json);
        Assert.Contains("MappingRate", json);
    }
}