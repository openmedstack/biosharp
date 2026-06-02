namespace OpenMedStack.BioSharp.Benchmarks;

using System;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using OpenMedStack.Preator;

/// <summary>
/// End-to-end pipeline head-to-head benchmarks.
///
/// Compares three execution strategies for the complete FASTQ → VCF pipeline
/// on the same input data with equivalent parameters:
///
///   1. <b>E2E in-process</b>   — <see cref="E2ECommand.Run"/> called directly;
///                                 warm JIT, no process-start overhead.
///   2. <b>E2E subprocess</b>   — the published <c>preator e2e</c> binary invoked as
///                                 a separate process; includes .NET runtime startup and JIT.
///   3. <b>External tools</b>   — the same pipeline implemented with standard
///                                 bioinformatics CLI tools chained via temp files:
///                                 <c>bwa mem</c> → <c>samtools sort</c> →
///                                 <c>samtools markdup</c> → <c>freebayes</c>
///
/// Fairness design
/// ────────────────
/// • All three strategies receive identical input: the same gzipped E. coli FASTQ
///   and the same gzipped reference FASTA from the repository's <c>data/</c> directory.
/// • Parameters are matched as closely as the external tools allow:
///     - <c>bwa mem -k 19 -T 10</c>   ↔  MinSeedLen=19, MinAlignmentScore=10
///     - <c>samtools markdup</c>        ↔  DuplicateMarker with OpticalPixelDistance=100
///     - <c>freebayes --min-base-quality 0 --min-alternate-fraction 0 --min-alternate-count 1</c>
///       + post-filter QUAL ≥ 30
///                                      ↔  MinVariantQuality=30 (variant-level), MinAlternateFraction=0, MinAlternateObservationCount=1
///       NOTE: freebayes --min-base-quality is a per-base FASTQ quality filter; BioSharp uses
///       alignment-score-derived variant quality instead.  The chains are structurally equivalent
///       (align → mark duplicates → call variants) but the algorithms differ fundamentally, so
///       returned variant counts are NOT directly comparable — the benchmark measures time only.
/// • The BWA reference index is built once in <c>[GlobalSetup]</c> — not benchmarked —
///   matching BioSharp's FM-index which is also pre-built in setup.
/// • Each iteration writes outputs to a unique temp subdirectory so iterations are independent.
///
/// What the numbers reveal
/// ────────────────────────
/// • Gap between (1) and (2) = .NET runtime startup + JIT cost for preator.
/// • Gap between (1) and (3) = cost of inter-process file I/O and external tool startup.
/// • If (1) ≈ (3): BioSharp's in-process pipeline matches the external toolchain wall-clock.
/// • If (1) ≪ (3): BioSharp's in-process pipeline is faster (no temp-file overhead).
/// </summary>
[MemoryDiagnoser]
[MarkdownExporterAttribute.GitHub]
[SimpleJob(RuntimeMoniker.Net10_0, warmupCount: 2, iterationCount: 5)]
public class E2eHeadToHeadBenchmarks
{
    // ── Benchmark parameter ──────────────────────────────────────────────────

    /// <summary>Degree of parallelism for BioSharp and external tools alike.</summary>
    [Params(1, 4)]
    public int Parallelism { get; set; }

    // ── Setup state ──────────────────────────────────────────────────────────

    private string _tempDir = null!;

    // Paths to the compressed source files (used by BioSharp and preator subprocess)
    private string _referenceFaGzPath = null!;
    private string _fastqGzPath = null!;

    // Decompressed files (used by BWA/samtools/freebayes which do not support .gz input)
    private string _referenceFaPath = null!;
    private string _fastqPath = null!;

    // BioSharp E2E options (reused each iteration; OutputDirectory and OutputPrefix are
    // overridden per iteration so outputs don't collide)
    private E2EOptions _e2eOptionsTemplate = null!;

    // Tool availability
    private bool _bwaAvailable;
    private bool _bwaMem2Available;
    private bool _samtoolsAvailable;
    private bool _freebayesAvailable;

    // Published preator binary
    private string? _preatorDll;
    private string? _preatorPublishError;

