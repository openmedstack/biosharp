using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using OpenMedStack.BioSharp.Calculations.Alignment;
using OpenMedStack.BioSharp.Model;
using Xunit;

namespace OpenMedStack.BioSharp.Calculations.Tests;

public class ProgressReportingTests
{
    private static Sequence MakeSeq(string id, string bases)
        => new Sequence(id, bases.AsMemory(), new string('I', bases.Length).AsMemory());

    [Fact]
    public async Task VariantCallingPipeline_ProgressReported_AtLeastOncePerThreshold()
    {
        var refSeq = new string('A', 200);
        var reference = MakeSeq("chr1", refSeq);
        var pipeline = new VariantCallingPipeline(reference, "chr1");

        var progressReports = new List<PipelineProgress>();
        var progress = new Progress<PipelineProgress>(p => progressReports.Add(p));

        await pipeline.LoadFastQ(Reads(), progress: progress);

        Assert.NotEmpty(progressReports);
        Assert.All(progressReports, p => Assert.True(p.ReadsProcessed >= 0));
        return;

        // Generate 10,000 identical reads to trigger progress
        async IAsyncEnumerable<Sequence> Reads()
        {
            for (var i = 0; i < 10_001; i++)
            {
                yield return MakeSeq($"r{i}", "AAAAAAAAAAAAAAAAAA");
            }

            await Task.CompletedTask;
        }
    }

    [Fact]
    public async Task VariantCallingPipeline_CancellationToken_StopsCleanly()
    {
        var refSeq = new string('A', 200);
        var reference = MakeSeq("chr1", refSeq);
        var pipeline = new VariantCallingPipeline(reference, "chr1");

        using var cts = new CancellationTokenSource();

        // Cancel after 100ms
        cts.CancelAfter(100);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => pipeline.LoadFastQ(Reads(cts.Token), cancellationToken: cts.Token));
        return;

        async IAsyncEnumerable<Sequence> Reads(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            for (var i = 0; i < 1_000_000; i++)
            {
                ct.ThrowIfCancellationRequested();
                yield return MakeSeq($"r{i}", "AAAAAAAAAAAAAAAAAA");
            }
        }
    }

    [Fact]
    public void PipelineProgress_SerializesToJson()
    {
        var progress = new PipelineProgress
        {
            ReadsProcessed = 1000,
            ReadsMapped = 950,
            ReadsFiltered = 50,
            VariantsCalled = 10,
            CurrentPhase = "Alignment",
            Elapsed = TimeSpan.FromSeconds(5)
        };

        var json = JsonSerializer.Serialize(progress);
        Assert.Contains("ReadsProcessed", json);
        Assert.Contains("CurrentPhase", json);
    }
}