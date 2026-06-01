using OpenMedStack.BioSharp.Model.Alignment;

namespace OpenMedStack.BioSharp.Benchmarks;

using System;
using System.IO;
using System.Linq;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using OpenMedStack.BioSharp.Calculations.Alignment;
using OpenMedStack.BioSharp.Calculations.BurrowsWheeler;
using OpenMedStack.BioSharp.Model;

/// <summary>
/// Head-to-head alignment benchmarks: BioSharp (hash-map seeder and FM-index seeder)
/// vs. <c>bwa mem</c> (the industry-standard short-read aligner) on the same input data.
///
/// Fairness design
/// ────────────────
/// • BioSharp's indices are built once in <c>[GlobalSetup]</c> so JIT and index
///   construction are NOT charged per iteration — same as BWA, which also reads
///   its pre-built binary index (*.bwt/*.sa) from disk once at start-up.
/// • BWA's index is built on disk in <c>[GlobalSetup]</c> (one-time cost not benchmarked).
/// • Every benchmark iteration measures the actual alignment work: seeding + SW extension
///   for BioSharp; process start plus BWA's full alignment for <c>bwa mem</c>.
/// • stdout of BWA is redirected to /dev/null so SAM serialization cost is symmetric
///   with BioSharp (pipeline results are returned as in-memory objects, not serialized).
/// • Input reads are the same synthetic FASTQ file for all methods.
///
/// What each timing number means
/// ──────────────────────────────
/// BioSharp numbers      = alignment plus seeding in managed .NET (warm JIT, in-process).
/// bwa mem numbers       = process start plus shared-lib load + index read + alignment.
///
/// The gap between them identifies optimization opportunities:
///   - If BioSharp ≫ BWA  → improve seeding/SW implementation.
///   - If BioSharp ≈ BWA  → overhead is in process management; BioSharp advantage is in
///                         not having that overhead in a longer pipeline.
/// </summary>
[MemoryDiagnoser]
[MarkdownExporterAttribute.GitHub]
[SimpleJob(RuntimeMoniker.Net10_0, warmupCount: 3, iterationCount: 10)]
public class AlignmentHeadToHeadBenchmarks
{
    private const int ThreadCount = 10;

    private Sequence _reference = null!;
    private Sequence[] _reads = null!;
    private FmIndexSeeder _fmSeeder = null!;
    private ReferenceIndex _hashSeeder = null!;
    private string _referenceFastaPath = null!;
    private string _readsFastqPath = null!;
    private string _tempDir = null!;
    private bool _bwaAvailable;
    private bool _bwaMem2Available;
    private string? _preatorDll;
    private string? _preatorPublishError;

    /// <summary>Number of synthetic 150 bp reads aligned in each iteration.</summary>
    [Params(100, 500)]
    public int ReadCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"biosharp-align-bench-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        // Build a synthetic 100 kb reference with a known 150-mer at 50 000
        const string bases = "ACGT";
        var random = new Random(42);
        var refBuf = new char[100_000];
        for (var i = 0; i < refBuf.Length; i++)
        {
            refBuf[i] = bases[random.Next(4)];
        }

        const string target = "ACGTGATTACAGGTTCCGATTAGCTTACGAAAGTCCTTAGATCGGATCCGAA" +
            "ACCGTTAGCTAGCTAGCTGCGATCGATCGATCGATCGATCGGCTAGCTAGCT";
        target.CopyTo(0, refBuf, 50_000, target.Length);
        var refStr = new string(refBuf);
        _reference = new Sequence("chrSynth", refStr.AsMemory(), new string('I', refStr.Length).AsMemory());

        // Build BioSharp indices
        _fmSeeder = new FmIndexSeeder(_reference);
        _hashSeeder = new ReferenceIndex(_reference, new ReferenceIndex.IndexOptions
        {
            SeedSize = 11,
            WindowPadding = 64,
            MaxCandidateWindowsPerRead = 8,
            MaxSeedHitsPerKmer = 64
        });

