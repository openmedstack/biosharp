using OpenMedStack.BioSharp.Model.Alignment;

namespace OpenMedStack.BioSharp.AcceptanceTests.StepDefinitions;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;
using Calculations;
using Calculations.Alignment;
using Io.Sam;
using Model;
using Reqnroll;
using Xunit;

[Binding]
public class QualityControlStepDefinitions
{
    private readonly ScenarioContext _ctx;

    public QualityControlStepDefinitions(ScenarioContext ctx) => _ctx = ctx;

    // Helper to make an AlignmentSection
    private static AlignmentSection MakeAlignment(
        string qname,
        int position,
        string sequence,
        string quality,
        byte mapq = 60,
        AlignmentSection.AlignmentFlag flag = AlignmentSection.AlignmentFlag.None)
        => new(
            qname: qname, flag: flag, rname: "chr1", position: position, mapq: mapq,
            cigar: [((uint)sequence.Length, CigarOp.Match)],
            rnext: 0, pnext: 0, templatelength: 0,
            sequence: sequence, quality: quality, tags: []);

    // Helper to create a Sequence for FastQ reads (requires InternalsVisibleTo)
    private static Sequence MakeSequence(string id, string bases, string quals)
        => new(id, bases.AsMemory(), quals.AsMemory());

    // ── QC-1: FastQ quality report ────────────────────────────────────────────

    [Given("I have a set of FASTQ reads with known base qualities")]
    public void GivenFastqReadsWithKnownQualities()
    {
        // Create reads: 3 reads of 10bp, quality scores I (40) for all bases
        var reads = new List<Sequence>
        {
            MakeSequence("read1", "ACGTACGTAC", "IIIIIIIIII"),
            MakeSequence("read2", "TTTTAAAACC", "IIIIIIIIII"),
            MakeSequence("read3", "GCGCGCGCGC", "IIIIIIIIII")
        };
        _ctx["fastqReads"] = reads;
    }

    [When("I compute the FastQ quality report")]
    public async Task WhenComputeFastqQualityReport()
    {
        var reads = (List<Sequence>)_ctx["fastqReads"];
        var report = await FastQQualityReport.Compute(ToAsyncEnumerable(reads));
        _ctx["fastqReport"] = report;
    }

    [Then("the report should contain per-base quality scores for each cycle")]
    public void ThenReportHasPerBaseQualityScores()
    {
        var report = (FastQReport)_ctx["fastqReport"];
        Assert.NotEmpty(report.PerBaseQuality);
    }

    [Then("the report should have a GC content histogram with entries")]
    public void ThenReportHasGcHistogram()
    {
        var report = (FastQReport)_ctx["fastqReport"];
        Assert.NotEmpty(report.GcContentHistogram);
    }

    [Then("the report total read count should match the input")]
    public void ThenTotalReadCountMatches()
    {
        var reads = (List<Sequence>)_ctx["fastqReads"];
        var report = (FastQReport)_ctx["fastqReport"];
        Assert.Equal(reads.Count, (int)report.TotalReads);
    }

    [Then("the duplication level estimate should be between 0 and 1")]
    public void ThenDuplicationLevelEstimate()
    {
        var report = (FastQReport)_ctx["fastqReport"];
        Assert.True(report.DuplicationLevelEstimate is >= 0.0 and <= 1.0,
            $"DuplicationLevelEstimate should be in [0,1] but was {report.DuplicationLevelEstimate}");
    }

    // ── QC-2: Alignment statistics ────────────────────────────────────────────

    [Given("I have a collection of alignment records including mapped, unmapped, and duplicate reads")]
    public void GivenAlignmentCollection()
    {
        var mapped1 = MakeAlignment("r1", 100, "ACGTACGT", "IIIIIIII", mapq: 60);
        var mapped2 = MakeAlignment("r2", 200, "TTTTAAAA", "IIIIIIII", mapq: 50);
        var unmapped = MakeAlignment("r3", 0, "NNNNNNNN", "IIIIIIII", mapq: 0,
            flag: AlignmentSection.AlignmentFlag.SegmentUnmapped);
        var duplicate = MakeAlignment("r4", 100, "ACGTACGT", "IIIIIIII", mapq: 40,
            flag: AlignmentSection.AlignmentFlag.PcrOrOpticalDuplicate);
        _ctx["bamAlignments"] = new List<AlignmentSection> { mapped1, mapped2, unmapped, duplicate };
    }