    private static string? FindRepoRoot()
    {
        // Support BIOSHARP_DATA_PATH env-var for Docker/CI where the .sln is not present at runtime.
        var envDataPath = Environment.GetEnvironmentVariable("BIOSHARP_DATA_PATH");
        if (!string.IsNullOrEmpty(envDataPath) && Directory.Exists(envDataPath))
        {
            return Directory.GetParent(envDataPath)?.FullName;
        }

        foreach (var start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            for (var dir = new DirectoryInfo(Path.GetFullPath(start)); dir != null; dir = dir.Parent)
            {
                if (File.Exists(Path.Combine(dir.FullName, "openmedstack-biosharp.sln")) &&
                    Directory.Exists(Path.Combine(dir.FullName, "data")))
                {
                    return dir.FullName;
                }
            }
        }

        // Container fallback: data/ is mounted at /app/data without the .sln file.
        if (Directory.Exists("/app/data"))
        {
            return "/app";
        }

        return null;
    }

    [GlobalSetup]
    public void Setup()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"biosharp-e2e-bench-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        // ── Locate fixture files ─────────────────────────────────────────────
        var repoRoot = FindRepoRoot()
            ?? throw new InvalidOperationException(
                "Cannot locate repository root (expected openmedstack-biosharp.sln + data/ directory). " +
                "Run the benchmark from within the repository tree.");

        var dataDir = Path.Combine(repoRoot, "data");
        var srcReference = Path.Combine(dataDir, "NC_000913.3.fa.gz");
        var srcFastq = Path.Combine(dataDir, "SRR1770413_1.first250.fastq.gz");

        if (!File.Exists(srcReference))
        {
            throw new FileNotFoundException(
                $"Reference FASTA not found: {srcReference}. " +
                "Place NC_000913.3.fa.gz in the data/ directory to run this benchmark.", srcReference);
        }

        if (!File.Exists(srcFastq))
        {
            throw new FileNotFoundException(
                $"FASTQ reads not found: {srcFastq}. " +
                "Place SRR1770413_1.first250.fastq.gz in the data/ directory to run this benchmark.",
                srcFastq);
        }

        _referenceFaGzPath = srcReference;
        _fastqGzPath = srcFastq;

        // ── Decompress for external tools ────────────────────────────────────
        // BWA and freebayes do not support gzipped FASTA/FASTQ as direct input
        // (they require plain text or piped decompression).  We decompress once
        // in setup so that decompression cost is not charged to each iteration.
        _referenceFaPath = Path.Combine(_tempDir, "reference.fa");
        DecompressGz(srcReference, _referenceFaPath);

        _fastqPath = Path.Combine(_tempDir, "reads.fq");
        DecompressGz(srcFastq, _fastqPath);

        // ── Check external tool availability ─────────────────────────────────
        _bwaAvailable = ExternalProcess.IsAvailable("bwa");
        _bwaMem2Available = ExternalProcess.IsAvailable("bwa-mem2");
        _samtoolsAvailable = ExternalProcess.IsAvailable("samtools");
        _freebayesAvailable = ExternalProcess.IsAvailable("freebayes");

        // ── Build BWA/BWA-MEM2 reference index once (not part of benchmark) ──
        if (_bwaAvailable)
        {
            ExternalProcess.Run("bwa", $"index \"{_referenceFaPath}\"", _tempDir, timeoutMs: 120_000);
        }

        if (_bwaMem2Available)
        {
            ExternalProcess.Run("bwa-mem2", $"index \"{_referenceFaPath}\"", _tempDir, timeoutMs: 120_000);
        }

        // ── Publish preator once ──────────────────────────────────────────────
        _preatorDll = PreatorPublisher.GetPreatorDll();
        _preatorPublishError = PreatorPublisher.GetPublishError();

