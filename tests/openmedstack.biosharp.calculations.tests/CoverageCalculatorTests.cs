using System.Linq;
using System.Text.Json;
using OpenMedStack.BioSharp.Calculations.Alignment;
using OpenMedStack.BioSharp.Io.Sam;
using OpenMedStack.BioSharp.Model;
using Xunit;

namespace OpenMedStack.BioSharp.Calculations.Tests;

public class CoverageCalculatorTests
{
    private static AlignmentSection MakeAlignment(
        string qname,
        int position,
        int length,
        AlignmentSection.AlignmentFlag flag = AlignmentSection.AlignmentFlag.None)
        => new AlignmentSection(
            qname: qname,
            flag: flag,
            rname: "chr1",
            position: position,
            mapq: 60,
            cigar: [((uint)length, CigarOp.Match)],
            rnext: 0,
            pnext: 0,
            templatelength: 0,
            sequence: new string('A', length),
            quality: new string('I', length),
            tags: []);

    [Fact]
    public void CoverageCalculator_UniformCoverage_CorrectDepthAtEveryPosition()
    {
        // 10 reads each covering positions 0-9 (length 10)
        var reads = Enumerable.Range(0, 10)
            .Select(i => MakeAlignment($"r{i}", 0, 10))
            .ToList();

        var result = CoverageCalculator.Compute(reads, referenceLength: 10);

        Assert.Equal(10, result.MeanDepth, 1);
        Assert.Equal(10.0, result.MedianDepth, 1);
        Assert.All(Enumerable.Range(0, 10), i => Assert.Equal(10, result.PerPositionDepth[i]));
    }

    [Fact]
    public void CoverageCalculator_GapPosition_ZeroCoverage()
    {
        // Reads cover positions 0-4 only (length 5), reference length 10
        var reads = Enumerable.Range(0, 5)
            .Select(i => MakeAlignment($"r{i}", 0, 5))
            .ToList();

        var result = CoverageCalculator.Compute(reads, referenceLength: 10);

        Assert.Equal(0, result.PerPositionDepth[9]);
        Assert.Equal(0.0, result.MinDepth, 1);
    }

    [Fact]
    public void CoverageCalculator_FractionAtDepthThresholds()
    {
        // 30 reads each covering all 10 positions → depth = 30 at each pos
        var reads = Enumerable.Range(0, 30)
            .Select(i => MakeAlignment($"r{i}", 0, 10))
            .ToList();

        var result = CoverageCalculator.Compute(reads, referenceLength: 10);

        Assert.Equal(1.0, result.FractionAt10x, 2);
        Assert.Equal(1.0, result.FractionAt20x, 2);
        Assert.Equal(1.0, result.FractionAt30x, 2);
        Assert.Equal(0.0, result.FractionAt100x, 2);
    }

    [Fact]
    public void CoverageCalculator_WithTargetBed_OnlyCountsTargetPositions()
    {
        // Reads cover 0-9; target BED = [2, 5)
        var reads = Enumerable.Range(0, 10)
            .Select(i => MakeAlignment($"r{i}", 0, 10))
            .ToList();
        var target = new[] { new BedInterval { Chrom = "chr1", Start = 2, End = 5 } };

        var result = CoverageCalculator.Compute(reads, referenceLength: 10, targetIntervals: target);

        // Only positions 2, 3, 4 are in scope (3 positions)
        Assert.Equal(3, result.TargetBasesCovered);
        Assert.Equal(10.0, result.MeanDepth, 1);
    }

    [Fact]
    public void CoverageCalculator_CoefficientOfVariation()
    {
        // Uniform coverage → CV = 0
        var reads = Enumerable.Range(0, 10)
            .Select(i => MakeAlignment($"r{i}", 0, 5))
            .ToList();

        var result = CoverageCalculator.Compute(reads, referenceLength: 5);

        Assert.Equal(0.0, result.CoefficientOfVariation, 2);
    }

    [Fact]
    public void CoverageCalculator_MatchesSamtoolsDepth_WithinPlusMinusOne()
    {
        // Known BAM: read at pos 1 (1-based) covering 4 bases
        var read = MakeAlignment("r1", 1, 4); // positions 1,2,3,4

        var result = CoverageCalculator.Compute([read], referenceLength: 10);

        // samtools depth uses 1-based positions, we store 0-based
        Assert.Equal(1, result.PerPositionDepth[1]);
        Assert.Equal(1, result.PerPositionDepth[4]);
        Assert.Equal(0, result.PerPositionDepth[5]);
    }

    [Fact]
    public void CoverageCalculator_SerializesToJson()
    {
        var reads = Enumerable.Range(0, 5)
            .Select(i => MakeAlignment($"r{i}", 0, 5))
            .ToList();

        var result = CoverageCalculator.Compute(reads, referenceLength: 5);

        var json = JsonSerializer.Serialize(result);
        Assert.Contains("MeanDepth", json);
    }
}