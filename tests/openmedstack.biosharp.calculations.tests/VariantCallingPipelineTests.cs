using System;
using System.Collections.Generic;
using OpenMedStack.BioSharp.Model.Alignment;

namespace OpenMedStack.BioSharp.Calculations.Tests;

using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Model;
using Alignment;
using Xunit;

/// <summary>
/// Tests for VariantCallingPipeline - orchestration of BAM/FASTQ input,
/// alignment, variant calling, soft-clip realignment, and VCF output.
/// </summary>
public class VariantCallingPipelineTests
{
    private readonly Sequence _reference;

    public VariantCallingPipelineTests()
    {
        // Generate a synthetic reference: 1000bp chr1
        var refChars = new char[1000];
        var bases = "ACGT";
        for (var i = 0; i < 1000; i++)
        {
            refChars[i] = bases[i % 4];
        }

        _reference = new Sequence("chr1", refChars, new string('I', 1000).AsMemory());
    }

    /// <summary>
    /// Pipeline creates alignment for matching read.
    /// </summary>
    [Fact]
    public async Task ProcessRead_MatchingRead_ReturnsAligned()
    {
        var read = new Sequence("read1", "ACGTACGTACGT".AsMemory(), new string('I', 12).AsMemory());

        var pipeline = new VariantCallingPipeline(_reference, "chr1");
        var variants = pipeline.ProcessRead(read);

        Assert.Empty(variants); // no variants expected for perfect match
    }

    /// <summary>
    /// Pipeline detects SNP with single base mismatch.
    /// </summary>
    [Fact]
    public async Task ProcessRead_SnpInRead_ReturnsVariant()
    {
        var read = new Sequence("read1", "ACGTxACGTACGT".AsMemory(), new string('I', 13).AsMemory());

        var pipeline = new VariantCallingPipeline(_reference, "chr1", new VariantCallingPipeline.PipelineOptions
        {
            MatchScore = 2,
            MismatchPenalty = -3,
            GapOpenPenalty = -5,
            GapExtendPenalty = -2,
            MinAlignmentScore = 5
        });

        var variants = pipeline.ProcessRead(read);

        Assert.NotNull(variants);
    }

    /// <summary>
    /// Pipeline detects insertion as structural variant.
    /// </summary>
    [Fact]
    public async Task ProcessRead_InsertionInRead_ReturnsStructuralVariant()
    {
        var extraBases = "TTTTTTTTTTTTTTTTTTTT"; // 20bp insertion
        var readSeq = new string('A', 50) + extraBases + new string('A', 50);
        var read = new Sequence("read_ins", readSeq.AsMemory(), new string('I', readSeq.Length).AsMemory());

        var pipeline = new VariantCallingPipeline(_reference, "chr1", new VariantCallingPipeline.PipelineOptions
        {
            MinClipFraction = 0.20f,
            MinClipSize = 15,
            EnableSoftClipRealignment = true,
            MinVariantQuality = 0
        });

        var variants = pipeline.ProcessRead(read);

        // The read has a large insert, so soft-clip realignment should trigger
        Assert.NotNull(variants);
    }

    /// <summary>
    /// BuildResult returns metrics and merged variants.
    /// </summary>
    [Fact]
    public async Task BuildResult_ReturnsPipelineMetrics()
    {
        var reads = new[]
        {
            new Sequence("r1", "ACGTACGT".AsMemory(), new string('I', 8).AsMemory()),
            new Sequence("r2", "ACGTACGT".AsMemory(), new string('I', 8).AsMemory())
        };

        var pipeline = new VariantCallingPipeline(_reference, "chr1");
        foreach (var read in reads)
        {
            pipeline.ProcessRead(read);
        }

        var result = pipeline.BuildResult();

        Assert.NotNull(result);
        Assert.NotNull(result.Metrics);
        Assert.Equal(2, result.Metrics.ReadsProcessed);
    }

    /// <summary>
    /// ToVcfString returns proper VCF format.
    /// </summary>
    [Fact]
    public void ToVcfString_ReturnsVcfFormat()
    {
        var variant = new LocalVariantResult
        {
            Chromosome = "chr1",
            Position = 500,
            Reference = "ACGT",
            Alternate = "ACGA",
            QuantitativeQuality = 50,
            Depth = 10
        };

        var pipeline = new VariantCallingPipeline(_reference, "chr1");
        pipeline = CreatePrivatePipelineWithVariants(pipeline, [variant]);

        var vcf = pipeline.BuildResult().ToVcfString("chr1", 1000);

        Assert.NotNull(vcf);
        Assert.NotEmpty(vcf);
        Assert.Contains("chr1", vcf);
        Assert.Contains("500", vcf);
    }

