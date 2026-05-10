namespace OpenMedStack.BioSharp.Calculations.Tests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Alignment;
using Io.Sam;
using Model;
using Xunit;

/// <summary>
/// Tests covering ALN-1 through ALN-5: Duplicate Marking, MAPQ Calculation,
/// Base Quality Score Recalibration, Adapter Trimming, and Insert Size Estimation.
/// </summary>
public class AlignmentReadProcessingTests
{
    // =========================================================================
    // Helper: Create a minimal AlignmentSection for testing
    // =========================================================================
    private static AlignmentSection MakeAlignment(
        string readName,
        int position,
        string sequence,
        string quality,
        byte mapq = 60,
        AlignmentSection.AlignmentFlag flag = AlignmentSection.AlignmentFlag.None,
        int rnext = 0,
        int pnext = 0,
        int tlen = 0,
        (uint, CigarOp)[]? cigar = null,
        IEnumerable<AlignmentTag>? tags = null) =>
        new AlignmentSection(
            qname: readName,
            flag: flag,
            rname: "chr1",
            position: position,
            mapq: mapq,
            cigar: cigar ?? new (uint, CigarOp)[] { ((uint)sequence.Length, CigarOp.Match) },
            rnext: rnext,
            pnext: pnext,
            templatelength: tlen,
            sequence: sequence,
            quality: quality,
            tags: tags ?? []);

    // =========================================================================
    // ALN-1: Duplicate Marking
    // =========================================================================

    [Fact]
    public void DuplicateMarker_NoDuplicates_ReturnsUnchanged()
    {
        var a1 = MakeAlignment("r1", 100, "ACGT", "IIII", mapq: 60);
        var a2 = MakeAlignment("r2", 200, "ACGT", "IIII", mapq: 60);

        var (marked, metrics) = DuplicateMarker.MarkDuplicates([a1, a2]);

        Assert.Equal(2, marked.Count);
        Assert.All(marked, r => Assert.False(r.Flag.HasFlag(AlignmentSection.AlignmentFlag.PcrOrOpticalDuplicate)));
        Assert.Equal(0, metrics.DuplicateReads);
        Assert.Equal(0, metrics.OpticalDuplicateReads);
        Assert.Equal(2, metrics.TotalReads);
        Assert.Equal(0.0, metrics.DuplicateRate);
    }

    [Fact]
    public void DuplicateMarker_TwoReadsAtSamePosition_MarksLowerQualityAsDuplicate()
    {
        // Two reads starting at the same position, same strand, same CIGAR
        var a1 = MakeAlignment("r1", 100, "ACGTACGT", "IIIIIIII", mapq: 60);
        var a2 = MakeAlignment("r2", 100, "ACGTACGT", "55555555", mapq: 30);

        var (marked, metrics) = DuplicateMarker.MarkDuplicates([a1, a2]);

        Assert.Equal(2, marked.Count);
        // a1 has higher MAPQ so it stays primary
        var primary = marked.Single(r => !r.Flag.HasFlag(AlignmentSection.AlignmentFlag.PcrOrOpticalDuplicate));
        var dup = marked.Single(r => r.Flag.HasFlag(AlignmentSection.AlignmentFlag.PcrOrOpticalDuplicate));
        Assert.Equal("r1", primary.QName);
        Assert.Equal("r2", dup.QName);
        Assert.Equal(1, metrics.DuplicateReads);
        Assert.Equal(2, metrics.TotalReads);
        Assert.InRange(metrics.DuplicateRate, 0.49, 0.51);
    }

    [Fact]
    public void DuplicateMarker_ThreeReadsAtSamePosition_MarksLowestTwoAsDuplicates()
    {
        var a1 = MakeAlignment("r1", 100, "ACGTACGT", "IIIIIIII", mapq: 60);
        var a2 = MakeAlignment("r2", 100, "ACGTACGT", "55555555", mapq: 30);
        var a3 = MakeAlignment("r3", 100, "ACGTACGT", "22222222", mapq: 10);

        var (marked, metrics) = DuplicateMarker.MarkDuplicates([a1, a2, a3]);

        Assert.Equal(3, marked.Count);
        var primaries = marked.Where(r => !r.Flag.HasFlag(AlignmentSection.AlignmentFlag.PcrOrOpticalDuplicate)).ToList();
        var dups = marked.Where(r => r.Flag.HasFlag(AlignmentSection.AlignmentFlag.PcrOrOpticalDuplicate)).ToList();
        Assert.Single(primaries);
        Assert.Equal(2, dups.Count);
        Assert.Equal("r1", primaries[0].QName);
        Assert.Equal(2, metrics.DuplicateReads);
    }