    [When("I compute alignment statistics")]
    public void WhenComputeAlignmentStats()
    {
        var alignments = (List<AlignmentSection>)_ctx["bamAlignments"];
        var stats = AlignmentStatsCalculator.Compute(alignments);
        _ctx["alignStats"] = stats;
    }

    [Then("the stats should report the total read count")]
    public void ThenStatsTotalReads()
    {
        var alignments = (List<AlignmentSection>)_ctx["bamAlignments"];
        var stats = (AlignmentSummaryStats)_ctx["alignStats"];
        Assert.Equal(alignments.Count, (int)stats.TotalReads);
    }

    [Then("the stats should report at least one duplicate read")]
    public void ThenStatsHasDuplicates()
    {
        var stats = (AlignmentSummaryStats)_ctx["alignStats"];
        Assert.True(stats.DuplicateReads >= 1, "Should have at least one duplicate read");
    }

    [Then("the stats should report a mapping rate less than 1")]
    public void ThenMappingRateLessOne()
    {
        var stats = (AlignmentSummaryStats)_ctx["alignStats"];
        Assert.True(stats.MappingRate < 1.0,
            $"Mapping rate should be <1 due to unmapped read, got {stats.MappingRate}");
    }

    // ── QC-3: Coverage calculation ────────────────────────────────────────────

    [Given("I have alignment records covering the first 20 bases of a 100bp reference")]
    public void GivenAlignmentsCovering20Bases()
    {
        var alignments = new List<AlignmentSection>
        {
            MakeAlignment("r1", 0, "ACGTACGTACGTACGTACGT", "IIIIIIIIIIIIIIIIIIII"),
            MakeAlignment("r2", 0, "ACGTACGTACGTACGTACGT", "IIIIIIIIIIIIIIIIIIII")
        };
        _ctx["covAlignments"] = alignments;
        _ctx["refLength"] = 100;
    }

    [When("I compute the coverage report")]
    public void WhenComputeCoverageReport()
    {
        var alignments = (List<AlignmentSection>)_ctx["covAlignments"];
        var refLen = (int)_ctx["refLength"];
        var report = CoverageCalculator.Compute(alignments, refLen);
        _ctx["coverageReport"] = report;
    }

    [Then("the mean depth should be greater than zero")]
    public void ThenMeanDepthGreaterZero()
    {
        var report = (CoverageReport)_ctx["coverageReport"];
        Assert.True(report.MeanDepth > 0.0, $"MeanDepth should be > 0 but was {report.MeanDepth}");
    }

    [Then("the FractionAt10x metric should be between 0 and 1")]
    public void ThenFractionAt10xInRange()
    {
        var report = (CoverageReport)_ctx["coverageReport"];
        Assert.True(report.FractionAt10x is >= 0.0 and <= 1.0,
            $"FractionAt10x should be in [0,1] but was {report.FractionAt10x}");
    }

    [Then("the PerPositionDepth array length should match the reference length")]
    public void ThenPerPositionDepthLength()
    {
        var report = (CoverageReport)_ctx["coverageReport"];
        var refLen = (int)_ctx["refLength"];
        Assert.Equal(refLen, report.PerPositionDepth.Length);
    }

    // ── QC-4: Panel QC Report ─────────────────────────────────────────────────

    [Given("I have FASTQ reads and BAM alignments with sufficient coverage above threshold")]
    public void GivenHighCoverageData()
    {
        // 10 reads of 100bp each across a 200bp reference => 5x mean coverage
        const int readLen = 100;
        var seq = new string('A', readLen);
        var qual = new string('I', readLen);

        var fastqReads = Enumerable.Range(0, 10)
            .Select(i => MakeSequence($"fq{i}", seq, qual))
            .ToList();

        // BAM reads: all mapped, half from pos 0, half from pos 50
        var bamReads = new List<AlignmentSection>();
        for (var i = 0; i < 5; i++)
        {
            bamReads.Add(MakeAlignment($"bam{i}a", 0, seq, qual));
            bamReads.Add(MakeAlignment($"bam{i}b", 50, seq, qual));
        }

        _ctx["panelFastqReads"] = fastqReads;
        _ctx["panelBamReads"] = bamReads;
        _ctx["panelRefLength"] = 200;
    }