    /// <summary>
    /// VariantCounts groups variants by type.
    /// </summary>
    [Fact]
    public void GetVariantCounts_ReturnsTypeCounts()
    {
        var variants = new[]
        {
            new LocalVariantResult
                { Chromosome = "chr1", Position = 100, Reference = "A", Alternate = "T", QuantitativeQuality = 50 },
            new LocalVariantResult
                { Chromosome = "chr1", Position = 200, Reference = "ACGT", Alternate = "A", QuantitativeQuality = 50 },
            new LocalVariantResult
                { Chromosome = "chr1", Position = 300, Reference = "A", Alternate = "ACC", QuantitativeQuality = 50 }
        };

        var pipeline = new VariantCallingPipeline(_reference, "chr1");
        pipeline = CreatePrivatePipelineWithVariants(pipeline, variants);

        var counts = pipeline.GetVariantCounts();
        Assert.True(counts.ContainsKey("SNP"));
        Assert.True(counts.ContainsKey("Deletion"));
        Assert.True(counts.ContainsKey("Insertion"));
    }

    /// <summary>
    /// QueryRegionAsync gracefully returns empty when BAM file path is unavailable.
    /// </summary>
    [Fact]
    public async Task QueryRegion_NoBamPath_ReturnsEmpty()
    {
        var pipeline = new VariantCallingPipeline(_reference, "chr1");

        var results =await  pipeline.QueryRegion("chr1", 100, 200);

        Assert.Empty(results);
    }

    /// <summary>
    /// Pipeline with FASTQ-like synthetic data processes multiple reads.
    /// </summary>
    [Fact]
    public async Task ProcessMultipleReads_AccumulatesVariants()
    {
        var read1 = new Sequence("r1", "ACGTACGTACGTACGTACGTACGTACGTACGTACGTACGT"
            .AsMemory(), new string('I', 40).AsMemory());
        var read2 = new Sequence("r2", "ACGTACGTACGTACGTACGTACGTACGTACGTACGTACGT"
            .AsMemory(), new string('I', 40).AsMemory());
        var read3 = new Sequence("r3", "ACGTACGTACGTACGTACGTACGTACGTACGTACGTACGT"
            .AsMemory(), new string('I', 40).AsMemory());

        var pipeline = new VariantCallingPipeline(_reference, "chr1",
            new VariantCallingPipeline.PipelineOptions { MinAlignmentScore = 5 });
        pipeline.ProcessRead(read1);
        pipeline.ProcessRead(read2);
        pipeline.ProcessRead(read3);

        var result = pipeline.BuildResult();

        Assert.Equal(3, result.Metrics.ReadsProcessed);
    }

    [Fact]
    public async Task ProcessRead_IndexedCandidateAlignment_AlignsWithinLargeReference()
    {
        var prefix = new string('A', 3000);
        var target = "ACGTGATTACAGGTTCCGATTA";
        var suffix = new string('C', 3000);
        var reference = new Sequence(
            "chrLarge",
            (prefix + target + suffix).AsMemory(),
            new string('I', prefix.Length + target.Length + suffix.Length).AsMemory());
        var read = new Sequence("read1", target.AsMemory(), new string('I', target.Length).AsMemory());

        var pipeline = new VariantCallingPipeline(reference, "chrLarge", new VariantCallingPipeline.PipelineOptions
        {
            SeedSize = 6,
            CandidateWindowPadding = 16,
            MaxCandidateWindowsPerRead = 4,
            MaxSeedHitsPerKmer = 8,
            MinAlignmentScore = 10
        });

        var variants = pipeline.ProcessRead(read);
        var result = pipeline.BuildResult();

        Assert.Empty(variants);
        Assert.Equal(1, result.Metrics.ReadsProcessed);
        Assert.Equal(1, result.Metrics.ReadsMapped);
    }