    [Fact]
    public void DuplicateMarker_ReverseStrand_TreatedSeparatelyFromForward()
    {
        var forward = MakeAlignment("r1", 100, "ACGT", "IIII",
            flag: AlignmentSection.AlignmentFlag.None);
        var reverse = MakeAlignment("r2", 100, "ACGT", "IIII",
            flag: AlignmentSection.AlignmentFlag.SeqBeingReverseComplemented);

        var (marked, metrics) = DuplicateMarker.MarkDuplicates([forward, reverse]);

        // Different orientation → not duplicates
        Assert.All(marked, r => Assert.False(r.Flag.HasFlag(AlignmentSection.AlignmentFlag.PcrOrOpticalDuplicate)));
        Assert.Equal(0, metrics.DuplicateReads);
    }

    [Fact]
    public void DuplicateMarker_DifferentCigar_TreatedSeparately()
    {
        var a1 = MakeAlignment("r1", 100, "ACGT", "IIII",
            cigar: [(4u, CigarOp.Match)]);
        var a2 = MakeAlignment("r2", 100, "ACGT", "IIII",
            cigar: [(2u, CigarOp.Match), (2u, CigarOp.Deletion), (2u, CigarOp.Match)]);

        var (marked, metrics) = DuplicateMarker.MarkDuplicates([a1, a2]);

        Assert.All(marked, r => Assert.False(r.Flag.HasFlag(AlignmentSection.AlignmentFlag.PcrOrOpticalDuplicate)));
        Assert.Equal(0, metrics.DuplicateReads);
    }

    [Fact]
    public void DuplicateMarker_PairedEndReads_MarksCorrectly()
    {
        // Proper paired reads — pair (r1+r2) at same position as pair (r3+r4)
        var flag1 = AlignmentSection.AlignmentFlag.MultipleSegments |
                    AlignmentSection.AlignmentFlag.EachSegmentProperlyAligned |
                    AlignmentSection.AlignmentFlag.FirstSegmentInTemplate;
        var flag2 = AlignmentSection.AlignmentFlag.MultipleSegments |
                    AlignmentSection.AlignmentFlag.EachSegmentProperlyAligned |
                    AlignmentSection.AlignmentFlag.LastSegmentInTemplate;

        var r1 = MakeAlignment("pair1", 100, "ACGTACGT", "IIIIIIII", mapq: 60, flag: flag1, pnext: 300, tlen: 200);
        var r2 = MakeAlignment("pair1", 300, "ACGTACGT", "IIIIIIII", mapq: 60, flag: flag2, pnext: 100, tlen: -200);
        var r3 = MakeAlignment("pair2", 100, "ACGTACGT", "55555555", mapq: 30, flag: flag1, pnext: 300, tlen: 200);
        var r4 = MakeAlignment("pair2", 300, "ACGTACGT", "55555555", mapq: 30, flag: flag2, pnext: 100, tlen: -200);

        var (marked, metrics) = DuplicateMarker.MarkDuplicates([r1, r2, r3, r4]);

        var dups = marked.Where(r => r.Flag.HasFlag(AlignmentSection.AlignmentFlag.PcrOrOpticalDuplicate)).ToList();
        Assert.Equal(2, dups.Count);
        Assert.All(dups, d => Assert.Equal("pair2", d.QName));
        Assert.Equal(2, metrics.DuplicateReads);
    }

    [Fact]
    public void DuplicateMarker_OpticalDuplicates_MarkedWithinPixelDistance()
    {
        // Illumina read names: @Instrument:RunID:FlowcellID:Lane:Tile:X:Y
        var a1 = MakeAlignment("Instr:1:FC:1:1101:1000:1000", 100, "ACGT", "IIII", mapq: 60);
        var a2 = MakeAlignment("Instr:1:FC:1:1101:1050:1050", 100, "ACGT", "5555", mapq: 30);

        var (marked, metrics) = DuplicateMarker.MarkDuplicates([a1, a2], opticalDuplicatePixelDistance: 100);

        Assert.Equal(1, metrics.OpticalDuplicateReads);
        Assert.Equal(1, metrics.DuplicateReads);
    }