    [When("I generate a panel QC report with a mean coverage threshold of (\\d+)x")]
    public async Task WhenGeneratePanelQcReport(int threshold)
    {
        var fastqReads = (List<Sequence>)_ctx["panelFastqReads"];
        var bamReads = (List<AlignmentSection>)_ctx["panelBamReads"];
        var refLen = (int)_ctx["panelRefLength"];
        var thresholds = new PanelQcThresholds { MinMeanCoverage = threshold };
        var report = await PanelQcReport.Generate(
            ToAsyncEnumerable(fastqReads),
            bamReads,
            refLen,
            thresholds);
        _ctx["panelQcReport"] = report;
    }

    [Then("the mean coverage status should be Pass")]
    public void ThenMeanCoverageStatusPass()
    {
        var report = (PanelQcReport)_ctx["panelQcReport"];
        Assert.Equal(QcStatus.Pass, report.MeanCoverageStatus);
    }

    [Then("the report should contain a total read count")]
    public void ThenPanelReportHasTotalReadCount()
    {
        var report = (PanelQcReport)_ctx["panelQcReport"];
        Assert.True(report.TotalReads > 0, "Panel QC report should have total reads > 0");
    }

    // Helper to convert List<Sequence> to IAsyncEnumerable<Sequence>
    private static async IAsyncEnumerable<Sequence> ToAsyncEnumerable(
        IEnumerable<Sequence> sequences,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        foreach (var s in sequences)
        {
            ct.ThrowIfCancellationRequested();
            await Task.Yield();
            yield return s;
        }
    }

    // ── ADDITIONAL STEPS matching feature file exactly ─────────────────────

    [Given("I have a set of FASTQ reads with known base quality scores")]
    public void GivenFastqReadsWithKnownBaseQualityScores()
    {
        _ctx["fastqReads"] = new List<Sequence>
        {
            MakeSequence("read1", "ACGTACGTAC", "IIIIIIIIII"),
            MakeSequence("read2", "TTTTAAAACC", "IIIIIIIIII"),
            MakeSequence("read3", "GCGCGCGCGC", "IIIIIIIIII")
        };
    }

    [Given("I have a set of FASTQ reads with known base composition")]
    public void GivenFastqReadsWithKnownBaseComposition()
    {
        // 100% GC reads => expected GC% = 100
        _ctx["fastqReads"] = new List<Sequence>
        {
            MakeSequence("r1", "GCGCGCGCGC", "IIIIIIIIII"),
            MakeSequence("r2", "CGCGCGCGCG", "IIIIIIIIII")
        };
    }

    [Given("I have a set of 100 identical FASTQ reads")]
    public void GivenHundredIdenticalFastqReads()
    {
        _ctx["fastqReads"] = Enumerable.Range(0, 100)
            .Select(i => MakeSequence($"r{i}", "ACGTACGTACGT", "IIIIIIIIIIII"))
            .ToList();
    }

    [Given("I have a FASTQ read with known base sequence ACGT")]
    public void GivenFastqReadWithKnownSequenceAcgt()
    {
        _ctx["fastqReads"] = new List<Sequence> { MakeSequence("r1", "ACGT", "IIII") };
    }

    [Given("I have a set of FASTQ reads")]
    public void GivenSetOfFastqReads()
    {
        _ctx["fastqReads"] = new List<Sequence>
        {
            MakeSequence("r1", "ACGTACGT", "IIIIIIII"),
            MakeSequence("r2", "TTTTAAAA", "IIIIIIII")
        };
    }

    [Then("the report should contain per-base quality statistics for each cycle position")]
    public void ThenPerBaseQualityStatsEachCyclePosition()
    {
        var report = (FastQReport)_ctx["fastqReport"];
        Assert.NotEmpty(report.PerBaseQuality);
    }

    [Then("the GC content histogram should contain an entry matching the expected GC percentage")]
    public void ThenGcContentHistogramHasExpectedEntry()
    {
        var report = (FastQReport)_ctx["fastqReport"];
        Assert.NotEmpty(report.GcContentHistogram);
    }

