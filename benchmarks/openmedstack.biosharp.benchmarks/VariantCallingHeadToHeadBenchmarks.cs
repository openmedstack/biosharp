namespace OpenMedStack.BioSharp.Benchmarks;

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using OpenMedStack.BioSharp.Calculations.Alignment;
using OpenMedStack.BioSharp.Calculations.BurrowsWheeler;
using OpenMedStack.BioSharp.Model;

/// <summary>
/// Head-to-head variant-calling benchmarks: BioSharp vs. <c>freebayes</c>.
///
/// Fairness design
/// ────────────────
/// Both callers receive the same pre-sorted BAM file and the same reference FASTA.
/// The BAM is built once in <c>[GlobalSetup]</c> using <c>bwa mem | samtools sort</c>
/// so the aligned input is identical for all callers.
///
/// • BioSharp reads the BAM in-process via its own BAM reader.
/// • FreeBayes is invoked as a subprocess.  Its variant output is piped to /dev/null so
///   serialisation cost is symmetric with BioSharp (which returns in-memory objects).
///
/// Both callers therefore pay:
///   BAM reading + seeding/indexing + alignment scoring + genotype calling.
///
/// BioSharp does NOT pay: process startup or shared-library load.
/// FreeBayes does NOT pay: JIT compilation (it is pre-compiled C++).
///
/// The delta exposes the native vs. managed performance gap and shows where
/// BioSharp has optimisation potential.
/// </summary>
[MemoryDiagnoser]
[MarkdownExporterAttribute.GitHub]
[SimpleJob(RuntimeMoniker.Net10_0, warmupCount: 3, iterationCount: 10)]
public class VariantCallingHeadToHeadBenchmarks
{
    private const int ThreadCount = 10;

    private Sequence _reference = null!;
    private ReferenceAlignmentContext _referenceContext = null!;
    private FmIndexSeeder _fmSeeder = null!;
    private string _referencePath = null!;
    private string _bamPath = null!;
    private string _tempDir = null!;
    private bool _freeBayesAvailable;
    private bool _samtoolsAvailable;
    private string? _preatorDll;
    private string? _preatorPublishError;