    [Fact]
    public void DuplicateMarker_OpticalDuplicates_NotMarkedOutsidePixelDistance()
    {
        var a1 = MakeAlignment("Instr:1:FC:1:1101:1000:1000", 100, "ACGT", "IIII", mapq: 60);
        var a2 = MakeAlignment("Instr:1:FC:1:1101:5000:5000", 100, "ACGT", "5555", mapq: 30);

        var (marked, metrics) = DuplicateMarker.MarkDuplicates([a1, a2], opticalDuplicatePixelDistance: 100);

        // Still a PCR duplicate (same position) but NOT optical
        Assert.Equal(1, metrics.DuplicateReads);
        Assert.Equal(0, metrics.OpticalDuplicateReads);
    }

    [Fact]
    public void DuplicateMarker_ReportsCorrectMetrics()
    {
        var reads = Enumerable.Range(0, 10)
            .Select(i => MakeAlignment($"r{i}", 100, "ACGTACGT", new string((char)(40 + i), 8), mapq: (byte)(60 - i)))
            .ToList();

        var (marked, metrics) = DuplicateMarker.MarkDuplicates(reads);

        Assert.Equal(10, metrics.TotalReads);
        Assert.Equal(9, metrics.DuplicateReads);
        Assert.InRange(metrics.DuplicateRate, 0.89, 0.91);
    }

    // =========================================================================
    // ALN-2: MAPQ Calculation
    // =========================================================================

    [Fact]
    public void MapqCalculator_UniqueAlignment_Returns60()
    {
        var mapq = MapqCalculator.Calculate(bestScore: 100, secondBestScore: 0, readLength: 50, matchScore: 2);
        Assert.Equal(60, mapq);
    }

    [Fact]
    public void MapqCalculator_EqualAlignments_Returns0()
    {
        var mapq = MapqCalculator.Calculate(bestScore: 100, secondBestScore: 100, readLength: 50, matchScore: 2);
        Assert.Equal(0, mapq);
    }

    [Fact]
    public void MapqCalculator_BestBetterThanSecond_ReturnsIntermediateValue()
    {
        var mapq = MapqCalculator.Calculate(bestScore: 100, secondBestScore: 50, readLength: 50, matchScore: 2);
        Assert.InRange(mapq, 1, 59);
    }

    [Fact]
    public void MapqCalculator_SlightlyBetter_ReturnsLowMapq()
    {
        var mapq = MapqCalculator.Calculate(bestScore: 100, secondBestScore: 98, readLength: 50, matchScore: 2);
        Assert.InRange(mapq, 0, 10);
    }

    [Fact]
    public void MapqCalculator_LargeGap_ReturnsHighMapq()
    {
        var mapq = MapqCalculator.Calculate(bestScore: 100, secondBestScore: 10, readLength: 50, matchScore: 2);
        Assert.InRange(mapq, 50, 60);
    }

    [Fact]
    public void MapqCalculator_NoSecondBest_Returns60()
    {
        // No second-best alignment (secondBestScore = -1 sentinel)
        var mapq = MapqCalculator.Calculate(bestScore: 80, secondBestScore: -1, readLength: 40, matchScore: 2);
        Assert.Equal(60, mapq);
    }

    [Fact]
    public void MapqCalculator_CorrelatesWithBwaStyle()
    {
        // BWA-MEM style: MAPQ = 60 * (1 - secondBest/best), capped at 60
        // For best=100, second=60: fraction remaining = 0.4 → MAPQ ≈ 24 (≥1)
        var mapq = MapqCalculator.Calculate(bestScore: 100, secondBestScore: 60, readLength: 50, matchScore: 2);
        Assert.InRange(mapq, 1, 59);
    }

    // =========================================================================
    // ALN-3: Base Quality Score Recalibration (BQSR)
    // =========================================================================