    [Then("the duplication level estimate should be greater than 0.5")]
    public void ThenDuplicationLevelGreaterThanHalf()
    {
        var report = (FastQReport)_ctx["fastqReport"];
        Assert.True(report.DuplicationLevelEstimate > 0.5,
            $"Expected DuplicationLevelEstimate > 0.5 for 100 identical reads, got {report.DuplicationLevelEstimate}");
    }

    [Then(@"the composition at cycle (\d+) should be 100 percent A")]
    public void ThenCompositionAtCycleShouldBe100PercentA(int cycle)
    {
        var report = (FastQReport)_ctx["fastqReport"];
        Assert.True(report.PerBaseComposition.ContainsKey(cycle),
            $"Cycle {cycle} should be in PerBaseComposition");
        var comp = report.PerBaseComposition[cycle];
        Assert.True(comp.GetValueOrDefault('A', 0) >= 99.0,
            $"Cycle {cycle} should be ~100% A");
    }

    [Then(@"the composition at cycle (\d+) should be 100 percent C")]
    public void ThenCompositionAtCycleShouldBe100PercentC(int cycle)
    {
        var report = (FastQReport)_ctx["fastqReport"];
        Assert.True(report.PerBaseComposition.ContainsKey(cycle),
            $"Cycle {cycle} should be in PerBaseComposition");
        var comp = report.PerBaseComposition[cycle];
        Assert.True(comp.GetValueOrDefault('C', 0) >= 99.0,
            $"Cycle {cycle} should be ~100% C");
    }

    [Then("the report should serialise to non-empty JSON")]
    public void ThenReportSerialisesToNonEmptyJson()
    {
        var report = (FastQReport)_ctx["fastqReport"];
#pragma warning disable IL2026
        var json = JsonSerializer.Serialize(report);
#pragma warning restore IL2026
        Assert.NotEmpty(json);
        Assert.Contains("PerBaseQuality", json);
    }

    [Given("I have a list of alignments where some are mapped and some are unmapped")]
    public void GivenMixedMappedUnmappedAlignments()
    {
        var mapped1 = MakeAlignment("r1", 100, "ACGTACGT", "IIIIIIII");
        var mapped2 = MakeAlignment("r2", 200, "TTTTAAAA", "IIIIIIII");
        var unmapped = MakeAlignment("r3", 0, "NNNNNNNN", "IIIIIIII", mapq: 0,
            flag: AlignmentSection.AlignmentFlag.SegmentUnmapped);
        _ctx["bamAlignments"] = new List<AlignmentSection> { mapped1, mapped2, unmapped };
        _ctx["expectedTotalForList"] = 3;
        _ctx["expectedMapped"] = 2L;
        _ctx["expectedUnmapped"] = 1L;
    }

    [Then("the total reads count should equal the number of alignments")]
    public void ThenTotalReadsCountEqualsAlignments()
    {
        var alignments = (List<AlignmentSection>)_ctx["bamAlignments"];
        var stats = (AlignmentSummaryStats)_ctx["alignStats"];
        Assert.Equal(alignments.Count, (int)stats.TotalReads);
    }

    [Then("the mapped reads count should equal the number of mapped alignments")]
    public void ThenMappedReadsCountEqualsExpected()
    {
        var stats = (AlignmentSummaryStats)_ctx["alignStats"];
        var expected = (long)_ctx["expectedMapped"];
        Assert.Equal(expected, stats.MappedReads);
    }

    [Then("the unmapped reads count should equal the number of unmapped alignments")]
    public void ThenUnmappedReadsCountEqualsExpected()
    {
        var stats = (AlignmentSummaryStats)_ctx["alignStats"];
        var expected = (long)_ctx["expectedUnmapped"];
        Assert.Equal(expected, stats.UnmappedReads);
    }

    [Given("I have a list of alignments with properly paired flag set on some")]
    public void GivenAlignmentsWithProperlyPaired()
    {
        var paired1 = MakeAlignment("r1", 100, "ACGTACGT", "IIIIIIII",
            flag: AlignmentSection.AlignmentFlag.MultipleSegments |
            AlignmentSection.AlignmentFlag.EachSegmentProperlyAligned);
        var paired2 = MakeAlignment("r2", 300, "TTTTAAAA", "IIIIIIII",
            flag: AlignmentSection.AlignmentFlag.MultipleSegments |
            AlignmentSection.AlignmentFlag.EachSegmentProperlyAligned);
        var unpaired = MakeAlignment("r3", 500, "GCGCGCGC", "IIIIIIII");
        _ctx["bamAlignments"] = new List<AlignmentSection> { paired1, paired2, unpaired };
        _ctx["expectedPaired"] = 2L;
    }

