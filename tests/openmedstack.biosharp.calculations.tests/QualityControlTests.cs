namespace OpenMedStack.BioSharp.Calculations.Tests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Alignment;
using Io.Sam;
using Model;
using Xunit;

// ─────────────────────────────────────────────────────────────────────────────
// QC-1 — FastQQualityReport
// ─────────────────────────────────────────────────────────────────────────────
public class FastQQualityReportTests
{
    private static Sequence MakeSeq(string bases, string quals)
    {
        // Phred+33 encoding: '!' = Q0, 'I' = Q40, 'J' = Q41
        return new Sequence("read", bases.AsMemory(), quals.AsMemory());
    }

    private static async IAsyncEnumerable<Sequence> Seqs(params Sequence[] seqs)
    {
        foreach (var s in seqs)
        {
            yield return s;
        }

        await Task.CompletedTask;
    }

    [Fact]
    public async Task FastQReport_GoodQualityReads_ComputesPerBaseQuality()
    {
        // Quality 'I' = ASCII 73, Phred = 73 - 33 = 40
        var s = MakeSeq("ACGT", "IIII");
        var report = await FastQQualityReport.ComputeAsync(Seqs(s));

        Assert.Equal(4, report.PerBaseQuality.Count);
        Assert.All(report.PerBaseQuality, kv => Assert.Equal(40.0, kv.Value.Mean, 1));
    }

    [Fact]
    public async Task FastQReport_ComputesPerBaseComposition()
    {
        // One read: A, C, G, T at cycles 0-3
        var s = MakeSeq("ACGT", "IIII");
        var report = await FastQQualityReport.ComputeAsync(Seqs(s));

        Assert.Equal(100.0, report.PerBaseComposition[0]['A'], 1);
        Assert.Equal(100.0, report.PerBaseComposition[1]['C'], 1);
        Assert.Equal(100.0, report.PerBaseComposition[2]['G'], 1);
        Assert.Equal(100.0, report.PerBaseComposition[3]['T'], 1);
    }

    [Fact]
    public async Task FastQReport_GcContentIsCorrect()
    {
        // ACGT → 50% GC (G+C = 2, total = 4)
        var s = MakeSeq("ACGT", "IIII");
        var report = await FastQQualityReport.ComputeAsync(Seqs(s));

        Assert.Single(report.GcContentHistogram);
        Assert.True(report.GcContentHistogram.ContainsKey(50));
    }

    [Fact]
    public async Task FastQReport_HighDuplicationRate_DetectedCorrectly()
    {
        var reads = Enumerable.Repeat(MakeSeq("ACGTACGT", "IIIIIIII"), 100)
            .ToList();

        async IAsyncEnumerable<Sequence> Gen()
        {
            foreach (var r in reads)
            {
                yield return r;
            }

            await Task.CompletedTask;
        }

        var report = await FastQQualityReport.ComputeAsync(Gen());
        Assert.True(report.DuplicationLevelEstimate > 0.5);
    }

    [Fact]
    public async Task FastQReport_AdapterContent_DetectedAtPositions()
    {
        // Read with Nextera adapter starting at position 10
        const string adapter = "CTGTCTCTTATACACATCT";
        var seq = "ACGTACGTAC" + adapter;
        var qual = new string('I', seq.Length);

        var report = await FastQQualityReport.ComputeAsync(
            Seqs(MakeSeq(seq, qual)),
            adapterSequence: adapter);

        // Adapter should be detected at position ≥ 10
        var adapterPositions = report.AdapterContentByPosition
            .Where(kv => kv.Value > 0)
            .Select(kv => kv.Key);
        Assert.Contains(adapterPositions, p => p >= 10);
    }

    [Fact]
    public async Task FastQReport_SerializesToJson()
    {
        var s = MakeSeq("ACGT", "IIII");
        var report = await FastQQualityReport.ComputeAsync(Seqs(s));

        var json = JsonSerializer.Serialize(report);
        Assert.Contains("PerBaseQuality", json);
        Assert.Contains("GcContentHistogram", json);
    }

    [Fact]
    public async Task FastQReport_LowQualityTail_DetectedAtHigherCycles()
    {
        // 10 reads where last 4 bases have quality '!' (Q=0)
        var highQ = new string('I', 10); // Q40
        var lowQ = new string('!', 4);   // Q0
        var reads = Enumerable.Range(0, 10)
            .Select(_ => MakeSeq("ACGTACGTACGTNN", highQ + lowQ))
            .ToList();

        async IAsyncEnumerable<Sequence> Gen()
        {
            foreach (var r in reads)
            {
                yield return r;
            }

            await Task.CompletedTask;
        }

        var report = await FastQQualityReport.ComputeAsync(Gen());
        // Cycles 10-13 should have mean quality 0
        for (var i = 10; i < 14; i++)
        {
            Assert.Equal(0.0, report.PerBaseQuality[i].Mean, 0);
        }
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// QC-2 — AlignmentStats
// ─────────────────────────────────────────────────────────────────────────────
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

// ─────────────────────────────────────────────────────────────────────────────
// QC-3 — CoverageCalculator
// ─────────────────────────────────────────────────────────────────────────────
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

        var calc = new CoverageCalculator();
        var result = calc.Compute(reads, referenceLength: 10);

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

        var calc = new CoverageCalculator();
        var result = calc.Compute(reads, referenceLength: 10);

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

        var calc = new CoverageCalculator();
        var result = calc.Compute(reads, referenceLength: 10);

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

        var calc = new CoverageCalculator();
        var result = calc.Compute(reads, referenceLength: 10, targetIntervals: target);

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

        var calc = new CoverageCalculator();
        var result = calc.Compute(reads, referenceLength: 5);

        Assert.Equal(0.0, result.CoefficientOfVariation, 2);
    }