    private static AlignmentSection MakeAlignmentWithQuality(string readName, int position, string sequence,
        string quality) =>
        MakeAlignment(readName, position, sequence, quality);

    [Fact]
    public void Bqsr_CollectCovariates_ProducesNonEmptyTable()
    {
        // Create alignments where some bases match reference, some don't
        var refSeq = new Sequence("chr1", "ACGTACGTACGTACGTACGT".ToCharArray(),
            new string('I', 20).ToCharArray());

        var alignments = new[]
        {
            MakeAlignmentWithQuality("r1", 1, "ACGTACGTACGT", new string('I', 12)),
            MakeAlignmentWithQuality("r2", 1, "ACGTXCGTACGT", new string('I', 12)), // mismatch at position 5
            MakeAlignmentWithQuality("r3", 5, "ACGTACGT", new string('I', 8))
        };

        var recalibrator = new BaseQualityRecalibrator();
        var table = recalibrator.CollectCovariates(alignments, refSeq);

        Assert.NotNull(table);
        Assert.NotEmpty(table.Entries);
    }

    [Fact]
    public void Bqsr_ApplyRecalibration_ModifiesQualitiesAtHighErrorSites()
    {
        // Build a reference
        var refChars = new string('A', 50).ToCharArray(); // all As
        var refSeq = new Sequence("chr1", refChars, new string('I', 50).ToCharArray());

        // Create reads where position 5 always has an error (empirical error rate = 1.0)
        // Reference quality claimed = 40 ('I'), but all mismatch → should be recalibrated down
        var alignments = Enumerable.Range(0, 20).Select(i =>
            MakeAlignmentWithQuality($"r{i}", 1,
                "AAAATAAAAAAAAAAAAAAAA", // T at position 5 (1-based ref position 5)
                new string('I', 21))).ToArray();

        var recalibrator = new BaseQualityRecalibrator();
        var table = recalibrator.CollectCovariates(alignments, refSeq);

        // Apply recalibration
        var original = MakeAlignmentWithQuality("test", 1, "AAAATAAAAAAAAAAAAAAAA", new string('I', 21));
        var recalibrated = recalibrator.ApplyRecalibration([original], table);

        Assert.Single(recalibrated);
        var recalQuality = recalibrated[0].Quality;
        // The mismatch position (index 4) should have quality lowered from 'I' (40)
        var originalQ = original.Quality[4] - 33;
        var recalQ = recalQuality[4] - 33;
        Assert.True(recalQ < originalQ,
            $"Expected quality at mismatch position to decrease. Original={originalQ}, Recalibrated={recalQ}");
    }

    [Fact]
    public void Bqsr_ApplyRecalibration_PerfectMatchSitesPreserveOrImproveQuality()
    {
        var refChars = new string('A', 30).ToCharArray();
        var refSeq = new Sequence("chr1", refChars, new string('I', 30).ToCharArray());

        // All reads perfectly match reference
        var alignments = Enumerable.Range(0, 20).Select(i =>
            MakeAlignmentWithQuality($"r{i}", 1,
                new string('A', 20),
                new string('I', 20))).ToArray();

        var recalibrator = new BaseQualityRecalibrator();
        var table = recalibrator.CollectCovariates(alignments, refSeq);

        var original = MakeAlignmentWithQuality("test", 1, new string('A', 20), new string('I', 20));
        var recalibrated = recalibrator.ApplyRecalibration([original], table);

        // Qualities should not increase beyond the original (capped at max Phred)
        for (var i = 0; i < recalibrated[0].Quality.Length; i++)
        {
            var q = recalibrated[0].Quality[i] - 33;
            Assert.InRange(q, 0, 40);
        }
    }

    [Fact]
    public void Bqsr_RecalibrationTable_IsSerializableToJson()
    {
        var refSeq = new Sequence("chr1", "ACGTACGT".ToCharArray(), new string('I', 8).ToCharArray());
        var alignments = new[]
        {
            MakeAlignmentWithQuality("r1", 1, "ACGTACGT", new string('I', 8))
        };

        var recalibrator = new BaseQualityRecalibrator();
        var table = recalibrator.CollectCovariates(alignments, refSeq);

#pragma warning disable IL2026
        var json = JsonSerializer.Serialize(table);
        Assert.NotEmpty(json);
        var roundTripped = JsonSerializer.Deserialize<RecalibrationTable>(json);
#pragma warning restore IL2026
        Assert.NotNull(roundTripped);
    }

