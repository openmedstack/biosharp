namespace OpenMedStack.BioSharp.AcceptanceTests.StepDefinitions;

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Calculations.Alignment;
using Io.FastA;
using Io.FastQ;
using Io.Vcf;
using Microsoft.Extensions.Logging.Abstractions;
using Model;
using Reqnroll;
using Xunit;

[Binding]
public sealed class FastaToVcfStepDefinitions
{
    private readonly ScenarioContext _ctx;

    public FastaToVcfStepDefinitions(ScenarioContext ctx) => _ctx = ctx;

    [Given("I load the acceptance fixtures")]
    public async Task GivenILoadTheDerivedAcceptanceFixtures()
    {
        var fixtureDirectory = GetFixtureDirectory();
        var expected = JsonSerializer.Deserialize<Golden>(
            await File.ReadAllTextAsync(Path.Combine(fixtureDirectory, "SRR1770413.first250.expected.json")),
            new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        Assert.NotNull(expected);

        var referenceReader = new FastAReader(NullLogger<FastAReader>.Instance);
        var referencePath = Path.Combine(fixtureDirectory, "NC_000913.3.fa.gz");
        var references = referenceReader.ReadGz(referencePath);
        var reference = await references.FirstAsync();

        var fastqReader = new FastQReader(NullLogger.Instance);
        var r1Path = Path.Combine(fixtureDirectory, "SRR1770413_1.first250.fastq.gz");
        var r2Path = Path.Combine(fixtureDirectory, "SRR1770413_2.first250.fastq.gz");

        var pairs = new List<(Sequence R1, Sequence R2)>();
        await foreach (var pair in fastqReader.ReadPaired(r1Path, r2Path, strict: true))
        {
            pairs.Add(pair);
        }

        _ctx["Expected"] = expected;
        _ctx["Reference"] = reference;
        _ctx["Pairs"] = pairs;
    }

    [When("I validate the reference excerpt")]
    public void WhenIValidateTheReferenceExcerpt()
    {
        var expected = GetExpected();
        var reference = (Sequence)_ctx["Reference"];
        var referenceText = new string(reference.GetData().Span);
        var startIndex = expected.Region.Start - 1;
        var length = expected.Region.End - expected.Region.Start + 1;
        var actual = referenceText.Substring(startIndex, length);

        Assert.Equal(expected.ReferenceId, reference.Id);
        Assert.Equal(expected.ReferenceLength, reference.Length);
        Assert.Equal(expected.Region.Sequence, actual);
    }

    [When("I run the BioSharp slice pipeline to a BGZF-compressed VCF")]
    public async Task WhenIRunTheBioSharpSlicePipelineToABgzfCompressedVcf()
    {
        await RunSlicePipeline(
            new VariantCallingPipeline.PipelineOptions
            {
                MinAlignmentScore = 10,
                MinVariantQuality = 30,
                EnableSoftClipRealignment = true,
                EnableGraphSvDetection = false,
                DegreeOfParallelism = 1
            });
    }

    [When("I run the BioSharp slice pipeline with freebayes-like read acceptance")]
    public async Task WhenIRunTheBioSharpSlicePipelineWithFreebayesLikeReadAcceptance()
    {
        await RunSlicePipeline(
            new VariantCallingPipeline.PipelineOptions
            {
                MinAlignmentScore = 10,
                MinVariantQuality = 30,
                MinAlternateObservationCount = 2,
                MinAlternateFraction = 0.20,
                EnableSoftClipRealignment = true,
                EnableGraphSvDetection = false,
                DegreeOfParallelism = 1
            });
    }

    [Then("the freebayes-like acceptance should suppress the sparse slice calls")]
    public void ThenTheFreebayesLikeAcceptanceShouldSuppressTheSparseSliceCalls()
    {
        var result = (VariantCallingPipeline.PipelineResult)_ctx["Result"];
        var records = (string[])_ctx["VcfRecords"];

        Assert.Equal(2776, result.Metrics.VariantsCalled);
        Assert.Equal(0, result.Metrics.VariantsFinal);
        Assert.Empty(result.Variants);
        Assert.Empty(records);
    }

    private async Task RunSlicePipeline(VariantCallingPipeline.PipelineOptions options)
    {
        var expected = GetExpected();
        var reference = (Sequence)_ctx["Reference"];
        var pairs = (List<(Sequence R1, Sequence R2)>)_ctx["Pairs"];
        var flattenedReads = pairs.SelectMany(pair => new[] { pair.R1, pair.R2 }).ToArray();

        var pipeline = new VariantCallingPipeline(reference, expected.Region.Chromosome, options);

        await pipeline.LoadFastQ(AsReads(flattenedReads), null, CancellationToken.None);
        var result = pipeline.BuildResult();

        var tempDirectory = Path.Combine(Path.GetTempPath(), $"biosharp-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);
        var vcfPath = Path.Combine(tempDirectory, ".vcf.gz");
        await VcfWriter.Write(vcfPath, result.Variants, expected.Region.Chromosome, reference.Length,
            compress: true);
        await TabixIndexWriter.Write(vcfPath);

        var vcfText = await ReadBgzfText(vcfPath);
        var records = vcfText
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Where(line => !line.StartsWith('#'))
            .ToArray();

        _ctx["TempDirectory"] = tempDirectory;
        _ctx["FlattenedReads"] = flattenedReads;
        _ctx["Result"] = result;
        _ctx["VcfPath"] = vcfPath;
        _ctx["VcfText"] = vcfText;
        _ctx["VcfRecords"] = records;
        _ctx["VcfSha256"] = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(vcfText)));
    }

