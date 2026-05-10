using Microsoft.Extensions.Logging.Abstractions;

namespace OpenMedStack.BioSharp.Calculations.Tests;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Alignment;
using Microsoft.Extensions.DependencyInjection;
using Io;
using Io.FastA;
using Io.Sam;
using Model;
using Report;
using Xunit;

// ─────────────────────────────────────────────────────────────────────────────
// CLIN-1 — Audit logging and pipeline provenance
// ─────────────────────────────────────────────────────────────────────────────
public class AuditLoggingTests
{
    [Fact]
    public void PipelineProvenance_ContainsRequiredFields()
    {
        var prov = new PipelineProvenance
        {
            PipelineVersion = "1.1.0",
            ReferenceChecksum = "abc123",
            InputFileChecksums = new Dictionary<string, string> { ["input.fastq"] = "def456" },
            Options = JsonSerializer.Serialize(new { MinAlignmentScore = 10 }),
            StartTime = DateTimeOffset.UtcNow,
            EndTime = DateTimeOffset.UtcNow.AddSeconds(5),
            ReadsProcessed = 100,
            ReadsMapped = 90,
            VariantsCalled = 5
        };

        var json = JsonSerializer.Serialize(prov);
        Assert.Contains("PipelineVersion", json);
        Assert.Contains("ReferenceChecksum", json);
        Assert.Contains("InputFileChecksums", json);
        Assert.Contains("Options", json);
        Assert.Contains("StartTime", json);
        Assert.Contains("EndTime", json);
        Assert.Contains("ReadsProcessed", json);
        Assert.Contains("VariantsCalled", json);
    }