    [Fact]
    public void Bqsr_CycleBias_CorrectedIndependently()
    {
        // First cycles have artificially high error rates
        var refChars = new string('A', 30).ToCharArray();
        var refSeq = new Sequence("chr1", refChars, new string('I', 30).ToCharArray());

        // Reads where first 3 bases always mismatch (cycle bias)
        var alignments = Enumerable.Range(0, 30).Select(i =>
            MakeAlignmentWithQuality($"r{i}", 1,
                "TTTAAAAAAAAAAAAAA", // TTT at cycles 1-3
                new string('I', 17))).ToArray();

        var recalibrator = new BaseQualityRecalibrator();
        var table = recalibrator.CollectCovariates(alignments, refSeq);
        var original = MakeAlignmentWithQuality("test", 1, "TTTAAAAAAAAAAAAAA", new string('I', 17));
        var recalibrated = recalibrator.ApplyRecalibration([original], table);

        // Early cycle positions should be downgraded
        for (var i = 0; i < 3; i++)
        {
            var origQ = original.Quality[i] - 33;
            var recalQ = recalibrated[0].Quality[i] - 33;
            Assert.True(recalQ < origQ, $"Cycle {i}: expected quality decrease from {origQ}, got {recalQ}");
        }
    }

    // =========================================================================
    // ALN-4: Adapter Trimming
    // =========================================================================

    [Fact]
    public void AdapterTrimmer_NoAdapter_ReturnUnchanged()
    {
        var seq = new Sequence("r1", "ACGTACGTACGT".ToCharArray(), new string('I', 12).ToCharArray());
        var trimmer = new AdapterTrimmer(AdapterTrimmer.Presets.TruSeqR1, maxMismatches: 2);

        var (trimmed, stats) = trimmer.Trim(seq);

        Assert.Equal(seq.Length, trimmed!.Length);
        Assert.Equal("ACGTACGTACGT", new string(trimmed.GetData().Span));
        Assert.Equal(0, stats.ReadsTrimmed);
    }

    [Fact]
    public void AdapterTrimmer_FullLengthAdapter_TrimsToEmpty()
    {
        // Sequence is pure adapter
        var adapter = AdapterTrimmer.Presets.TruSeqR1;
        var seq = new Sequence("r1", adapter.ToCharArray(), new string('I', adapter.Length).ToCharArray());
        var trimmer = new AdapterTrimmer(adapter, maxMismatches: 0, minLength: 1);

        var (trimmed, stats) = trimmer.Trim(seq);

        Assert.Null(trimmed); // shorter than minLength after trim
        Assert.Equal(1, stats.ReadsDiscarded);
    }

    [Fact]
    public void AdapterTrimmer_PartialAdapter_TrimsCorrectly()
    {
        // 20bp insert + first 15bp of adapter
        var adapter = AdapterTrimmer.Presets.TruSeqR1;
        var insert = "ACGTACGTACGTACGTACGT"; // 20 bp
        var partialAdapter = adapter[..15];
        var readSeq = insert + partialAdapter;
        var seq = new Sequence("r1", readSeq.ToCharArray(), new string('I', readSeq.Length).ToCharArray());
        var trimmer = new AdapterTrimmer(adapter, maxMismatches: 0, minLength: 1);

        var (trimmed, stats) = trimmer.Trim(seq);

        Assert.NotNull(trimmed);
        Assert.Equal(20, trimmed.Length);
        Assert.Equal(insert, new string(trimmed.GetData().Span));
        Assert.Equal(1, stats.ReadsTrimmed);
        Assert.Equal(15, stats.BasesRemoved);
    }