        // ── Build E2E options template ────────────────────────────────────────
        // OutputDirectory and OutputPrefix are replaced per iteration.
        _e2eOptionsTemplate = new E2EOptions(
            FastqPath: _fastqGzPath,
            FastaPath: null,
            BamPath: null,
            OutputDirectory: _tempDir,        // placeholder — overridden per iteration
            OutputPrefix: "variants",
            ReferencePath: _referenceFaGzPath,
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
            MaxCores: Parallelism,
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
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    // ── Benchmark 1: BioSharp E2E in-process ─────────────────────────────────

    /// <summary>
    /// BioSharp E2E pipeline called in-process.
    ///
    /// Runs the complete FASTQ → FM-index alignment → duplicate marking →
    /// variant calling → VCF write pipeline inside the benchmark process.
    /// JIT is warm after the first warmup iteration; no process-start overhead.
    ///
    /// This is the fastest expected path and serves as the baseline.
    /// </summary>
    [Benchmark(Baseline = true, Description = "preator-e2e (in-process)")]
    [BenchmarkCategory("E2E", "BioSharp")]
    public async Task<int> E2E_BioSharp_InProcess()
    {
        var outDir = Path.Combine(_tempDir, $"inproc_{Guid.NewGuid():N}");
        Directory.CreateDirectory(outDir);
        try
        {
            var options = _e2eOptionsTemplate with
            {
                OutputDirectory = outDir,
                OutputPrefix = "variants",
                MaxCores = Parallelism,
            };
            var exit = await E2ECommand.Run(options, CancellationToken.None).ConfigureAwait(false);
            if (exit != 0)
            {
                throw new InvalidOperationException($"E2ECommand.Run returned exit code {exit}.");
            }

            return CountVcfVariants(Path.Combine(outDir, "preator-variants.vcf"));
        }
        finally
        {
            if (Directory.Exists(outDir))
            {
                Directory.Delete(outDir, recursive: true);
            }
        }
    }

    // ── Benchmark 2: preator e2e subprocess ─────────────────────────────────

    /// <summary>
    /// BioSharp E2E pipeline as a compiled subprocess.
    ///
    /// Invokes the published <c>preator e2e</c> binary with identical parameters.
    /// Includes .NET runtime startup, JIT compilation, FM-index construction, and
    /// the full pipeline — directly comparable to the external tool chain.
    ///
    /// The gap between this and the in-process benchmark reveals the real cost of
    /// shipping a .NET pipeline as a CLI tool vs. embedding it as a library.
    /// </summary>
    [Benchmark(Description = "preator-e2e (subprocess)")]
    [BenchmarkCategory("E2E", "Preator")]
    public int E2E_Preator_Subprocess()
    {
        if (_preatorDll == null)
        {
            throw new InvalidOperationException(
                $"preator binary is not available: {_preatorPublishError}");
        }

        var outDir = Path.Combine(_tempDir, $"subprocess_{Guid.NewGuid():N}");
        Directory.CreateDirectory(outDir);
        try
        {
            var args =
                $"e2e" +
                $" --reference \"{_referenceFaGzPath}\"" +
                $" --fastq \"{_fastqGzPath}\"" +
                $" --output \"{outDir}\"" +
                $" --output-prefix variants" +
                $" --min-alignment-score 10" +
                $" --min-variant-quality 30" +
                $" --min-alternate-observation-count 1" +
                $" --min-alternate-fraction 0.0" +
                $" --min-seed-len 19" +
                $" --max-seed-hits 64" +
                $" --window-padding 64" +
                $" --max-windows 8" +
                $" -p {Parallelism}";

            var runningInContainer =
                Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") == "true";
            var exit = runningInContainer
                ? ExternalProcess.Run("/app/preator/preator", args, _tempDir, timeoutMs: 600_000)
                : ExternalProcess.Run("dotnet", $"\"{_preatorDll}\" {args}", _tempDir, timeoutMs: 600_000);

            if (exit != 0)
            {
                throw new InvalidOperationException($"preator e2e exited with code {exit}.");
            }

            return CountVcfVariants(Path.Combine(outDir, "preator-variants.vcf"));
        }
        finally
        {
            if (Directory.Exists(outDir))
            {
                Directory.Delete(outDir, recursive: true);
            }
        }
    }

    // ── Benchmark 3: external tools pipeline (BWA + samtools + freebayes) ────

    /// <summary>
    /// Full FASTQ → VCF pipeline using standard external bioinformatics tools.
    ///
    /// Steps (all external processes, intermediate results on disk):
    ///   1. <c>bwa mem -k 19 -T 10 -t {P}</c> — alignment (equivalent to preator align)
    ///   2. <c>samtools sort -@ {P}</c>          — coordinate sort
    ///   3. <c>samtools markdup</c>               — PCR duplicate marking (equivalent to preator markdup)
    ///   4. <c>samtools index</c>                 — BAI index required by freebayes
    ///   5. <c>freebayes -f ref --min-base-quality 0 --min-alternate-fraction 0 --min-alternate-count 1</c>
    ///      + VCF post-filter: QUAL ≥ 30
    ///                                            — variant calling (equivalent to preator variantcall)
    ///
    /// This is the "gold standard" CLI workflow used by bioinformaticians today.
    /// Its wall-clock time establishes the real-world baseline that in-process
    /// BioSharp must beat to justify adoption.
    ///
    /// Throws when any required tool is absent so the benchmark is never silently skipped.
    /// </summary>
    [Benchmark(Description = "bwa+samtools+freebayes (subprocess chain)")]
    [BenchmarkCategory("E2E", "External")]
    public int ExternalTools_BwaMem_SamtoolsMarkdup_Freebayes()
    {
        return RunExternalPipeline("bwa");
    }

    /// <summary>
    /// Same as <see cref="ExternalTools_BwaMem_SamtoolsMarkdup_Freebayes"/> but using
    /// <c>bwa-mem2</c> for alignment.  BWA-MEM2 uses AVX2/AVX-512 SIMD for the DP
    /// step and is typically 2-3× faster than BWA for alignment-only throughput.
    ///
    /// Including it lets us distinguish between alignment overhead and the downstream
    /// markdup + variant-calling cost.
    /// </summary>
    [Benchmark(Description = "bwa-mem2+samtools+freebayes (subprocess chain)")]
    [BenchmarkCategory("E2E", "External")]
    public int ExternalTools_BwaMem2_SamtoolsMarkdup_Freebayes()
    {
        return RunExternalPipeline("bwa-mem2");
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private int RunExternalPipeline(string bwaExecutable)
    {
        var bwaAvailable = bwaExecutable == "bwa-mem2" ? _bwaMem2Available : _bwaAvailable;
        if (!bwaAvailable)
        {
            throw new InvalidOperationException(
                $"{bwaExecutable} is not installed on PATH. " +
                $"Install it to run the external-tools E2E head-to-head benchmark.");
        }

        if (!_samtoolsAvailable)
        {
            throw new InvalidOperationException(
                "samtools is not installed on PATH. " +
                "Install it to run the external-tools E2E head-to-head benchmark.");
        }

        if (!_freebayesAvailable)
        {
            throw new InvalidOperationException(
                "freebayes is not installed on PATH. " +
                "Install it to run the external-tools E2E head-to-head benchmark.");
        }

        var iterDir = Path.Combine(_tempDir, $"ext_{bwaExecutable.Replace("-", "_")}_{Guid.NewGuid():N}");
        Directory.CreateDirectory(iterDir);
        try
        {
            return RunExternalPipelineInDir(bwaExecutable, iterDir);
        }
        finally
        {
            if (Directory.Exists(iterDir))
            {
                Directory.Delete(iterDir, recursive: true);
            }
        }
    }

    private int RunExternalPipelineInDir(string bwaExecutable, string workDir)
    {
        var samPath = Path.Combine(workDir, "aln.sam");
        var sortedBamPath = Path.Combine(workDir, "sorted.bam");
        var markedBamPath = Path.Combine(workDir, "marked.bam");
        var vcfPath = Path.Combine(workDir, "variants.vcf");
        var stderrLog = Path.Combine(workDir, "stderr.log");

        // ── Step 1: Align reads (bwa mem / bwa-mem2) ─────────────────────────
        // -k 19  = min seed length, matching BioSharp MinSeedLen=19
        // -T 10  = min alignment score to output, matching MinAlignmentScore=10
        // -t     = thread count
        var alignCmd =
            $"{bwaExecutable} mem -k 19 -T 10 -t {Parallelism}" +
            $" \"{_referenceFaPath}\" \"{_fastqPath}\" > \"{samPath}\" 2>> \"{stderrLog}\"";

        var exit = ExternalProcess.Shell(alignCmd, workDir, timeoutMs: 300_000);
        if (exit != 0)
        {
            var log = File.Exists(stderrLog) ? File.ReadAllText(stderrLog) : string.Empty;
            throw new InvalidOperationException(
                $"{bwaExecutable} mem exited with code {exit}. stderr: {log}");
        }

        // ── Step 2: Sort by coordinate ────────────────────────────────────────
        exit = ExternalProcess.Run(
            "samtools", $"sort -@ {Parallelism} \"{samPath}\" -o \"{sortedBamPath}\"",
            workDir, timeoutMs: 120_000);
        if (exit != 0)
        {
            throw new InvalidOperationException($"samtools sort exited with code {exit}.");
        }

        File.Delete(samPath); // free disk space between steps

        // ── Step 3: Mark PCR duplicates ───────────────────────────────────────
        // Equivalent to preator markdup with default optical pixel distance (100).
        exit = ExternalProcess.Run(
            "samtools", $"markdup \"{sortedBamPath}\" \"{markedBamPath}\"",
            workDir, timeoutMs: 120_000);
        if (exit != 0)
        {
            throw new InvalidOperationException($"samtools markdup exited with code {exit}.");
        }

        File.Delete(sortedBamPath);

        // ── Step 4: Index BAM (required by freebayes) ─────────────────────────
        exit = ExternalProcess.Run(
            "samtools", $"index \"{markedBamPath}\"",
            workDir, timeoutMs: 60_000);
        if (exit != 0)
        {
            throw new InvalidOperationException($"samtools index exited with code {exit}.");
        }

        // ── Step 5: Call variants with freebayes ──────────────────────────────
        // Parameters matched to BioSharp pipeline options:
        //   --min-base-quality 0           = BioSharp does NOT filter by per-base FASTQ quality
        //                                    (it uses read alignment score, not per-base quality).
        //                                    Using --min-base-quality 30 was a mismatch.
        //   --min-alternate-fraction 0.0   = MinAlternateFraction=0.0
        //   --min-alternate-count 1        = MinAlternateObservationCount=1
        //
        // NOTE ON VARIANT COUNT DIFFERENCES:
        //   BioSharp's VariantCallingPipeline and freebayes use fundamentally different algorithms:
        //   - BioSharp derives variant quality from the read alignment score (not per-base quality).
        //     With MinAlignmentScore=10, any reasonably-aligned read's mismatches become variant
        //     candidates. At ~0.008× coverage (250 reads / 4.6 Mbp E. coli), this results in
        //     hundreds of variant calls (mostly single-read support).
        //   - freebayes uses a Bayesian statistical model. At the same ~0.008× coverage, a single
        //     supporting read provides very low posterior probability, so freebayes calls far fewer
        //     variants. This is the expected behavior of a statistically rigorous caller.
        //   The variant count difference (preator ~1000s vs freebayes ~5) is an EXPECTED consequence
        //   of different algorithm philosophies, NOT a sign that steps are missing from either chain.
        //   Both chains perform the same logical steps: align → mark duplicates → call variants.
        //   The benchmark measures execution TIME; variant counts are returned only to prevent
        //   dead-code elimination and are NOT directly comparable across tools.
        var freebayesCmd =
            $"freebayes -f \"{_referenceFaPath}\"" +
            $" --min-base-quality 0" +
            $" --min-alternate-fraction 0.0" +
            $" --min-alternate-count 1" +
            $" \"{markedBamPath}\" > \"{vcfPath}\" 2>> \"{stderrLog}\"";

        exit = ExternalProcess.Shell(freebayesCmd, workDir, timeoutMs: 300_000);
        if (exit != 0)
        {
            var log = File.Exists(stderrLog) ? File.ReadAllText(stderrLog) : string.Empty;
            throw new InvalidOperationException(
                $"freebayes exited with code {exit}. stderr: {log}");
        }

        // Apply QUAL >= 30 filter to match BioSharp's MinVariantQuality=30.
        // In BioSharp, MinVariantQuality is a VARIANT-level quality threshold (VCF QUAL field),
        // not a per-base quality threshold.  freebayes emits a QUAL column; we mirror the
        // same filter here so the two chains apply equivalent post-calling quality gates.
        return CountVcfVariantsAboveQual(vcfPath, minQual: 30);
    }

    // ── Utilities ─────────────────────────────────────────────────────────────

    /// <summary>Counts non-header lines in a VCF file (= variant record count).</summary>
    private static int CountVcfVariants(string vcfPath)
    {
        if (!File.Exists(vcfPath))
        {
            return 0;
        }

        var count = 0;
        foreach (var line in File.ReadLines(vcfPath))
        {
            if (!string.IsNullOrWhiteSpace(line) && line[0] != '#')
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>
    /// Counts VCF variant records whose QUAL column is >= <paramref name="minQual"/>.
    /// This mirrors BioSharp's <c>MinVariantQuality</c> filter, which is a variant-level
    /// quality threshold (VCF QUAL field), NOT a per-base quality threshold.
    /// </summary>
    private static int CountVcfVariantsAboveQual(string vcfPath, double minQual)
    {
        if (!File.Exists(vcfPath))
        {
            return 0;
        }

        var count = 0;
        foreach (var line in File.ReadLines(vcfPath))
        {
            if (string.IsNullOrWhiteSpace(line) || line[0] == '#')
            {
                continue;
            }

            // VCF column 6 (0-based) is QUAL.  "." means missing/unset — skip those.
            var fields = line.Split('\t', 7);
            if (fields.Length < 6)
            {
                continue;
            }

            if (double.TryParse(fields[5], System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out var qual)
                && qual >= minQual)
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>Decompresses a .gz file to the given destination path.</summary>
    private static void DecompressGz(string gzPath, string destPath)
    {
        using var input = new GZipStream(File.OpenRead(gzPath), CompressionMode.Decompress);
        using var output = File.Create(destPath);
        input.CopyTo(output);
    }
}