    [Then("the FASTQ slice should contain the expected concordant read pairs")]
    public void ThenTheFastqSliceShouldContainTheExpectedConcordantReadPairs()
    {
        var expected = GetExpected();
        var pairs = (List<(Sequence R1, Sequence R2)>)_ctx["Pairs"];
        var flattenedReads = (Sequence[])_ctx["FlattenedReads"];

        Assert.Equal(expected.PairCount, pairs.Count);
        Assert.Equal(expected.FlattenedReadCount, flattenedReads.Length);
        Assert.Equal("SRR1770413.1 1/1", pairs[0].R1.Id);
        Assert.Equal("SRR1770413.1 1/2", pairs[0].R2.Id);
    }

    [Then("the slice metrics and VCF should match the frozen golden summary")]
    public void ThenTheSliceMetricsAndVcfShouldMatchTheFrozenGoldenSummary()
    {
        var expected = GetExpected();
        var result = (VariantCallingPipeline.PipelineResult)_ctx["Result"];
        var flattenedReads = (Sequence[])_ctx["FlattenedReads"];
        var records = (string[])_ctx["VcfRecords"];
        var vcfSha256 = (string)_ctx["VcfSha256"];

        Assert.Equal(expected.FlattenedReadCount, flattenedReads.Length);
        Assert.Equal(expected.MappedReadCount, result.Metrics.ReadsProcessed);
        Assert.Equal(expected.MappedReadCount, result.Metrics.ReadsMapped);
        Assert.Equal(expected.SoftClipRealignedReadCount, result.Metrics.ReadsRealigned);
        Assert.Equal(expected.RawVariantCount, result.Metrics.VariantsCalled);
        Assert.Equal(expected.MergedVariantCount, result.Metrics.VariantsFinal);
        Assert.Equal(expected.StructuralVariantCount, result.Metrics.StructuralVariants);
        Assert.Equal(expected.RecordCount, records.Length);
        Assert.Equal(expected.FirstRecord, records.First());
        Assert.Equal(expected.LastRecord, records.Last());
        Assert.Equal(expected.VcfSha256, vcfSha256);

        var counts = result.Variants
            .GroupBy(GetVariantType)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        Assert.Equal(expected.VariantTypeCounts, counts);
    }

    [Then("the tabix query for the golden window should return the expected variants")]
    public async Task ThenTheTabixQueryForTheGoldenWindowShouldReturnTheExpectedVariants()
    {
        var expected = GetExpected();
        var vcfPath = (string)_ctx["VcfPath"];
        var regionQuery = expected.RegionQuery;

        var count = 0;
        await foreach (var _ in VcfFileReader.ReadRegion(
            vcfPath,
            regionQuery.Chromosome,
            regionQuery.Start,
            regionQuery.End))
        {
            count++;
        }

        Assert.Equal(regionQuery.Count, count);
    }

    [AfterScenario]
    public void AfterScenario()
    {
        if (_ctx.TryGetValue("TempDirectory", out var value) &&
            value is string tempDirectory &&
            Directory.Exists(tempDirectory))
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    private static async IAsyncEnumerable<Sequence> AsReads(
        IEnumerable<Sequence> reads,
        [System.Runtime.CompilerServices.EnumeratorCancellation]
        CancellationToken cancellationToken = default)
    {
        foreach (var read in reads)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return read;
        }

        await Task.CompletedTask;
    }

    private static string GetVariantType(LocalVariantResult variant)
    {
        if (variant.IsStructuralVariant)
        {
            return variant.SvType?.ToString() ?? "SV";
        }

        if (variant.IsInsertion)
        {
            return "Insertion";
        }

        if (variant.IsDeletion)
        {
            return "Deletion";
        }

        return "SNP";
    }

    private static async Task<string> ReadBgzfText(string path)
    {
        await using var file = File.OpenRead(path);
        await using var gzip = new GZipStream(file, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip, Encoding.UTF8);
        return await reader.ReadToEndAsync();
    }

    private static string GetFixtureDirectory()
    {
        return Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "..",
            "data"));
    }

    private Golden GetExpected() => (Golden)_ctx["Expected"];

    private sealed class Golden
    {
        public string ReferenceId { get; init; } = string.Empty;

        public int ReferenceLength { get; init; }

        public Region Region { get; init; } = new();

        public int PairCount { get; init; }

        public int FlattenedReadCount { get; init; }

        public int MappedReadCount { get; init; }

        public int SoftClipRealignedReadCount { get; init; }

        public int RawVariantCount { get; init; }

        public int MergedVariantCount { get; init; }

        public int StructuralVariantCount { get; init; }

        public Dictionary<string, int> VariantTypeCounts { get; init; } = new(StringComparer.Ordinal);

        public string VcfSha256 { get; init; } = string.Empty;

        public int RecordCount { get; init; }

        public string FirstRecord { get; init; } = string.Empty;

        public string LastRecord { get; init; } = string.Empty;

        public RegionQuery RegionQuery { get; init; } = new();
    }

    private sealed class Region
    {
        public string Chromosome { get; init; } = string.Empty;

        public int Start { get; init; }

        public int End { get; init; }

        public string Sequence { get; init; } = string.Empty;
    }

    private sealed class RegionQuery
    {
        public string Chromosome { get; init; } = string.Empty;

        public int Start { get; init; }

        public int End { get; init; }

        public int Count { get; init; }
    }
}

internal static class AsyncEnumerableExtensions
{
    public static async Task<T> FirstAsync<T>(this IAsyncEnumerable<T> source)
    {
        await foreach (var item in source)
        {
            return item;
        }

        throw new InvalidOperationException("The sequence was empty.");
    }
}