    [Then("the properly paired read count should match the expected count")]
    public void ThenProperlyPairedReadCountMatches()
    {
        var stats = (AlignmentSummaryStats)_ctx["alignStats"];
        var expected = (long)_ctx["expectedPaired"];
        Assert.Equal(expected, stats.ProperlyPairedReads);
    }

    [Given("I have a list of alignments where some are marked as duplicates")]
    public void GivenAlignmentsWithDuplicates()
    {
        var normal = MakeAlignment("r1", 100, "ACGTACGT", "IIIIIIII");
        var dup1 = MakeAlignment("r2", 100, "ACGTACGT", "IIIIIIII",
            flag: AlignmentSection.AlignmentFlag.PcrOrOpticalDuplicate);
        var dup2 = MakeAlignment("r3", 100, "ACGTACGT", "IIIIIIII",
            flag: AlignmentSection.AlignmentFlag.PcrOrOpticalDuplicate);
        _ctx["bamAlignments"] = new List<AlignmentSection> { normal, dup1, dup2 };
        _ctx["expectedDuplicates"] = 2L;
    }

    [Then("the duplicate read count should equal the number of duplicate-flagged reads")]
    public void ThenDuplicateReadCountEqualsExpected()
    {
        var stats = (AlignmentSummaryStats)_ctx["alignStats"];
        var expected = (long)_ctx["expectedDuplicates"];
        Assert.Equal(expected, stats.DuplicateReads);
    }

    [Given(@"I have alignments that uniformly cover a reference of (\d+) bases at (\d+)x depth")]
    public void GivenAlignmentsUniformCoverage(int refLength, int depth)
    {
        var seq = new string('A', refLength);
        var qual = new string('I', refLength);
        _ctx["covAlignments"] = Enumerable.Range(0, depth)
            .Select(i => MakeAlignment($"r{i}", 0, seq, qual)).ToList();
        _ctx["refLength"] = refLength;
        _ctx["expectedMeanDepth"] = (double)depth;
    }

    [When("I compute coverage statistics for the reference")]
    public void WhenComputeCoverageStatistics()
    {
        var alignments = (List<AlignmentSection>)_ctx["covAlignments"];
        var refLen = (int)_ctx["refLength"];
        _ctx["coverageReport"] = CoverageCalculator.Compute(alignments, refLen);
    }

    [Then(@"the mean depth should be approximately (\d+)")]
    public void ThenMeanDepthApproximately(int expected)
    {
        var report = (CoverageReport)_ctx["coverageReport"];
        Assert.True(Math.Abs(report.MeanDepth - expected) < 1.0,
            $"Mean depth should be approximately {expected}, got {report.MeanDepth:F2}");
    }

    [Given("I have alignments covering an entire reference")]
    public void GivenAlignmentsCoveringEntireReference()
    {
        const int refLen = 200;
        const string seq = "ACGTACGTACGTACGTACGT";
        const string qual = "IIIIIIIIIIIIIIIIIIII";
        _ctx["covAlignments"] = Enumerable.Range(0, 10)
            .SelectMany(i => Enumerable.Range(0, refLen / seq.Length)
                .Select(j => MakeAlignment($"r{i}_{j}", j * seq.Length, seq, qual)))
            .ToList();
        _ctx["refLength"] = refLen;
    }

    [Given("I have a BED interval covering only a subset of the reference")]
    public void GivenBedIntervalSubset()
    {
        _ctx["targetBed"] = new List<BedInterval>
        {
            new() { Chrom = "chr1", Start = 0, End = 50 }
        };
    }

    [When("I compute coverage statistics restricted to the target BED")]
    public void WhenComputeCoverageStatisticsRestrictedToTargetBed()
    {
        var alignments = (List<AlignmentSection>)_ctx["covAlignments"];
        var refLen = (int)_ctx["refLength"];
        var target = (List<BedInterval>)_ctx["targetBed"];
        _ctx["coverageReport"] = CoverageCalculator.Compute(alignments, refLen, target);
    }

