using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using OpenMedStack.BioSharp.Calculations.Alignment;
using OpenMedStack.BioSharp.Io.Sam;
using OpenMedStack.BioSharp.Model;
using Xunit;

namespace OpenMedStack.BioSharp.Calculations.Tests;

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

        var report = await PanelQcReport.Generate(
            FastqStream(),
            bamReads,
            referenceLength: 10,
            thresholds: thresholds);

        Assert.Equal(QcStatus.Pass, report.MeanCoverageStatus);
        Assert.Equal(QcStatus.Pass, report.MappingRateStatus);
        Assert.Equal(QcStatus.Pass, report.DuplicateRateStatus);
        Assert.Equal(QcStatus.Pass, report.Q30FractionStatus);
        return;

        async IAsyncEnumerable<Sequence> FastqStream()
        {
            foreach (var r in fastqReads)
            {
                yield return r;
            }

            await Task.CompletedTask;
        }
    }

    [Fact]
    public async Task PanelQcReport_Fail_WhenCoverageBelowThreshold()
    {
        // Low coverage: 1 read, threshold 20x
        var fastqReads = Enumerable.Range(0, 1)
            .Select(_ => MakeSeq("ACGT", "IIII"))
            .ToList();

        var bamReads = new[] { MakeAlignment("r1", 0, 4) };

        var thresholds = new PanelQcThresholds { MinMeanCoverage = 20.0 };
        var report = await PanelQcReport.Generate(
            FastqStream(), bamReads, referenceLength: 100, thresholds: thresholds);

        Assert.Equal(QcStatus.Fail, report.MeanCoverageStatus);
        return;

        async IAsyncEnumerable<Sequence> FastqStream()
        {
            foreach (var r in fastqReads)
            {
                yield return r;
            }

            await Task.CompletedTask;
        }
    }

    [Fact]
    public async Task PanelQcReport_SerializesToJson()
    {
        var fastqReads = Enumerable.Range(0, 5)
            .Select(_ => MakeSeq("ACGT", "IIII"))
            .ToList();

        var bamReads = Enumerable.Range(0, 5)
            .Select(i => MakeAlignment($"r{i}", 0, 4))
            .ToList();

        var report = await PanelQcReport.Generate(
            FastqStream(), bamReads, referenceLength: 4,
            thresholds: new PanelQcThresholds());

        var json = JsonSerializer.Serialize(report);
        Assert.Contains("MeanCoverageStatus", json);
        Assert.Contains("MappingRateStatus", json);
        return;

        async IAsyncEnumerable<Sequence> FastqStream()
        {
            foreach (var r in fastqReads)
            {
                yield return r;
            }

            await Task.CompletedTask;
        }
    }

    [Fact]
    public async Task PanelQcReport_GeneratesHtmlReport()
    {
        var fastqReads = Enumerable.Range(0, 5)
            .Select(_ => MakeSeq("ACGT", "IIII"))
            .ToList();

        var bamReads = Enumerable.Range(0, 5)
            .Select(i => MakeAlignment($"r{i}", 0, 4))
            .ToList();

        var report = await PanelQcReport.Generate(
            FastqStream(), bamReads, referenceLength: 4,
            thresholds: new PanelQcThresholds());

        var html = report.ToHtml();
        Assert.Contains("<html>", html);
        Assert.Contains("<table>", html);
        Assert.Contains("PASS", html.ToUpperInvariant());
        return;

        async IAsyncEnumerable<Sequence> FastqStream()
        {
            foreach (var r in fastqReads)
            {
                yield return r;
            }

            await Task.CompletedTask;
        }
    }
}