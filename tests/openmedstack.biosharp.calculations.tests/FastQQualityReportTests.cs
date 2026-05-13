namespace OpenMedStack.BioSharp.Calculations.Tests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
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
        var report = await FastQQualityReport.Compute(Seqs(s));

        Assert.Equal(4, report.PerBaseQuality.Count);
        Assert.All(report.PerBaseQuality, kv => Assert.Equal(40.0, kv.Value.Mean, 1));
    }

    [Fact]
    public async Task FastQReport_ComputesPerBaseComposition()
    {
        // One read: A, C, G, T at cycles 0-3
        var s = MakeSeq("ACGT", "IIII");
        var report = await FastQQualityReport.Compute(Seqs(s));

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
        var report = await FastQQualityReport.Compute(Seqs(s));

        Assert.Single(report.GcContentHistogram);
        Assert.True(report.GcContentHistogram.ContainsKey(50));
    }

    [Fact]
    public async Task FastQReport_HighDuplicationRate_DetectedCorrectly()
    {
        var reads = Enumerable.Repeat(MakeSeq("ACGTACGT", "IIIIIIII"), 100)
            .ToList();

        var report = await FastQQualityReport.Compute(Gen());
        Assert.True(report.DuplicationLevelEstimate > 0.5);
        return;

        async IAsyncEnumerable<Sequence> Gen()
        {
            foreach (var r in reads)
            {
                yield return r;
            }

            await Task.CompletedTask;
        }
    }

    [Fact]
    public async Task FastQReport_AdapterContent_DetectedAtPositions()
    {
        // Read with Nextera adapter starting at position 10
        const string adapter = "CTGTCTCTTATACACATCT";
        var seq = $"ACGTACGTAC{adapter}";
        var qual = new string('I', seq.Length);

        var report = await FastQQualityReport.Compute(
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
        var report = await FastQQualityReport.Compute(Seqs(s));

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

        var report = await FastQQualityReport.Compute(Gen());
        // Cycles 10-13 should have mean quality 0
        for (var i = 10; i < 14; i++)
        {
            Assert.Equal(0.0, report.PerBaseQuality[i].Mean, 0);
        }

        return;

        async IAsyncEnumerable<Sequence> Gen()
        {
            foreach (var r in reads)
            {
                yield return r;
            }

            await Task.CompletedTask;
        }
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// QC-2 — AlignmentStats
// ─────────────────────────────────────────────────────────────────────────────

// ─────────────────────────────────────────────────────────────────────────────
// QC-3 — CoverageCalculator
// ─────────────────────────────────────────────────────────────────────────────

// ─────────────────────────────────────────────────────────────────────────────
// QC-4 — PanelQcReport
// ─────────────────────────────────────────────────────────────────────────────