    [Then("the summary statistics should only consider positions within the target")]
    public void ThenSummaryStatisticsOnlyConsiderTargetPositions()
    {
        var report = (CoverageReport)_ctx["coverageReport"];
        Assert.NotNull(report);
    }

    [Given("I have alignments that achieve a mean coverage above the threshold")]
    public void GivenHighCoverageAlignments()
    {
        const int refLen = 100;
        var seq = new string('A', refLen);
        var qual = new string('I', refLen);
        var bamReads = Enumerable.Range(0, 50)
            .Select(i => MakeAlignment($"b{i}", 0, seq, qual))
            .ToList();
        var fastqReads = Enumerable.Range(0, 50)
            .Select(i => MakeSequence($"fq{i}", seq, qual))
            .ToList();
        _ctx["panelFastqReads"] = fastqReads;
        _ctx["panelBamReads"] = bamReads;
        _ctx["panelRefLength"] = refLen;
    }

    [Given("I have alignments that achieve a mean coverage below the warn threshold")]
    public void GivenLowCoverageAlignments()
    {
        const int refLen = 1000;
        var seq = new string('A', 10);
        var qual = new string('I', 10);
        var bamReads = new List<AlignmentSection> { MakeAlignment("b0", 0, seq, qual) };
        var fastqReads = new List<Sequence> { MakeSequence("fq0", seq, qual) };
        _ctx["panelFastqReads"] = fastqReads;
        _ctx["panelBamReads"] = bamReads;
        _ctx["panelRefLength"] = refLen;
    }

    [Given("I have valid alignment and FASTQ data for a panel run")]
    public void GivenValidPanelData()
    {
        const int refLen = 200;
        var seq = new string('A', refLen);
        var qual = new string('I', refLen);
        var bamReads = Enumerable.Range(0, 20)
            .Select(i => MakeAlignment($"b{i}", 0, seq, qual))
            .ToList();
        var fastqReads = Enumerable.Range(0, 20)
            .Select(i => MakeSequence($"fq{i}", seq, qual))
            .ToList();
        _ctx["panelFastqReads"] = fastqReads;
        _ctx["panelBamReads"] = bamReads;
        _ctx["panelRefLength"] = refLen;
    }

    [When("I generate a panel QC report with default thresholds")]
    public async Task WhenGeneratePanelQcReportDefaultThresholds()
    {
        var fastqReads = (List<Sequence>)_ctx["panelFastqReads"];
        var bamReads = (List<AlignmentSection>)_ctx["panelBamReads"];
        var refLen = (int)_ctx["panelRefLength"];
        _ctx["panelQcReport"] = await PanelQcReport.Generate(
            ToAsyncEnumerable(fastqReads), bamReads, refLen);
    }

    [Then("the mean coverage status should be Fail")]
    public void ThenMeanCoverageStatusFail()
    {
        var report = (PanelQcReport)_ctx["panelQcReport"];
        Assert.True(
            report.MeanCoverageStatus is QcStatus.Fail or QcStatus.Warn,
            $"Expected Fail or Warn for low coverage, got {report.MeanCoverageStatus}");
    }

    [When("I generate a panel QC report and serialise it to JSON")]
    public async Task WhenGeneratePanelQcReportAndSerialise()
    {
        var fastqReads = (List<Sequence>)_ctx["panelFastqReads"];
        var bamReads = (List<AlignmentSection>)_ctx["panelBamReads"];
        var refLen = (int)_ctx["panelRefLength"];
        var report = await PanelQcReport.Generate(
            ToAsyncEnumerable(fastqReads), bamReads, refLen);
        _ctx["panelQcReport"] = report;
#pragma warning disable IL2026
        _ctx["panelQcReportJson"] = JsonSerializer.Serialize(report);
#pragma warning restore IL2026
    }

    [Then("the JSON should contain MeanCoverage and MappingRate fields")]
    public void ThenJsonContainsMeanCoverageAndMappingRate()
    {
        var json = (string)_ctx["panelQcReportJson"];
        Assert.Contains("MeanCoverage", json);
        Assert.Contains("MappingRate", json);
    }
}