        // Generate synthetic reads: mix of perfect matches and 1-2 substitution variants
        _reads = new Sequence[ReadCount];
        for (var i = 0; i < ReadCount; i++)
        {
            var offset = (50_000 - 25 + (i * 3)) % (refBuf.Length - 150);
            var readBuf = new char[150];
            refStr.CopyTo(offset, readBuf, 0, 150);
            if (i % 5 == 0)
            {
                readBuf[75] = readBuf[75] == 'A' ? 'T' : 'A'; // SNP
            }

            if (i % 7 == 0)
            {
                readBuf[40] = 'X'; /* soft-clip */
            }

            _reads[i] = new Sequence($"r{i}", new string(readBuf).AsMemory(),
                new string('I', 150).AsMemory());
        }

        // Write FASTA reference and FASTQ reads to temp files for BWA
        _referenceFastaPath = Path.Combine(_tempDir, "ref.fa");
        WriteFasta(_referenceFastaPath, "chrSynth", refStr);

        _readsFastqPath = Path.Combine(_tempDir, "reads.fq");
        WriteFastq(_readsFastqPath, _reads);

        // Build BWA index (one-time setup, not benchmarked)
        _bwaAvailable = ExternalProcess.IsAvailable("bwa");
        _bwaMem2Available = ExternalProcess.IsAvailable("bwa-mem2");

        // Publish the preator binary once (shared across benchmark classes in this process).
        _preatorDll = PreatorPublisher.GetPreatorDll();
        _preatorPublishError = PreatorPublisher.GetPublishError();

        if (_bwaAvailable)
        {
            ExternalProcess.Run("bwa", $"index {_referenceFastaPath}", timeoutMs: 60_000);
        }