    [Fact]
    public void AdapterTrimmer_AdapterWithMismatches_TrimsCorrectly()
    {
        var adapter = AdapterTrimmer.Presets.NexTera;
        var insert = "GCTAGCTAGCTAGCTA"; // 16 bp
        // Adapter with 2 mismatches (first and last base changed)
        var mutatedAdapter = "X" + adapter[1..^1] + "X";
        var readSeq = insert + mutatedAdapter;
        var seq = new Sequence("r1", readSeq.ToCharArray(), new string('I', readSeq.Length).ToCharArray());
        var trimmer = new AdapterTrimmer(adapter, maxMismatches: 2, minLength: 1);

        var (trimmed, stats) = trimmer.Trim(seq);

        Assert.NotNull(trimmed);
        Assert.Equal(insert.Length, trimmed.Length);
        Assert.Equal(1, stats.ReadsTrimmed);
    }

    [Fact]
    public void AdapterTrimmer_TrimmedReadBelowMinLength_Discards()
    {
        var adapter = AdapterTrimmer.Presets.TruSeqR1;
        // Only 5bp insert before adapter — below default minLength of 20
        var insert = "ACGTA";
        var readSeq = insert + adapter;
        var seq = new Sequence("r1", readSeq.ToCharArray(), new string('I', readSeq.Length).ToCharArray());
        var trimmer = new AdapterTrimmer(adapter, maxMismatches: 0, minLength: 20);

        var (trimmed, stats) = trimmer.Trim(seq);

        Assert.Null(trimmed);
        Assert.Equal(1, stats.ReadsDiscarded);
    }

    [Fact]
    public void AdapterTrimmer_TrimStats_Accumulated()
    {
        var adapter = AdapterTrimmer.Presets.TruSeqR1;
        var insert = "ACGTACGTACGTACGTACGTACGT"; // 24 bp (above minLength=20)
        var reads = new[]
        {
            new Sequence("r1", (insert + adapter).ToCharArray(),
                new string('I', insert.Length + adapter.Length).ToCharArray()),
            new Sequence("r2", "ACGTACGT".ToCharArray(), new string('I', 8).ToCharArray()),    // no adapter
            new Sequence("r3", ("ACGTA" + adapter).ToCharArray(),
                new string('I', 5 + adapter.Length).ToCharArray()), // below min length after trim
        };
        var trimmer = new AdapterTrimmer(adapter, maxMismatches: 0, minLength: 20);

        var results = trimmer.TrimAll(reads);

        Assert.Equal(1, results.Stats.ReadsTrimmed);
        Assert.Equal(adapter.Length, results.Stats.BasesRemoved);
        Assert.Equal(1, results.Stats.ReadsDiscarded);
    }

    [Fact]
    public void AdapterTrimmer_NexTeraPreset_CorrectSequence()
    {
        Assert.Equal("CTGTCTCTTATACACATCT", AdapterTrimmer.Presets.NexTera);
    }

    [Fact]
    public void AdapterTrimmer_TruSeqR1Preset_ContainsExpectedPrefix()
    {
        // TruSeq Read 1 adapter starts with AGATCGGAAGAGC
        Assert.StartsWith("AGATCGGAAGAGC", AdapterTrimmer.Presets.TruSeqR1, StringComparison.Ordinal);
    }

    // =========================================================================
    // ALN-5: Insert Size Estimation
    // =========================================================================

    private static AlignmentSection MakePairedAlignment(
        string qname,
        int pos,
        int pnext,
        int tlen,
        AlignmentSection.AlignmentFlag extraFlags = AlignmentSection.AlignmentFlag.None)
    {
        var baseFlags = AlignmentSection.AlignmentFlag.MultipleSegments |
                        AlignmentSection.AlignmentFlag.EachSegmentProperlyAligned |
                        extraFlags;
        return MakeAlignment(qname, pos, "ACGTACGT", "IIIIIIII", mapq: 60,
            flag: baseFlags, pnext: pnext, tlen: tlen);
    }

    [Fact]
    public void InsertSizeEstimator_KnownDistribution_ComputesCorrectStats()
    {
        // 100 pairs with known insert sizes: 200, 210, 220, ..., 200+99*10 = 1190
        // mean ≈ 695, median ≈ 695
        var alignments = Enumerable.Range(0, 100)
            .Select(i => MakePairedAlignment($"pair{i}", 100, 100 + (200 + i * 10),
                200 + i * 10))
            .ToList();

        var estimator = new InsertSizeEstimator();
        var report = estimator.Estimate(alignments);

        Assert.True(report.Mean > 0);
        Assert.True(report.Median > 0);
        Assert.True(report.StandardDeviation > 0);
        // mean should be ~ 695
        Assert.InRange(report.Mean, 600, 800);
        Assert.InRange(report.Median, 600, 800);
        Assert.Equal(0, report.DiscordantPairs);
    }