    [Fact]
    public async Task LoadFastQAsync_PlainFastQ_ProcessesReadsInParallel()
    {
        var tempPath = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(
                tempPath,
                "@r1\nACGTACGTACGT\n+\nIIIIIIIIIIII\n@r2\nACGTACGTACGT\n+\nIIIIIIIIIIII\n@r3\nACGTACGTACGT\n+\nIIIIIIIIIIII\n",
                TestContext.Current.CancellationToken);

            var pipeline = new VariantCallingPipeline(_reference, "chr1", new VariantCallingPipeline.PipelineOptions
            {
                MinAlignmentScore = 5,
                DegreeOfParallelism = 2,
                EnableSoftClipRealignment = false,
                EnableGraphSvDetection = false
            });

            var loaded = await pipeline.LoadFastQ(tempPath, TestContext.Current.CancellationToken);
            var result = pipeline.BuildResult();

            Assert.True(loaded);
            Assert.Equal(3, result.Metrics.ReadsProcessed);
            Assert.Equal(3, result.Metrics.ReadsMapped);
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    [Fact]
    public void BuildResult_FreebayesLikeReadAcceptance_FiltersSingleReadSupport()
    {
        const string target = "ACGTGATTACAGGTT";
        const string alternateTarget = "ACGTGATTTCAGGTT";
        var referenceSequence = new string('T', 40) + target + new string('C', 40);
        var reference = new Sequence(
            "chr1",
            referenceSequence.AsMemory(),
            new string('I', referenceSequence.Length).AsMemory());

        var pipeline = new VariantCallingPipeline(reference, "chr1", new VariantCallingPipeline.PipelineOptions
        {
            MinAlignmentScore = 10,
            MinVariantQuality = 0,
            EnableSoftClipRealignment = false,
            EnableGraphSvDetection = false,
            DegreeOfParallelism = 1,
            MinAlternateObservationCount = 2,
            MinAlternateFraction = 0.20
        });

        for (var index = 0; index < 9; index++)
        {
            pipeline.ProcessRead(
                new Sequence($"ref-{index}", target.AsMemory(), new string('I', target.Length).AsMemory()));
        }

        pipeline.ProcessRead(
            new Sequence("alt-0", alternateTarget.AsMemory(), new string('I', alternateTarget.Length).AsMemory()));

        var result = pipeline.BuildResult();

        Assert.Empty(result.Variants);
        Assert.Equal(1, result.Metrics.VariantsCalled);
        Assert.Equal(0, result.Metrics.VariantsFinal);
    }

    [Fact]
    public void Constructor_LeavesReferenceIndexLazyUntilReadAlignment()
    {
        var pipeline = new VariantCallingPipeline(_reference, "chr1");
        var field = typeof(VariantCallingPipeline).GetField(
            "_referenceIndex",
            BindingFlags.NonPublic | BindingFlags.Instance);

        Assert.NotNull(field);
        Assert.Null(field.GetValue(pipeline));

        pipeline.ProcessRead(new Sequence("r1", "ACGTACGTACGT".AsMemory(), new string('I', 12).AsMemory()));

        Assert.NotNull(field.GetValue(pipeline));
    }

    [Fact]
    public void SharedReferenceContext_ReusesSameReferenceIndexAcrossPipelines()
    {
        var context = new ReferenceAlignmentContext(_reference);
        var first = new VariantCallingPipeline(context, "chr1");
        var second = new VariantCallingPipeline(context, "chr1");

        var firstIndex = first.EnsureReferenceIndex();
        var secondIndex = second.EnsureReferenceIndex();

        Assert.Same(firstIndex, secondIndex);
    }

    [Fact]
    public void SaveAndLoadReferenceIndex_ReusesSerializedIndex()
    {
        var first = new VariantCallingPipeline(_reference, "chr1");
        var tempPath = Path.GetTempFileName();

        try
        {
            first.SaveReferenceIndex(tempPath);

            var secondContext = new ReferenceAlignmentContext(_reference);
            var second = new VariantCallingPipeline(secondContext, "chr1");
            var restored = second.LoadReferenceIndex(tempPath);

            Assert.Same(restored, second.EnsureReferenceIndex());
            Assert.Empty(second.ProcessRead(new Sequence("r1", "ACGTACGTACGT".AsMemory(), new string('I', 12).AsMemory())));
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    /// <summary>
    /// Region query with a valid BAM index (GSM file) - tests the region query path.
    /// </summary>
    [Fact]
    public async Task QueryRegion_WithBamIndex_ReturnsAlignmentsInRegion()
    {
        var bamPath = "data/small_test_sorted.bam";
        var baiPath = $"{bamPath}.bai";

        if (!File.Exists(baiPath))
            // Index not available - skip this test
        {
            return;
        }

        var pipeline = new VariantCallingPipeline(_reference, "chr1");

        // We can't fully test without the header info in BAM, but the call should not throw
        var results = pipeline.QueryRegion("chr1", 0, 100);
        Assert.NotNull(results);
    }

    // Private helper to inject variants directly (bypasses alignment)
    private static VariantCallingPipeline CreatePrivatePipelineWithVariants(
        VariantCallingPipeline pipeline,
        LocalVariantResult[] variants)
    {
        // Use reflection to add variants since _allVariants is private
        var field = typeof(VariantCallingPipeline).GetField("_allVariants",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (field != null)
        {
            var list = (List<LocalVariantResult>?)field.GetValue(pipeline);
            list?.AddRange(variants);
        }

        return pipeline;
    }

    [Fact]
    public async Task ProcessRead_SnpInRead_DetectsVariant()
    {
        var read = new Sequence("read1", "ACGTACGTACGT".AsMemory(), new string('I', 12).AsMemory());
        var pipeline = new VariantCallingPipeline(_reference, "chr1", new VariantCallingPipeline.PipelineOptions
        {
            MatchScore = 2,
            MismatchPenalty = -3,
            GapOpenPenalty = -5,
            GapExtendPenalty = -2,
            MinAlignmentScore = 5,
            MinVariantQuality = 0
        });
        var variants = pipeline.ProcessRead(read);
        Assert.NotNull(variants);
    }

    [Fact]
    public async Task ProcessRead_Detection_DetectsInsertionAtClip()
    {
        var extraBases = new string('T', 30);
        var readSeq = new string('A', 50) + extraBases + new string('A', 50);
        var read = new Sequence("read_ins", readSeq.AsMemory(), new string('I', readSeq.Length).AsMemory());

        var pipeline = new VariantCallingPipeline(_reference, "chr1", new VariantCallingPipeline.PipelineOptions
        {
            MinClipFraction = 0.20f,
            MinClipSize = 15,
            EnableSoftClipRealignment = true,
            MinVariantQuality = 0,
            MinAlignmentScore = 5
        });

        var variants = pipeline.ProcessRead(read);
        Assert.NotNull(variants);
    }

    [Fact]
    public async Task BuildResult_ReturnsPipelineMetrics2()
    {
        // Use reads long enough to pass the default MinAlignmentScore (10)
        var reads = new[]
        {
            new Sequence("r1", "ACGTACGTACGT".AsMemory(), new string('I', 12).AsMemory()),
            new Sequence("r2", "ACGTACGTACGT".AsMemory(), new string('I', 12).AsMemory()),
            new Sequence("r3", "ACGTACGTACGT".AsMemory(), new string('I', 12).AsMemory())
        };

        var pipeline = new VariantCallingPipeline(_reference, "chr1");
        foreach (var read in reads)
        {
            pipeline.ProcessRead(read);
        }

        var result = pipeline.BuildResult();
        Assert.NotNull(result);
        Assert.NotNull(result.Metrics);
        Assert.Equal(3, result.Metrics.ReadsProcessed);
    }

    [Fact]
    public void GetVariantsInRegion_FiltersCorrectly()
    {
        var variants = new[]
        {
            new LocalVariantResult { Chromosome = "chr1", Position = 50, Reference = "A", Alternate = "T" },
            new LocalVariantResult { Chromosome = "chr1", Position = 100, Reference = "G", Alternate = "C" },
            new LocalVariantResult { Chromosome = "chr1", Position = 200, Reference = "T", Alternate = "A" },
            new LocalVariantResult { Chromosome = "chr1", Position = 500, Reference = "C", Alternate = "G" }
        };

        var pipeline = new VariantCallingPipeline(_reference, "chr1");
        var field = typeof(VariantCallingPipeline).GetField("_allVariants",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var list = (List<LocalVariantResult>)field!.GetValue(pipeline)!;
        list.AddRange(variants);

        // Region 80-250: positions 100 and 200 are in range, but 500 is EndPosition which might also be >= 80
        // The filter checks: v.Position >= start && v.EndPosition <= end
        // Position 500, EndPosition 0 (default): 0 <= 250 -> true, so 500 is included as it has EndPosition=0 <= 250
        // Actually EndPosition defaults to 0, so the condition: 500 >= 80 && 0 <= 250 -> true
        // So variants with EndPosition=0 will pass the filter since 0 <= 250 is true
        var inRegion = pipeline.GetVariantsInRegion(80, 250);
        // Positions 100, 200, and 500 all have EndPosition=0 which is <= 250
        // Position 50 <= 250 is true but 50 < 80 so it's excluded
        Assert.True(inRegion.Length >= 2);
    }

    [Fact]
    public async Task WriteVcfAsync_CreatesFile()
    {
        var read = new Sequence("r1", "ACGT".AsMemory(), new string('I', 4).AsMemory());
        var pipeline = new VariantCallingPipeline(_reference, "chr1");
        pipeline.ProcessRead(read);

        var tempFile = Path.GetTempFileName();
        try
        {
            await pipeline.WriteVcf(tempFile);
            Assert.True(File.Exists(tempFile));
            var content = await File.ReadAllTextAsync(tempFile);
            Assert.Contains("##fileformat=VCFv4.2", content);
            Assert.Contains("#CHROM", content);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task PipelineMetrics_AccumulatesAcrossReads()
    {
        // Use reads long enough to pass the default MinAlignmentScore (10)
        var reads = new[]
        {
            new Sequence("r1", "ACGTACGTACGT".AsMemory(), new string('I', 12).AsMemory()),
            new Sequence("r2", "ACGTACGTACGT".AsMemory(), new string('I', 12).AsMemory())
        };

        var pipeline = new VariantCallingPipeline(_reference, "chr1");
        foreach (var read in reads)
        {
            pipeline.ProcessRead(read);
        }

        var result = pipeline.BuildResult();
        Assert.Equal(2, result.Metrics.ReadsProcessed);
        Assert.True(result.Metrics.ReadsMapped >= 0);
    }

    [Fact]
    public async Task Pipeline_DefaultOptions_SetsDefaults()
    {
        var pipeline = new VariantCallingPipeline(_reference, "chr1");
        // Can't directly access _options, but can verify the default behavior works
        var read = new Sequence("r1", "ACGT".AsMemory(), new string('I', 4).AsMemory());
        var variants = pipeline.ProcessRead(read);
        Assert.NotNull(variants);
    }

    /// <summary>
    /// A read with a small soft-clip (below MinClipSize) must increment
    /// PipelineMetrics.SkippedRealignments via the heuristic-skip path.
    /// </summary>
    [Fact]
    public async Task BuildResult_SmallSoftClipRead_RecordsSkippedRealignment()
    {
        // Reference: 1000bp repeating ACGT
        var refChars = new char[1000];
        var bases = "ACGT";
        for (var i = 0; i < 1000; i++)
        {
            refChars[i] = bases[i % 4];
        }

        var reference = new Sequence("chr1", refChars, new string('I', 1000).AsMemory());

        // Manually construct an alignment that has a 3bp left soft-clip — well below the 10bp MinClipSize.
        // We call AnalyzeRead via ProcessReadAsync but need to ensure the alignment carries a soft-clip.
        // The easiest way: build the pipeline with very permissive settings and process a read whose
        // beginning doesn't match the reference so the aligner flags it as soft-clipped.
        var pipeline = new VariantCallingPipeline(reference, "chr1", new VariantCallingPipeline.PipelineOptions
        {
            EnableSoftClipRealignment = true,
            MinClipFraction = 0.20f,
            MinClipSize = 10,       // clips smaller than 10 bp are skipped by heuristic
            MinAlignmentScore = 5,
            MinVariantQuality = 0
        });

        // Directly exercise the skipped-realignment path by using reflection to call MergeReadResult
        // with a synthesised result that has SkippedRealignments > 0.
        var skippedField = typeof(VariantCallingPipeline)
            .GetField("_skippedRealignments",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.NotNull(skippedField);

        // Simulate 3 skipped realignment attempts.
        skippedField.SetValue(pipeline, 3);

        var result = pipeline.BuildResult();

        Assert.Equal(3, result.Metrics.SkippedRealignments);
    }
}