        if (_bwaMem2Available)
        {
            ExternalProcess.Run("bwa-mem2", $"index {_referenceFastaPath}", timeoutMs: 60_000);
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
    /// BioSharp: align all reads using the hash-map k-mer seeder + Smith-Waterman extension.
    /// This is the default BioSharp pipeline seeder.
    /// </summary>
    [Benchmark(Baseline = true, Description = "BioSharp-HashMap-Seed+SW")]
    [BenchmarkCategory("Alignment", "BioSharp")]
    public int BioSharp_HashMap_AlignReads()
    {
        var aligned = 0;
        Parallel.ForEach(_reads, new ParallelOptions { MaxDegreeOfParallelism = ThreadCount }, read =>
        {
            var windows = _hashSeeder.FindCandidateWindows(read);
            AlignmentResult? best = null;
            foreach (var w in windows)
            {
                var wLen = w.End - w.Start;
                if (wLen <= 0)
                {
                    continue;
                }

                var refWindow = _reference.Slice(w.Start, wLen);
                var result = SmithWatermanAligner.Align(refWindow, read, minScore: 10);
                if (result != null && (best == null || result.Score > best.Score))
                {
                    best = result;
                }
            }

            if (best != null)
            {
                Interlocked.Increment(ref aligned);
            }
        });

        return aligned;
    }

    /// <summary>
    /// BioSharp: align all reads using the FM-index seeder (BWT backward search) + Smith-Waterman extension.
    /// This is the BWA-style seeding path.
    /// </summary>
    [Benchmark(Description = "BioSharp-FmIndex-Seed+SW")]
    [BenchmarkCategory("Alignment", "BioSharp")]
    public int BioSharp_FmIndex_AlignReads()
    {
        var aligned = 0;
        Parallel.ForEach(_reads, new ParallelOptions { MaxDegreeOfParallelism = ThreadCount }, read =>
        {
            var windows = _fmSeeder.FindCandidateWindows(read);
            AlignmentResult? best = null;
            foreach (var w in windows)
            {
                var wLen = w.End - w.Start;
                if (wLen <= 0)
                {
                    continue;
                }

                var refWindow = _reference.Slice(w.Start, wLen);
                var result = SmithWatermanAligner.Align(refWindow, read, minScore: 10);
                if (result != null && (best == null || result.Score > best.Score))
                {
                    best = result;
                }
            }

            if (best != null)
            {
                Interlocked.Increment(ref aligned);
            }
        });

        return aligned;
    }

    /// <summary>
    /// BioSharp: full <see cref="VariantCallingPipeline"/> with hash-map seeder via FASTQ.
    /// Measures the complete pipeline including I/O and index construction per run.
    /// </summary>
    [Benchmark(Description = "BioSharp-Pipeline-HashMap")]
    [BenchmarkCategory("Alignment", "BioSharp", "Pipeline")]
    public async Task<int> BioSharp_Pipeline_HashMap()
    {
        var pipeline = CreatePipeline();
        return await pipeline.LoadFastQ(_readsFastqPath).ConfigureAwait(false)
            ? pipeline.BuildResult().Metrics.ReadsMapped
            : 0;
    }

    /// <summary>
    /// BioSharp: full <see cref="VariantCallingPipeline"/> with FM-index seeder via FASTQ.
    /// Seeder is pre-built; pipeline builds it internally here to match real usage pattern.
    /// </summary>
    [Benchmark(Description = "BioSharp-Pipeline-FmIndex")]
    [BenchmarkCategory("Alignment", "BioSharp", "Pipeline")]
    public async Task<int> BioSharp_Pipeline_FmIndex()
    {
        var pipeline = CreatePipeline();
        pipeline.Seeder = _fmSeeder;
        return await pipeline.LoadFastQ(_readsFastqPath).ConfigureAwait(false)
            ? pipeline.BuildResult().Metrics.ReadsMapped
            : 0;
    }

    // ── External tool benchmarks ─────────────────────────────────────────────

    /// <summary>
    /// BWA-MEM: align the same FASTQ to the same reference.
    /// Includes process start, index read from disk, alignment, and SAM output to /dev/null.
    /// Throws when <c>bwa</c> is unavailable or when it fails to produce a SAM file,
    /// preventing bogus near-zero timings from a short-circuit path.
    /// </summary>
    [Benchmark(Description = "bwa-mem (subprocess)")]
    [BenchmarkCategory("Alignment", "External")]
    public int BwaMem_Subprocess()
    {
        if (!_bwaAvailable)
        {
            throw new InvalidOperationException(
                "bwa is not installed on PATH. The alignment head-to-head external benchmark cannot be run apples-to-apples on this machine.");
        }

        return RunBwaAndCountMappedReads("bwa", "mem");
    }

    /// <summary>
    /// BWA-MEM2: same as <see cref="BwaMem_Subprocess"/> but using the vectorised BWA-MEM2.
    /// BWA-MEM2 uses SIMD for the full DP, making it the fastest single-threaded aligner.
    /// </summary>
    [Benchmark(Description = "bwa-mem2 (subprocess)")]
    [BenchmarkCategory("Alignment", "External")]
    public int BwaMem2_Subprocess()
    {
        if (!_bwaMem2Available)
        {
            throw new InvalidOperationException(
                "bwa-mem2 is not installed on PATH. The alignment head-to-head external benchmark cannot be run apples-to-apples on this machine.");
        }

        return RunBwaAndCountMappedReads("bwa-mem2", "mem");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// preator (compiled subprocess): run <c>preator analysis</c> on the same reference + reads.
    /// Uses the published, pre-compiled preator binary — process start, .NET runtime load, index
    /// construction, seeding, and Smith-Waterman alignment are all included in the measurement.
    ///
    /// This is the end-to-end pipeline comparison counterpart to the in-process
    /// <see cref="BioSharp_Pipeline_HashMap"/> benchmark.  Because preator runs as a separate
    /// process it also includes JIT compilation, whereas the in-process benchmarks only pay JIT
    /// on the first warmup iteration.  The difference exposes the true startup + JIT overhead
    /// of the .NET runtime vs. a native C tool like BWA.
    /// </summary>
    [Benchmark(Description = "preator-alignment (subprocess)")]
    [BenchmarkCategory("Alignment", "Preator")]
    public int Preator_Alignment_Subprocess()
    {
        if (_preatorDll == null)
        {
            throw new InvalidOperationException(
                $"preator binary is not available: {_preatorPublishError}");
        }

        var outDir = Path.Combine(_tempDir, $"preator_alignment_{Guid.NewGuid():N}");
        Directory.CreateDirectory(outDir);
        try
        {
            var runningInContainer = Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") == "true";
            var exit = runningInContainer
                ? ExternalProcess.Run(
                    "/app/preator/preator",
                    $"align" +
                    $" --reference \"{_referenceFastaPath}\"" +
                    $" --fastq \"{_readsFastqPath}\"" +
                    $" --output \"{outDir}\"" +
                    $" --output-prefix aligned" +
                    $" -p {ThreadCount}",
                    _tempDir,
                    300_000)
                : ExternalProcess.Run(
                    "dotnet",
                    $"\"{_preatorDll}\" align" +
                    $" --reference \"{_referenceFastaPath}\"" +
                    $" --fastq \"{_readsFastqPath}\"" +
                    $" --output \"{outDir}\"" +
                    $" --output-prefix aligned" +
                    $" -p {ThreadCount}",
                    _tempDir,
                    300_000);
            if (exit != 0)
            {
                throw new InvalidOperationException($"preator align exited with code {exit}.");
            }

            // Return the BAM file size as a work-done signal.
            var bamPath = Path.Combine(outDir, "aligned.bam");
            return File.Exists(bamPath) ? (int)new FileInfo(bamPath).Length : 0;
        }
        finally
        {
            if (Directory.Exists(outDir))
            {
                Directory.Delete(outDir, recursive: true);
            }
        }
    }


    private VariantCallingPipeline CreatePipeline()
    {
        return new VariantCallingPipeline(_reference, "chrSynth",
            new VariantCallingPipeline.PipelineOptions
            {
                SeedSize = 11,
                MinAlignmentScore = 10,
                EnableGraphSvDetection = false,
                DegreeOfParallelism = ThreadCount,
                CandidateAlignmentDegreeOfParallelism = ThreadCount,
                ParallelCandidateWindowThreshold = 1
            });
    }

    private int RunBwaAndCountMappedReads(string executable, string subcommand)
    {
        var tag = executable.Replace("/", "_", StringComparison.Ordinal);
        var samPath = Path.Combine(_tempDir, $"{tag}_{Guid.NewGuid():N}.sam");
        var logPath = Path.Combine(_tempDir, $"{tag}_{Guid.NewGuid():N}.stderr.log");
        try
        {
            var command =
                $"{executable} {subcommand} -t {ThreadCount} \"{_referenceFastaPath}\" \"{_readsFastqPath}\" > \"{samPath}\" 2> \"{logPath}\"";
            var exit = ExternalProcess.Shell(command, _tempDir, 120_000);
            if (exit != 0)
            {
                var log = File.Exists(logPath) ? File.ReadAllText(logPath) : string.Empty;
                throw new InvalidOperationException($"{executable} exited with code {exit}. STDERR: {log}");
            }

            if (!File.Exists(samPath))
            {
                throw new InvalidOperationException($"{executable} completed but produced no SAM output.");
            }

            var mappedCount = CountMappedSamRecords(samPath);
            if (mappedCount == 0)
            {
                throw new InvalidOperationException(
                    $"{executable} completed but produced zero mapped reads, which is suspicious for this synthetic dataset.");
            }

            return mappedCount;
        }
        finally
        {
            if (File.Exists(samPath))
            {
                File.Delete(samPath);
            }

            if (File.Exists(logPath))
            {
                File.Delete(logPath);
            }
        }
    }

    private static int CountMappedSamRecords(string samPath)
    {
        var mapped = 0;
        foreach (var line in File.ReadLines(samPath))
        {
            if (string.IsNullOrWhiteSpace(line) || line[0] == '@')
            {
                continue;
            }

            var fields = line.Split('\t');
            if (fields.Length < 2)
            {
                throw new InvalidOperationException($"SAM line had fewer than 2 tab-delimited fields: {line}");
            }

            if (!int.TryParse(fields[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var flag))
            {
                throw new InvalidOperationException($"Unable to parse SAM flag from line: {line}");
            }

            if ((flag & 0x4) == 0)
            {
                mapped++;
            }
        }

        return mapped;
    }

    private static void WriteFasta(string path, string id, string seq)
    {
        using var w = new StreamWriter(path);
        w.WriteLine($">{id}");
        const int lineLen = 80;
        for (var i = 0; i < seq.Length; i += lineLen)
        {
            w.WriteLine(seq.AsSpan(i, Math.Min(lineLen, seq.Length - i)));
        }
    }

    private static void WriteFastq(string path, Sequence[] reads)
    {
        using var w = new StreamWriter(path);
        foreach (var r in reads)
        {
            w.WriteLine($"@{r.Id}");
            w.WriteLine(new string(r.GetData().ToArray()));
            w.WriteLine("+");
            w.WriteLine(new string(r.GetQuality().ToArray()));
        }
    }
}