    [Fact]
    public void CoverageCalculator_MatchesSamtoolsDepth_WithinPlusMinusOne()
    {
        // Known BAM: read at pos 1 (1-based) covering 4 bases
        var read = MakeAlignment("r1", 1, 4); // positions 1,2,3,4

        var calc = new CoverageCalculator();
        var result = calc.Compute([read], referenceLength: 10);

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
        var calc = new CoverageCalculator();
        var result = calc.Compute(reads, referenceLength: 5);

        var json = JsonSerializer.Serialize(result);
        Assert.Contains("MeanDepth", json);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// QC-4 — PanelQcReport
// ─────────────────────────────────────────────────────────────────────────────
public class PanelQcReportTests
{
    private static Sequence MakeSeq(string bases, string quals)
        => new Sequence("read", bases.AsMemory(), quals.AsMemory());

    private static AlignmentSection MakeAlignment(
        string qname,
        int position,
        int length,
        byte mapq = 60,
        AlignmentSection.AlignmentFlag flag = AlignmentSection.AlignmentFlag.None)
        => new AlignmentSection(
            qname: qname,
            flag: flag,
            rname: "chr1",
            position: position,
            mapq: mapq,
            cigar: [((uint)length, CigarOp.Match)],
            rnext: 0,
            pnext: 0,
            templatelength: 0,
            sequence: new string('A', length),
            quality: new string('I', length),
            tags: []);

    [Fact]
    public async Task PanelQcReport_AllMetricsPass_WhenDataIsGood()
    {
        // Good data: 10 reads, depth 10, Q40
        var fastqReads = Enumerable.Range(0, 10)
            .Select(_ => MakeSeq("ACGTACGTAC", "IIIIIIIIII"))
            .ToList();

        async IAsyncEnumerable<Sequence> FastqStream()
        {
            foreach (var r in fastqReads)
            {
                yield return r;
            }

            await Task.CompletedTask;
        }

        var bamReads = Enumerable.Range(0, 50)
            .Select(i => MakeAlignment($"r{i}", 0, 10, mapq: 60))
            .ToList();

        var thresholds = new PanelQcThresholds
        {
            MinMeanCoverage = 5.0,
            MinMappingRate = 0.5,
            MaxDuplicateRate = 0.5,
            MinQ30Fraction = 0.5
        };

        var report = await PanelQcReport.GenerateAsync(
            FastqStream(),
            bamReads,
            referenceLength: 10,
            thresholds: thresholds);

        Assert.Equal(QcStatus.Pass, report.MeanCoverageStatus);
        Assert.Equal(QcStatus.Pass, report.MappingRateStatus);
        Assert.Equal(QcStatus.Pass, report.DuplicateRateStatus);
        Assert.Equal(QcStatus.Pass, report.Q30FractionStatus);
    }

    [Fact]
    public async Task PanelQcReport_Fail_WhenCoverageBelowThreshold()
    {
        // Low coverage: 1 read, threshold 20x
        var fastqReads = Enumerable.Range(0, 1)
            .Select(_ => MakeSeq("ACGT", "IIII"))
            .ToList();

        async IAsyncEnumerable<Sequence> FastqStream()
        {
            foreach (var r in fastqReads)
            {
                yield return r;
            }

            await Task.CompletedTask;
        }

        var bamReads = new[] { MakeAlignment("r1", 0, 4) };

        var thresholds = new PanelQcThresholds { MinMeanCoverage = 20.0 };
        var report = await PanelQcReport.GenerateAsync(
            FastqStream(), bamReads, referenceLength: 100, thresholds: thresholds);

        Assert.Equal(QcStatus.Fail, report.MeanCoverageStatus);
    }

    [Fact]
    public async Task PanelQcReport_SerializesToJson()
    {
        var fastqReads = Enumerable.Range(0, 5)
            .Select(_ => MakeSeq("ACGT", "IIII"))
            .ToList();

        async IAsyncEnumerable<Sequence> FastqStream()
        {
            foreach (var r in fastqReads)
            {
                yield return r;
            }

            await Task.CompletedTask;
        }

        var bamReads = Enumerable.Range(0, 5)
            .Select(i => MakeAlignment($"r{i}", 0, 4))
            .ToList();

        var report = await PanelQcReport.GenerateAsync(
            FastqStream(), bamReads, referenceLength: 4,
            thresholds: new PanelQcThresholds());

        var json = JsonSerializer.Serialize(report);
        Assert.Contains("MeanCoverageStatus", json);
        Assert.Contains("MappingRateStatus", json);
    }

    [Fact]
    public async Task PanelQcReport_GeneratesHtmlReport()
    {
        var fastqReads = Enumerable.Range(0, 5)
            .Select(_ => MakeSeq("ACGT", "IIII"))
            .ToList();

        async IAsyncEnumerable<Sequence> FastqStream()
        {
            foreach (var r in fastqReads)
            {
                yield return r;
            }

            await Task.CompletedTask;
        }

        var bamReads = Enumerable.Range(0, 5)
            .Select(i => MakeAlignment($"r{i}", 0, 4))
            .ToList();

        var report = await PanelQcReport.GenerateAsync(
            FastqStream(), bamReads, referenceLength: 4,
            thresholds: new PanelQcThresholds());

        var html = report.ToHtml();
        Assert.Contains("<html>", html);
        Assert.Contains("<table>", html);
        Assert.Contains("PASS", html.ToUpperInvariant());
    }
}