    [Fact]
    public async Task ProvenanceWriter_WritesAtomically()
    {
        var prov = new PipelineProvenance
        {
            PipelineVersion = "1.1.0",
            ReferenceChecksum = "sha256:abc",
            StartTime = DateTimeOffset.UtcNow,
            EndTime = DateTimeOffset.UtcNow
        };

        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        var outputPath = Path.Combine(dir, "output.vcf");
        try
        {
            await ProvenanceWriter.WriteAsync(prov, outputPath);
            var provPath = outputPath + ".provenance.json";
            Assert.True(File.Exists(provPath));
            var content = await File.ReadAllTextAsync(provPath);
            var deserialized = JsonSerializer.Deserialize<PipelineProvenance>(content);
            Assert.NotNull(deserialized);
            Assert.Equal("1.1.0", deserialized.PipelineVersion);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// CLIN-2 — Sample tracking and run metadata
// ─────────────────────────────────────────────────────────────────────────────
public class SampleSheetTests
{
    private const string SingleSampleCsv = """
        [Header]
        IEMFileVersion,4
        Investigator Name,Test
        Date,2024-01-01
        Workflow,GenerateFASTQ
        [Reads]
        151
        151
        [Settings]
        [Data]
        Lane,Sample_ID,Sample_Name,Sample_Plate,Sample_Well,I7_Index_ID,index,I5_Index_ID,index2,Sample_Project,Description
        1,S1,SampleOne,,A01,N701,TAAGGCGA,S501,TAGATCGC,Project1,Test sample
        """;

    private const string MultiSampleCsv = """
        [Header]
        IEMFileVersion,4
        [Reads]
        151
        [Settings]
        [Data]
        Lane,Sample_ID,Sample_Name,I7_Index_ID,index,Sample_Project
        1,S1,SampleOne,N701,TAAGGCGA,Proj1
        1,S2,SampleTwo,N702,CGTACTAG,Proj1
        """;

    [Fact]
    public async Task SampleSheetReader_ParsesSingleSampleSheet()
    {
        using var ms = new MemoryStream(Encoding.UTF8.GetBytes(SingleSampleCsv));
        var sheet = await SampleSheetReader.ReadAsync(ms);

        Assert.Single(sheet.Samples);
        Assert.Equal("S1", sheet.Samples[0].SampleId);
        Assert.Equal("SampleOne", sheet.Samples[0].SampleName);
        Assert.Equal("TAAGGCGA", sheet.Samples[0].Index1);
        Assert.Equal("TAGATCGC", sheet.Samples[0].Index2);
        Assert.Equal("Project1", sheet.Samples[0].Project);
    }

    [Fact]
    public async Task SampleSheetReader_ParsesMultiSampleSheet()
    {
        using var ms = new MemoryStream(Encoding.UTF8.GetBytes(MultiSampleCsv));
        var sheet = await SampleSheetReader.ReadAsync(ms);

        Assert.Equal(2, sheet.Samples.Count);
        Assert.Equal("S1", sheet.Samples[0].SampleId);
        Assert.Equal("S2", sheet.Samples[1].SampleId);
    }

    [Fact]
    public async Task SampleSheetReader_ParsesHeaderSection()
    {
        using var ms = new MemoryStream(Encoding.UTF8.GetBytes(SingleSampleCsv));
        var sheet = await SampleSheetReader.ReadAsync(ms);

        Assert.Equal("4", sheet.Header["IEMFileVersion"]);
        Assert.Equal("GenerateFASTQ", sheet.Header["Workflow"]);
    }

    [Fact]
    public async Task SampleSheetReader_ParsesReadsSection()
    {
        using var ms = new MemoryStream(Encoding.UTF8.GetBytes(SingleSampleCsv));
        var sheet = await SampleSheetReader.ReadAsync(ms);

        Assert.Equal(2, sheet.ReadLengths.Count);
        Assert.Equal(151, sheet.ReadLengths[0]);
        Assert.Equal(151, sheet.ReadLengths[1]);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// CLIN-3 — Reference genome checksum validation
// ─────────────────────────────────────────────────────────────────────────────
public class ReferenceValidatorTests
{
    [Fact]
    public async Task ReferenceValidator_ComputesChecksums_ForKnownContent()
    {
        // Write a small fake FASTA
        var content = ">chr1\nACGTACGT\n";
        using var ms = new MemoryStream(Encoding.ASCII.GetBytes(content));

        var result = await ReferenceValidator.ComputeChecksumsAsync(ms);

        Assert.NotNull(result.Md5);
        Assert.NotNull(result.Sha256);
        Assert.False(string.IsNullOrEmpty(result.Md5));
        Assert.False(string.IsNullOrEmpty(result.Sha256));
    }

    [Fact]
    public async Task ReferenceValidator_SameFastaProducesSameChecksum()
    {
        var content = ">chr1\nACGTACGT\n";
        using var ms1 = new MemoryStream(Encoding.ASCII.GetBytes(content));
        using var ms2 = new MemoryStream(Encoding.ASCII.GetBytes(content));

        var r1 = await ReferenceValidator.ComputeChecksumsAsync(ms1);
        var r2 = await ReferenceValidator.ComputeChecksumsAsync(ms2);

        Assert.Equal(r1.Md5, r2.Md5);
        Assert.Equal(r1.Sha256, r2.Sha256);
    }

    [Fact]
    public async Task ReferenceValidator_Validate_ThrowsOnChecksuMismatch()
    {
        var content = ">chr1\nACGTACGT\n";
        using var ms = new MemoryStream(Encoding.ASCII.GetBytes(content));

        await Assert.ThrowsAsync<ReferenceValidationException>(
            () => ReferenceValidator.ValidateAsync(ms, expectedMd5: "wrong_checksum"));
    }

    [Fact]
    public async Task ReferenceValidator_Validate_SucceedsWithCorrectChecksum()
    {
        var content = ">chr1\nACGTACGT\n";
        var bytes = Encoding.ASCII.GetBytes(content);
        var expectedMd5 = Convert.ToHexString(MD5.HashData(bytes)).ToLowerInvariant();

        using var ms = new MemoryStream(bytes);
        // Should not throw
        await ReferenceValidator.ValidateAsync(ms, expectedMd5: expectedMd5);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// CLIN-4 — Clinical variant report generation
// ─────────────────────────────────────────────────────────────────────────────
public class ClinicalReportWriterTests
{
    private static VariantAnnotation MakeAnnotation(string chrom, int pos, string hgvs, string clinSig)
        => new VariantAnnotation
        {
            Chromosome = chrom,
            Position = pos,
            Reference = "A",
            Alternate = "T",
            HgvsNotation = hgvs,
            ClinicalSignificance = clinSig,
            Consequence = VariantConsequence.Missense
        };

    [Fact]
    public void ClinicalReportWriter_JsonReport_ContainsAllVariants()
    {
        var variants = new[]
        {
            MakeAnnotation("chr1", 100, "NM_001.1:c.100A>T", "Pathogenic"),
            MakeAnnotation("chr1", 200, "NM_001.1:c.200A>T", "Benign"),
        };

        var metadata = new ClinicalReportMetadata
        {
            SampleName = "Sample1",
            RunDate = DateTimeOffset.UtcNow,
            PipelineVersion = "1.1.0",
            ReferenceGenome = "GRCh38"
        };

        var writer = new ClinicalReportWriter();
        var json = writer.WriteJson(variants, metadata);

        Assert.Contains("chr1", json);
        Assert.Contains("Pathogenic", json);
        Assert.Contains("Sample1", json);
        Assert.Contains("GRCh38", json);
    }

    [Fact]
    public void ClinicalReportWriter_JsonReport_VariantsSortedByChromPos()
    {
        var variants = new[]
        {
            MakeAnnotation("chr1", 300, "c.300A>T", "VUS"),
            MakeAnnotation("chr1", 100, "c.100A>T", "Pathogenic"),
        };

        var writer = new ClinicalReportWriter();
        var json = writer.WriteJson(variants, new ClinicalReportMetadata());

        // Position 100 must come before 300 in JSON
        var idx100 = json.IndexOf("100", StringComparison.Ordinal);
        var idx300 = json.IndexOf("300", StringComparison.Ordinal);
        Assert.True(idx100 < idx300, "Variants should be sorted by position");
    }

    [Fact]
    public void ClinicalReportWriter_HtmlReport_ContainsRequiredElements()
    {
        var variants = new[]
        {
            MakeAnnotation("chr1", 100, "c.100A>T", "Pathogenic")
        };

        var writer = new ClinicalReportWriter();
        var html = writer.WriteHtml(variants, new ClinicalReportMetadata { SampleName = "S1" });

        Assert.Contains("<html>", html);
        Assert.Contains("<table>", html);
        Assert.Contains("Pathogenic", html);
        Assert.Contains("S1", html);
        // No JavaScript (renders without JS)
        Assert.DoesNotContain("<script>", html);
    }

    [Fact]
    public void ClinicalReportWriter_HtmlReport_IncludesQcMetrics()
    {
        var variants = Array.Empty<VariantAnnotation>();
        var metadata = new ClinicalReportMetadata { SampleName = "S1" };
        var qcSummary = new QcSummary
        {
            MeanCoverage = 30.5,
            MappingRate = 0.98,
            DuplicateRate = 0.05
        };

        var writer = new ClinicalReportWriter();
        var html = writer.WriteHtml(variants, metadata, qcSummary);

        Assert.Contains("30.5", html);
        Assert.Contains("0.98", html);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// PERF-1 — Multi-threaded FASTQ reading
// ─────────────────────────────────────────────────────────────────────────────
public class MultithreadedReadingTests
{
    private static string BuildFastqContent(int readCount, int readLength = 50)
    {
        var sb = new StringBuilder();
        var bases = "ACGT";
        var rng = new Random(42);
        for (var i = 0; i < readCount; i++)
        {
            sb.AppendLine($"@read_{i}");
            for (var j = 0; j < readLength; j++)
            {
                sb.Append(bases[rng.Next(4)]);
            }

            sb.AppendLine();
            sb.AppendLine("+");
            sb.Append(new string('I', readLength));
            sb.AppendLine();
        }
        return sb.ToString();
    }

    [Fact]
    public async Task FastQReader_ReadAheadBuffer_ReturnsAllRecords()
    {
        const int readCount = 1000;
        var content = BuildFastqContent(readCount);
        using var ms = new MemoryStream(Encoding.ASCII.GetBytes(content));

        var reader = new Io.FastQ.FastQReader(NullLogger.Instance);

        var reads = new List<Sequence>();
        await foreach (var seq in reader.ReadWithReadAheadAsync(ms))
        {
            reads.Add(seq);
        }

        Assert.Equal(readCount, reads.Count);
    }

    [Fact]
    public async Task FastQReader_ReadAheadBuffer_NoRecordLostOrDuplicated()
    {
        const int readCount = 500;
        var content = BuildFastqContent(readCount);
        using var ms = new MemoryStream(Encoding.ASCII.GetBytes(content));

        var reader = new Io.FastQ.FastQReader(
            NullLogger.Instance);

        var readIds = new HashSet<string>();
        await foreach (var seq in reader.ReadWithReadAheadAsync(ms))
        {
            Assert.True(readIds.Add(seq.Id), $"Duplicate read detected: {seq.Id}");
        }
        Assert.Equal(readCount, readIds.Count);
    }

    [Fact]
    public async Task FastQReader_ReadAheadBuffer_PreservesRecordOrder()
    {
        const int readCount = 100;
        var content = BuildFastqContent(readCount);
        using var ms1 = new MemoryStream(Encoding.ASCII.GetBytes(content));
        using var ms2 = new MemoryStream(Encoding.ASCII.GetBytes(content));

        var reader = new Io.FastQ.FastQReader(
            NullLogger.Instance);

        var sequential = new List<string>();
        await foreach (var seq in reader.Read(ms1))
        {
            sequential.Add(seq.Id);
        }

        var parallel = new List<string>();
        await foreach (var seq in reader.ReadWithReadAheadAsync(ms2))
        {
            parallel.Add(seq.Id);
        }

        Assert.Equal(sequential, parallel);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// PERF-2 — SIMD-accelerated sequence comparison
// ─────────────────────────────────────────────────────────────────────────────
public class SimdSequenceTests
{
    private static Sequence MakeSeq(string bases)
        => new Sequence("s", bases.AsMemory(), new string('I', bases.Length).AsMemory());

    [Fact]
    public void SimdSequence_CountMismatches_MatchesScalar()
    {
        const string a = "ACGTACGTACGTACGTACGTACGTACGTACGT"; // 32 chars
        const string b = "ACGTACGTACGTACGTCCGTACGTACGTACGT"; // diff at index 16

        var seqA = MakeSeq(a);
        var seqB = MakeSeq(b);

        var scalarCount = SequenceSimd.CountMismatchesScalar(seqA.GetData().Span, seqB.GetData().Span);
        var simdCount = SequenceSimd.CountMismatches(seqA.GetData().Span, seqB.GetData().Span);

        Assert.Equal(scalarCount, simdCount);
        Assert.Equal(1, simdCount);
    }

    [Fact]
    public void SimdSequence_CountMismatches_ForRandomSequences()
    {
        var rng = new Random(42);
        for (var len = 1; len <= 256; len++)
        {
            var a = new char[len];
            var b = new char[len];
            var bases = "ACGT";
            for (var i = 0; i < len; i++)
            {
                a[i] = bases[rng.Next(4)];
                b[i] = bases[rng.Next(4)];
            }
            var scalar = SequenceSimd.CountMismatchesScalar(a, b);
            var simd = SequenceSimd.CountMismatches(a, b);
            Assert.Equal(scalar, simd);
        }
    }

    [Fact]
    public void SimdSequence_ReverseComplement_MatchesScalar()
    {
        const string seq = "ACGTACGTACGTACGT";
        var seqMem = seq.AsMemory();

        var scalar = SequenceSimd.ReverseComplementScalar(seqMem.Span);
        var simd = SequenceSimd.ReverseComplement(seqMem.Span);

        Assert.Equal(new string(scalar), new string(simd));
    }

    [Fact]
    public void SimdSequence_FindKmer_MatchesScalar()
    {
        const string seq = "ACGTACGTACGTTTTTACGT";
        const string kmer = "ACGT";

        var seqSpan = seq.AsSpan();
        var kmerSpan = kmer.AsSpan();

        var scalarPositions = SequenceSimd.FindKmerScalar(seqSpan, kmerSpan).ToList();
        var simdPositions = SequenceSimd.FindKmer(seqSpan, kmerSpan).ToList();

        Assert.Equal(scalarPositions, simdPositions);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// PERF-3 — Streaming De Bruijn graph with bounded memory
// ─────────────────────────────────────────────────────────────────────────────
public class StreamingDeBruijnTests
{
    private static async IAsyncEnumerable<Sequence> GenerateReads(string refSeq, int count, int readLen = 20)
    {
        for (var i = 0; i < count; i++)
        {
            var start = i % (refSeq.Length - readLen);
            var read = refSeq.Substring(start, readLen);
            yield return new Sequence($"read_{i}", read.AsMemory(), new string('I', readLen).AsMemory());
        }
        await Task.CompletedTask;
    }

    [Fact]
    public async Task StreamingDeBruijn_CanBuildGraph_WithoutBufferingAllReads()
    {
        const string refSeq = "ACGTACGTACGTACGTACGTACGTACGTACGT";
        var graph = new DeBruijn.StreamingDeBruijnGraph(k: 10);

        await graph.IngestAsync(GenerateReads(refSeq, 50));
        var stats = graph.GetStats();

        Assert.True(stats.NodeCount > 0);
        Assert.False(stats.AllReadsBuffered, "Streaming graph should not buffer all reads");
    }

    [Fact]
    public async Task StreamingDeBruijn_PrunesLowCoverageNodes()
    {
        const string refSeq = "ACGTACGTACGTACGTACGTACGTACGTACGT";
        var graph = new DeBruijn.StreamingDeBruijnGraph(
            k: 10, maxNodes: 5, minCoverageToKeep: 2);

        await graph.IngestAsync(GenerateReads(refSeq, 100));
        var stats = graph.GetStats();

        Assert.True(stats.NodeCount <= 5 || stats.PrunedNodes > 0,
            "Graph should have pruned low-coverage nodes");
    }

    [Fact]
    public async Task StreamingDeBruijn_ExistingTestsStillPass()
    {
        // Verify basic assembly still works with streaming graph
        const string refSeq = "ACGTACGTACGTACGTACGTACGTACGTACGT"; // 32 chars, readLen=20 → no divide-by-zero
        var reads = GenerateReads(refSeq, 50);
        var graph = new DeBruijn.StreamingDeBruijnGraph(k: 8);
        await graph.IngestAsync(reads);

        var contigs = await graph.AssembleContigsAsync();
        Assert.NotEmpty(contigs);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// DEV-1 — Microsoft.Extensions.DependencyInjection registration extensions
// ─────────────────────────────────────────────────────────────────────────────
public class DependencyInjectionTests
{
    [Fact]
    public void AddBioSharpIo_RegistersRequiredServices()
    {
        var services = new ServiceCollection();
        services.AddBioSharpIo();

        var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetService<Io.FastQ.FastQReader>());
        Assert.NotNull(provider.GetService<FastAReader>());
    }

    [Fact]
    public void AddBioSharpCalculations_RegistersPipelineFactory()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddBioSharpCalculations();

        var provider = services.BuildServiceProvider();

        var factory = provider.GetService<Func<Sequence, string, VariantCallingPipeline>>();
        Assert.NotNull(factory);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// DEV-4 — Structured progress reporting and cancellation
// ─────────────────────────────────────────────────────────────────────────────
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

        // Generate 10,000 identical reads to trigger progress
        async IAsyncEnumerable<Sequence> Reads()
        {
            for (var i = 0; i < 10_001; i++)
            {
                yield return MakeSeq($"r{i}", "AAAAAAAAAAAAAAAAAA");
            }

            await Task.CompletedTask;
        }

        await pipeline.LoadFastQAsync(Reads(), progress: progress);

        Assert.NotEmpty(progressReports);
        Assert.All(progressReports, p => Assert.True(p.ReadsProcessed >= 0));
    }

    [Fact]
    public async Task VariantCallingPipeline_CancellationToken_StopsCleanly()
    {
        var refSeq = new string('A', 200);
        var reference = MakeSeq("chr1", refSeq);
        var pipeline = new VariantCallingPipeline(reference, "chr1");

        using var cts = new CancellationTokenSource();

        async IAsyncEnumerable<Sequence> Reads(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            for (var i = 0; i < 1_000_000; i++)
            {
                ct.ThrowIfCancellationRequested();
                yield return MakeSeq($"r{i}", "AAAAAAAAAAAAAAAAAA");
            }
        }

        // Cancel after 100ms
        cts.CancelAfter(100);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => pipeline.LoadFastQAsync(Reads(cts.Token), cancellationToken: cts.Token));
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

// ─────────────────────────────────────────────────────────────────────────────
// DEV-5 — Synthetic test data generator
// ─────────────────────────────────────────────────────────────────────────────
public class TestDataGeneratorTests
{
    [Fact]
    public void TestDataGenerator_GeneratesValidReferenceFasta()
    {
        var gen = new TestDataGenerator(seed: 42);
        var fasta = gen.GenerateReference(length: 1000, repeatFraction: 0.1);

        Assert.Equal(1000, fasta.Length);
        Assert.All(fasta.ToCharArray(), c => Assert.Contains(c, new[] { 'A', 'C', 'G', 'T' }));
    }

    [Fact]
    public async Task TestDataGenerator_SimulatesReads_CorrectCount()
    {
        var gen = new TestDataGenerator(seed: 42);
        var reference = gen.GenerateReference(length: 500);

        var reads = new List<Sequence>();
        await foreach (var r in gen.SimulateReadsAsync(reference, depth: 10, readLength: 50))
        {
            reads.Add(r);
        }

        // depth 10 × length 500 / read 50 = 100 reads expected
        Assert.InRange(reads.Count, 80, 120);
    }

    [Fact]
    public async Task TestDataGenerator_SimulatedReads_AreValidFastq()
    {
        var gen = new TestDataGenerator(seed: 42);
        var reference = gen.GenerateReference(length: 200);

        await foreach (var r in gen.SimulateReadsAsync(reference, depth: 5, readLength: 30))
        {
            Assert.Equal(30, r.Length);
            Assert.All(r.GetData().ToArray(), c => Assert.Contains(c, new[] { 'A', 'C', 'G', 'T', 'N' }));
        }
    }

    [Fact]
    public void TestDataGenerator_InjectsVariants_AtKnownPositions()
    {
        var gen = new TestDataGenerator(seed: 42);
        var reference = gen.GenerateReference(length: 500);
        var variants = new[]
        {
            new SyntheticVariant { Position = 100, ReferenceAllele = reference[100], AlternateAllele = 'T' },
            new SyntheticVariant { Position = 200, ReferenceAllele = reference[200], AlternateAllele = 'G' }
        };

        var mutated = gen.InjectVariants(reference, variants);

        Assert.Equal('T', mutated[100]);
        Assert.Equal('G', mutated[200]);
        // Surrounding bases unchanged
        Assert.Equal(reference[99], mutated[99]);
        Assert.Equal(reference[101], mutated[101]);
    }

    [Fact]
    public async Task TestDataGenerator_GenerateVariantSet_ReturnsInjectedVariants()
    {
        var gen = new TestDataGenerator(seed: 42);
        var (reference, variants, reads) = await gen.GenerateVariantSetAsync(
            referenceLength: 500, variantCount: 5, readDepth: 10, readLength: 50);

        Assert.Equal(5, variants.Count);
        Assert.NotEmpty(reads);
    }
}