    [Fact]
    public void InsertSizeEstimator_Percentiles_OrderedCorrectly()
    {
        var alignments = Enumerable.Range(0, 200)
            .Select(i => MakePairedAlignment($"pair{i}", 1, 1 + (100 + i),
                100 + i))
            .ToList();

        var estimator = new InsertSizeEstimator();
        var report = estimator.Estimate(alignments);

        Assert.True(report.Percentile10 <= report.Median);
        Assert.True(report.Median <= report.Percentile90);
        Assert.True(report.Percentile10 < report.Percentile90);
    }

    [Fact]
    public void InsertSizeEstimator_DiscordantPairs_Counted()
    {
        // Proper pairs (positive TLEN) + discordant pairs (TLEN = 0 = unmapped mate)
        var proper = Enumerable.Range(0, 10)
            .Select(i => MakePairedAlignment($"proper{i}", 100, 300, 200))
            .ToList();

        // Discordant: mate on different chromosome (rnext != 0, tlen = 0)
        var discordant = Enumerable.Range(0, 3)
            .Select(i => new AlignmentSection(
                qname: $"discord{i}",
                flag: AlignmentSection.AlignmentFlag.MultipleSegments,
                rname: "chr1",
                position: 100,
                mapq: 60,
                cigar: [(8u, CigarOp.Match)],
                rnext: 2, // different reference
                pnext: 100,
                templatelength: 0,
                sequence: "ACGTACGT",
                quality: "IIIIIIII",
                tags: []))
            .ToList();

        var all = proper.Concat(discordant).ToList();
        var estimator = new InsertSizeEstimator();
        var report = estimator.Estimate(all);

        Assert.Equal(3, report.DiscordantPairs);
        Assert.Equal(10, report.ProperlyPairedReads);
    }

    [Fact]
    public void InsertSizeEstimator_Histogram_CoversSizes()
    {
        var alignments = Enumerable.Range(0, 50)
            .Select(i => MakePairedAlignment($"pair{i}", 1, 1 + 150 + i * 2, 150 + i * 2))
            .ToList();

        var estimator = new InsertSizeEstimator(binSize: 10);
        var report = estimator.Estimate(alignments);

        Assert.NotEmpty(report.Histogram);
        Assert.True(report.Histogram.Values.Sum() == 50);
    }

    [Fact]
    public void InsertSizeEstimator_SerializesToJson()
    {
        var alignments = Enumerable.Range(0, 20)
            .Select(i => MakePairedAlignment($"pair{i}", 1, 1 + 200 + i, 200 + i))
            .ToList();

        var estimator = new InsertSizeEstimator();
        var report = estimator.Estimate(alignments);

#pragma warning disable IL2026
        var json = JsonSerializer.Serialize(report);
#pragma warning restore IL2026
        Assert.NotEmpty(json);
        Assert.Contains("Mean", json);
        Assert.Contains("Median", json);
    }

    [Fact]
    public void InsertSizeEstimator_EmptyInput_ReturnsZeroStats()
    {
        var estimator = new InsertSizeEstimator();
        var report = estimator.Estimate([]);

        Assert.Equal(0, report.Mean);
        Assert.Equal(0, report.Median);
        Assert.Equal(0, report.StandardDeviation);
        Assert.Equal(0, report.ProperlyPairedReads);
    }

    [Fact]
    public void InsertSizeEstimator_NegativeTlen_UsedAsAbsoluteValue()
    {
        // When read2 is listed, TLEN is negative; absolute value is the insert size
        var alignments = new[]
        {
            MakePairedAlignment("pair1", 300, 100, -200,
                AlignmentSection.AlignmentFlag.LastSegmentInTemplate)
        };

        var estimator = new InsertSizeEstimator();
        var report = estimator.Estimate(alignments);

        Assert.Equal(1, report.ProperlyPairedReads);
        Assert.Equal(200, report.Mean);
    }
}
