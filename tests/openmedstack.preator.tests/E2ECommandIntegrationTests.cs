namespace OpenMedStack.Preator.Tests;

using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

/// <summary>
/// Integration test that exercises the full E2E pipeline end-to-end using the
/// NC_000913.3 (E. coli K-12) reference and a small set of SRR1770413 FASTQ reads.
/// The test is skipped gracefully when the fixture files are not present in the
/// test output directory (e.g. clean CI without data/).
/// </summary>
public sealed class E2ECommandIntegrationTests : IDisposable
{
    private readonly string _outputDirectory;

    // Fixture files are copied to the test output directory via the .csproj None/CopyToOutputDirectory items.
    private const string ReferencePath = "NC_000913.3.fa.gz";
    private const string FastqPath = "SRR1770413_1.first250.fastq.gz";
    private const string BamPath = "small_test_sorted.bam";

    public E2ECommandIntegrationTests()
    {
        _outputDirectory = Path.Combine(Path.GetTempPath(), $"preator_e2e_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_outputDirectory);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_outputDirectory))
            {
                Directory.Delete(_outputDirectory, recursive: true);
            }
        }
        catch
        {
            // ignore cleanup failures
        }
    }

    [Fact]
    public async Task Run_FastqInput_ProducesVcfAndSummaryFiles()
    {
        if (!File.Exists(ReferencePath) || !File.Exists(FastqPath))
        {
            // Fixture files not available — skip rather than fail.
            return;
        }

        var options = BuildFastqOptions(maxReads: 50);
        var exitCode = await E2ECommand.Run(options, CancellationToken.None);

        Assert.Equal(0, exitCode);
        AssertOutputFilesExist("variants");
    }

    [Fact]
    public async Task Run_FastqInput_VcfContainsValidHeader()
    {
        if (!File.Exists(ReferencePath) || !File.Exists(FastqPath))
        {
            return;
        }

        var prefix = "vcfheader";
        var options = BuildFastqOptions(maxReads: 30, prefix: prefix);
        var exitCode = await E2ECommand.Run(options, CancellationToken.None);

        Assert.Equal(0, exitCode);

        var vcfPath = Path.Combine(_outputDirectory, $"preator-{prefix}.vcf");
        Assert.True(File.Exists(vcfPath), $"VCF not found at {vcfPath}");

        var content = await File.ReadAllTextAsync(vcfPath);
        Assert.Contains("##fileformat=VCF", content);
    }

    [Fact]
    public async Task Run_FastqInput_VcfContainsVariantRecords()
    {
        if (!File.Exists(ReferencePath) || !File.Exists(FastqPath))
        {
            return;
        }

        var prefix = "variants_check";
        var options = BuildFastqOptions(maxReads: 250, prefix: prefix);
        var exitCode = await E2ECommand.Run(options, CancellationToken.None);

        Assert.Equal(0, exitCode);

        var vcfPath = Path.Combine(_outputDirectory, $"preator-{prefix}.vcf");
        Assert.True(File.Exists(vcfPath), $"VCF not found at {vcfPath}");

        var lines = await File.ReadAllLinesAsync(vcfPath);
        var variantCount = lines.Count(l => l.Length > 0 && !l.StartsWith('#'));

        var logPath = Path.Combine(_outputDirectory, $"preator-{prefix}.log.txt");
        var logContent = File.Exists(logPath) ? await File.ReadAllTextAsync(logPath) : "(no log file)";

        Assert.True(variantCount > 0,
            $"VCF contains no variant records (only {lines.Length} lines total, all headers). " +
            $"Log:\n{logContent}");
    }

    [Fact]
    public async Task Run_BamInput_ProducesVcfAndSummaryFiles()
    {
        if (!File.Exists(ReferencePath) || !File.Exists(BamPath))
        {
            return;
        }

        var prefix = "bam";
        var options = new E2EOptions(
            FastqPath: null,
            FastaPath: null,
            BamPath: BamPath,
            OutputDirectory: _outputDirectory,
            OutputPrefix: prefix,
            ReferencePath: ReferencePath,
            Chromosome: null,
            MaxReads: null,
            MinAlignmentScore: 10,
            MinVariantQuality: 30,
            MinAlternateObservationCount: 1,
            MinAlternateFraction: 0.0,
            EnableSoftClipRealignment: true,
            EnableGraphSvDetection: false,
            KmerSize: 15,
            MinGraphCoverage: 5,
            GraphWindowBp: 500,
            MaxCores: 2,
            ReferenceIdContains: null,
            TranscriptDatabasePath: null,
            TranscriptId: null,
            MinQuality: 0f,
            Adapter: null,
            MinLength: 20,
            MaxMismatches: 2,
            MinSeedLen: 19,
            MaxSeedHitsThreshold: 64,
            SeedStep: 1,
            WindowPadding: 64,
            MaxCandidateWindowsPerRead: 8,
            OpticalPixelDistance: 100);

        var exitCode = await E2ECommand.Run(options, CancellationToken.None);

        Assert.Equal(0, exitCode);
        AssertOutputFilesExist(prefix);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private E2EOptions BuildFastqOptions(int maxReads, string prefix = "variants") =>
        new(
            FastqPath: FastqPath,
            FastaPath: null,
            BamPath: null,
            OutputDirectory: _outputDirectory,
            OutputPrefix: prefix,
            ReferencePath: ReferencePath,
            Chromosome: null,
            MaxReads: maxReads,
            MinAlignmentScore: 10,
            MinVariantQuality: 30,
            MinAlternateObservationCount: 1,
            MinAlternateFraction: 0.0,
            EnableSoftClipRealignment: true,
            EnableGraphSvDetection: false,
            KmerSize: 15,
            MinGraphCoverage: 5,
            GraphWindowBp: 500,
            MaxCores: 2,
            ReferenceIdContains: null,
            TranscriptDatabasePath: null,
            TranscriptId: null,
            MinQuality: 0f,
            Adapter: null,
            MinLength: 20,
            MaxMismatches: 2,
            MinSeedLen: 19,
            MaxSeedHitsThreshold: 64,
            SeedStep: 1,
            WindowPadding: 64,
            MaxCandidateWindowsPerRead: 8,
            OpticalPixelDistance: 100);

    private void AssertOutputFilesExist(string prefix)
    {
        var baseName = $"preator-{prefix}";
        Assert.True(File.Exists(Path.Combine(_outputDirectory, $"{baseName}.vcf")),
            "VCF output not created");
        Assert.True(File.Exists(Path.Combine(_outputDirectory, $"{baseName}.tsv")),
            "TSV report not created");
        Assert.True(File.Exists(Path.Combine(_outputDirectory, $"{baseName}.summary.txt")),
            "Summary not created");
        Assert.True(File.Exists(Path.Combine(_outputDirectory, $"{baseName}.log.txt")),
            "Log not created");
    }
}