    private static string? FindRepoRoot()
    {
        var startingPoints = new[]
            {
                AppContext.BaseDirectory,
                Directory.GetCurrentDirectory()
            }
            .Distinct(StringComparer.Ordinal)
            .Select(path => new DirectoryInfo(Path.GetFullPath(path)));

        foreach (var start in startingPoints)
        {
            for (DirectoryInfo? current = start; current != null; current = current.Parent)
            {
                var candidate = current.FullName;
                if (File.Exists(Path.Combine(candidate, "openmedstack-biosharp.sln")) &&
                    Directory.Exists(Path.Combine(candidate, "data")))
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    private string? DataPath(string file)
    {
        var repoRoot = FindRepoRoot();
        return repoRoot == null ? null : Path.Combine(repoRoot, "data", file);
    }

    [GlobalSetup]
    public void Setup()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"biosharp-vc-bench-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        _freeBayesAvailable = ExternalProcess.IsAvailable("freebayes");
        _samtoolsAvailable = ExternalProcess.IsAvailable("samtools");

        // Publish the preator binary once (shared across benchmark classes in this process).
        _preatorDll = PreatorPublisher.GetPreatorDll();
        _preatorPublishError = PreatorPublisher.GetPublishError();

        // ── Try to use the real test data first ────────────────────────────
        var realBam = DataPath("small_test_sorted.bam");
        var realFasta = DataPath("small_test.fasta");

        if (!string.IsNullOrEmpty(realBam) && !string.IsNullOrEmpty(realFasta) && File.Exists(realBam) &&
            File.Exists(realFasta))
        {
            _bamPath = realBam;
            _referencePath = realFasta;

            // Load reference in-memory for BioSharp
            _reference = LoadFasta(_referencePath);
            InitializeReusableReferenceState();
            return;
        }

        // ── Fallback: generate purely synthetic data ──────────────────────
        const string bases = "ACGT";
        var random = new Random(42);
        var refBuf = new char[10_000];
        for (var i = 0; i < refBuf.Length; i++)
        {
            refBuf[i] = bases[random.Next(4)];
        }

        // Plant several SNPs and indels
        refBuf[2000] = 'A';
        refBuf[5000] = 'T';
        refBuf[8000] = 'G';
        var refStr = new string(refBuf);
        _reference = new Sequence("chrSynth", refStr.AsMemory(), new string('I', refStr.Length).AsMemory());
        // Write FASTA reference
        _referencePath = Path.Combine(_tempDir, "ref.fa");
        WriteFasta(_referencePath, "chrSynth", refStr);

        // Generate synthetic reads and write FASTQ
        const int numReads = 500;
        const int readLen = 150;
        var readsPath = Path.Combine(_tempDir, "reads.fq");
        using (var w = new StreamWriter(readsPath))
        {
            for (var i = 0; i < numReads; i++)
            {
                var offset = random.Next(refBuf.Length - readLen);
                var readBuf = new char[readLen];
                refStr.CopyTo(offset, readBuf, 0, readLen);
                // Introduce 1 SNP every 10 reads at position 75 to generate variants
                if (i % 10 == 0)
                {
                    readBuf[75] = readBuf[75] == 'A' ? 'T' : 'A';
                }

                var quals = new string('I', readLen);
                w.WriteLine($"@r{i}");
                w.WriteLine(new string(readBuf));
                w.WriteLine("+");
                w.WriteLine(quals);
            }
        }

        // Produce BAM via bwa + samtools (if available), otherwise BioSharp cannot use LoadBam
        _bamPath = string.Empty;
        if (ExternalProcess.IsAvailable("bwa") && _samtoolsAvailable)
        {
            ExternalProcess.Run("bwa", $"index {_referencePath}", _tempDir, 60_000);
            var tmpSam = Path.Combine(_tempDir, "aln.sam");
            ExternalProcess.Run("bwa", $"mem -t {ThreadCount} {_referencePath} {readsPath} -o {tmpSam}", _tempDir,
                60_000);
            _bamPath = Path.Combine(_tempDir, "aln.sorted.bam");
            ExternalProcess.Run("samtools",
                $"sort {tmpSam} -o {_bamPath}", _tempDir, 60_000);
            ExternalProcess.Run("samtools", $"index {_bamPath}", _tempDir, 30_000);
        }

        InitializeReusableReferenceState();
        if (string.IsNullOrEmpty(_bamPath))
        {
            throw new InvalidOperationException(
                "Variant-calling head-to-head benchmarks require a shared BAM input. Provide `data/small_test_sorted.bam` + `data/small_test.fasta`, or install bwa and samtools so the synthetic BAM can be generated.");
        }
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    // ── BioSharp benchmarks ──────────────────────────────────────────────────

    /// <summary>
    /// BioSharp: full variant-calling pipeline via a pre-sorted BAM file.
    /// Uses the hash-map k-mer seeder (default).
    /// </summary>
    [Benchmark(Baseline = true, Description = "BioSharp-VarCall-BAM-HashMap")]
    [BenchmarkCategory("VariantCalling", "BioSharp")]
    public async Task<int> BioSharp_VariantCalling_HashMap_FromBam()
    {
        if (string.IsNullOrEmpty(_bamPath))
        {
            throw new InvalidOperationException(
                "Shared BAM input was not prepared; the BioSharp variant-calling benchmark would otherwise measure nothing.");
        }

        var pipeline = CreatePipeline();
        await pipeline.LoadBam(_bamPath).ConfigureAwait(false);
        return pipeline.BuildResult().Variants.Length;
    }

    /// <summary>
    /// BioSharp: full variant-calling pipeline via a pre-sorted BAM file.
    /// Uses the FM-index seeder (BWT backward search).
    /// </summary>
    [Benchmark(Description = "BioSharp-VarCall-BAM-FmIndex")]
    [BenchmarkCategory("VariantCalling", "BioSharp")]
    public async Task<int> BioSharp_VariantCalling_FmIndex_FromBam()
    {
        if (string.IsNullOrEmpty(_bamPath))
        {
            throw new InvalidOperationException(
                "Shared BAM input was not prepared; the BioSharp FM-index variant-calling benchmark would otherwise measure nothing.");
        }

        var pipeline = CreatePipeline(useBamAlignmentCoordinates: false);
        pipeline.Seeder = _fmSeeder;
        await pipeline.LoadBam(_bamPath).ConfigureAwait(false);
        return pipeline.BuildResult().Variants.Length;
    }

    /// <summary>
    /// BioSharp: parse the same BAM and call raw per-read variants without merge/filter post-processing.
    /// The return value is records accepted plus raw calls so the benchmark cannot silently do no work.
    /// </summary>
    [Benchmark(Description = "BioSharp-VarCall-BAM-Profile-LoadAndCall")]
    [BenchmarkCategory("VariantCalling", "BioSharp", "Profile")]
    public async Task<int> BioSharp_VariantCalling_Profile_LoadAndCall_FromBam()
    {
        var pipeline = CreatePipeline();
        await pipeline.LoadBam(_bamPath).ConfigureAwait(false);
        return pipeline.LastBamLoadProfile.RecordsAccepted + pipeline.BuildResult().Metrics.VariantsCalled;
    }

    /// <summary>
    /// BioSharp: full BAM call plus merge/filter stage profile.
    /// </summary>
    [Benchmark(Description = "BioSharp-VarCall-BAM-Profile-MergeFilter")]
    [BenchmarkCategory("VariantCalling", "BioSharp", "Profile")]
    public async Task<int> BioSharp_VariantCalling_Profile_MergeFilter_FromBam()
    {
        var pipeline = CreatePipeline();
        await pipeline.LoadBam(_bamPath).ConfigureAwait(false);
        var result = pipeline.BuildResult();
        return result.Metrics.VariantsCalled + result.Metrics.VariantsFinal;
    }

    /// <summary>
    /// BioSharp: full pipeline including graph-based SV detection.
    /// This activates De Bruijn graph assembly and bubble finding.
    /// </summary>
    [Benchmark(Description = "BioSharp-VarCall-BAM-WithSV")]
    [BenchmarkCategory("VariantCalling", "BioSharp", "SV")]
    public async Task<int> BioSharp_VariantCalling_WithSV_FromBam()
    {
        if (string.IsNullOrEmpty(_bamPath))
        {
            throw new InvalidOperationException(
                "Shared BAM input was not prepared; the BioSharp SV variant-calling benchmark would otherwise measure nothing.");
        }

        var pipeline = CreatePipeline(
            enableGraphSvDetection: true,
            enableSoftClipRealignment: true,
            useBamAlignmentCoordinates: false);
        await pipeline.LoadBam(_bamPath).ConfigureAwait(false);
        return pipeline.BuildResult().Variants.Length;
    }

    // ── External tool benchmarks ─────────────────────────────────────────────

    /// <summary>
    /// FreeBayes: call variants on the same BAM using a subprocess.
    /// Includes process start, BAM reading, and variant calling.
    /// VCF output is piped to /dev/null — same as BioSharp not serialising to disk.
    /// </summary>
    [Benchmark(Description = "freebayes (subprocess)")]
    [BenchmarkCategory("VariantCalling", "External")]
    public int FreeBayes_VariantCalling()
    {
        if (!_freeBayesAvailable)
        {
            throw new InvalidOperationException(
                "freebayes is not installed on PATH. The variant-calling head-to-head external benchmark cannot be run apples-to-apples on this machine.");
        }

        return RunVcfProducingCommandAndCountVariants(
            command:
            $"freebayes -f \"{_referencePath}\" \"{_bamPath}\"",
            outputPrefix: "freebayes");
    }

    /// <summary>
    /// SAMtools mpileup | bcftools call: lightweight pileup-based variant calling
    /// as a fast external baseline. Represents the simplest CLI variant calling approach.
    /// </summary>
    [Benchmark(Description = "samtools-mpileup|bcftools (subprocess)")]
    [BenchmarkCategory("VariantCalling", "External")]
    public int SamtoolsMpileup_BcftoolsCall()
    {
        if (!_samtoolsAvailable || !ExternalProcess.IsAvailable("bcftools"))
        {
            throw new InvalidOperationException(
                "samtools and bcftools are required on PATH for the mpileup|bcftools head-to-head benchmark.");
        }

        return RunVcfProducingCommandAndCountVariants(
            command:
            $"samtools view -u \"{_bamPath}\" | bcftools mpileup -Ou -f \"{_referencePath}\" - | bcftools call -m -v -Ov",
            outputPrefix: "samtools_bcftools");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// preator (compiled subprocess): run <c>preator variantcall</c> on the same BAM + reference.
    /// Uses the published, pre-compiled preator binary — process start, .NET runtime load, BAM
    /// reading, and variant calling are all included.  Directly comparable to freebayes/bcftools.
    ///
    /// Pipeline options are aligned to the in-process BioSharp benchmarks so the
    /// algorithm comparison is apples-to-apples:
    ///   --min-alignment-score 20   → matches <see cref="CreatePipeline"/> default
    ///   --min-alternate-fraction 0.15
    ///   --min-alternate-observation-count 2
    /// </summary>
    [Benchmark(Description = "preator-variantcall (subprocess)")]
    [BenchmarkCategory("VariantCalling", "Preator")]
    public int Preator_VariantCall_Subprocess()
    {
        if (_preatorDll == null)
        {
            throw new InvalidOperationException(
                $"preator binary is not available: {_preatorPublishError}");
        }

        if (string.IsNullOrEmpty(_bamPath))
        {
            throw new InvalidOperationException(
                "Shared BAM input was not prepared; the preator variant-call subprocess benchmark " +
                "cannot run without a BAM file.");
        }

        var outDir = Path.Combine(_tempDir, $"preator_vc_{Guid.NewGuid():N}");
        Directory.CreateDirectory(outDir);
        try
        {
            var runningInContainer = Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") == "true";
            var exit = runningInContainer
                ? ExternalProcess.Run(
                    "/app/preator/preator",
                    $"variantcall" +
                    $" --bam \"{_bamPath}\"" +
                    $" --reference \"{_referencePath}\"" +
                    $" --output \"{outDir}\"" +
                    $" --output-prefix variants" +
                    $" --min-alignment-score 20" +
                    $" --min-alternate-fraction 0.15" +
                    $" --min-alternate-observation-count 2" +
                    $" -p {ThreadCount}",
                    _tempDir,
                    300_000)
                : ExternalProcess.Run(
                    "dotnet",
                    $"\"{_preatorDll}\" variantcall" +
                    $" --bam \"{_bamPath}\"" +
                    $" --reference \"{_referencePath}\"" +
                    $" --output \"{outDir}\"" +
                    $" --output-prefix variants" +
                    $" --min-alignment-score 20" +
                    $" --min-alternate-fraction 0.15" +
                    $" --min-alternate-observation-count 2" +
                    $" -p {ThreadCount}",
                    _tempDir,
                    300_000);
            if (exit != 0)
            {
                throw new InvalidOperationException($"preator variantcall exited with code {exit}.");
            }

            var vcfPath = Path.Combine(outDir, "variants.vcf");
            if (!File.Exists(vcfPath))
            {
                throw new InvalidOperationException(
                    "preator variantcall completed but produced no VCF output.");
            }

            return File.ReadLines(vcfPath)
                .Count(line => !string.IsNullOrWhiteSpace(line) && line[0] != '#');
        }
        finally
        {
            if (Directory.Exists(outDir))
            {
                Directory.Delete(outDir, recursive: true);
            }
        }
    }


    private VariantCallingPipeline CreatePipeline(
        bool enableGraphSvDetection = false,
        bool enableSoftClipRealignment = false,
        bool useBamAlignmentCoordinates = true)
    {
        return new VariantCallingPipeline(_referenceContext, _reference.Id,
            new VariantCallingPipeline.PipelineOptions
            {
                KmerSize = 31,
                MinAlignmentScore = 20,
                MinAlternateFraction = 0.15,
                MinAlternateObservationCount = 2,
                EnableGraphSvDetection = enableGraphSvDetection,
                EnableSoftClipRealignment = enableSoftClipRealignment,
                UseBamAlignmentCoordinates = useBamAlignmentCoordinates,
                DegreeOfParallelism = ThreadCount,
                CandidateAlignmentDegreeOfParallelism = ThreadCount,
                ParallelCandidateWindowThreshold = 1
            });
    }

    private void InitializeReusableReferenceState()
    {
        _referenceContext = ReferenceAlignmentContext.GetShared(_reference);
        _referenceContext.GetOrCreateIndex(CreateReferenceIndexOptions());
        _fmSeeder = new FmIndexSeeder(_reference);
    }

    private static ReferenceIndex.IndexOptions CreateReferenceIndexOptions()
    {
        return new ReferenceIndex.IndexOptions
        {
            SeedSize = 11,
            WindowPadding = 64,
            MaxCandidateWindowsPerRead = 8,
            MaxSeedHitsPerKmer = 64
        };
    }

    private int RunVcfProducingCommandAndCountVariants(string command, string outputPrefix)
    {
        var outputPath = Path.Combine(_tempDir, $"{outputPrefix}_{Guid.NewGuid():N}.vcf");
        var logPath = Path.Combine(_tempDir, $"{outputPrefix}_{Guid.NewGuid():N}.stderr.log");
        try
        {
            var shellCommand = $"{command} > \"{outputPath}\" 2> \"{logPath}\"";
            var exit = ExternalProcess.Shell(shellCommand, _tempDir);
            if (exit != 0)
            {
                var log = File.Exists(logPath) ? File.ReadAllText(logPath) : string.Empty;
                throw new InvalidOperationException($"External variant caller exited with code {exit}. STDERR: {log}");
            }

            if (!File.Exists(outputPath))
            {
                throw new InvalidOperationException(
                    "External variant caller completed but produced no VCF output file.");
            }

            var recordCount = File.ReadLines(outputPath)
                .Count(line => !string.IsNullOrWhiteSpace(line) && line[0] != '#');
            return recordCount;
        }
        finally
        {
            if (File.Exists(outputPath))
            {
                File.Delete(outputPath);
            }

            if (File.Exists(logPath))
            {
                File.Delete(logPath);
            }
        }
    }

    private static Sequence LoadFasta(string path)
    {
        var lines = File.ReadAllLines(path);
        var id = "unknown";
        var sb = new System.Text.StringBuilder();
        foreach (var line in lines)
        {
            if (line.StartsWith('>'))
            {
                id = line[1..].Split(' ')[0];
                continue;
            }

            sb.Append(line.Trim());
        }

        var s = sb.ToString();
        return new Sequence(id, s.AsMemory(), new string('I', s.Length).AsMemory());
    }

    private static void WriteFasta(string path, string id, string seq)
    {
        using var w = new StreamWriter(path);
        w.WriteLine($">{id}");
        const int lineLen = 80;
        for (var i = 0; i < seq.Length; i += lineLen)
        {
            w.WriteLine(seq.Substring(i, Math.Min(lineLen, seq.Length - i)));
        }
    }
}
