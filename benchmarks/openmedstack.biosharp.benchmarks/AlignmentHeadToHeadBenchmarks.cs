using OpenMedStack.BioSharp.Model.Alignment;

namespace OpenMedStack.BioSharp.Benchmarks;

using System;
using System.IO;
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
///   for BioSharp; process start + BWA's full alignment for <c>bwa mem</c>.
/// • stdout of BWA is redirected to /dev/null so SAM serialisation cost is symmetric
///   with BioSharp (pipeline results are returned as in-memory objects, not serialised).
/// • Input reads are the same synthetic FASTQ file for all methods.
///
/// What each timing number means
/// ──────────────────────────────
/// BioSharp numbers      = alignment + seeding in managed .NET (warm JIT, in-process).
/// bwa mem numbers       = process start + shared-lib load + index read + alignment.
///
/// The gap between them identifies optimisation opportunities:
///   - If BioSharp ≫ BWA  → improve seeding/SW implementation.
///   - If BioSharp ≈ BWA  → overhead is in process management; BioSharp advantage is in
///                         not having that overhead in a longer pipeline.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net10_0, warmupCount: 3, iterationCount: 10)]
public class AlignmentHeadToHeadBenchmarks
{
    private const int ThreadCount = 10;

    private Sequence       _reference    = null!;
    private Sequence[]     _reads        = null!;
    private FmIndexSeeder  _fmSeeder     = null!;
    private ReferenceIndex _hashSeeder   = null!;
    private string         _referenceFastaPath = null!;
    private string         _readsFastqPath     = null!;
    private string         _tempDir            = null!;
    private bool           _bwaAvailable;
    private bool           _bwaMem2Available;

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
        _fmSeeder   = new FmIndexSeeder(_reference);
        _hashSeeder = new ReferenceIndex(_reference, new ReferenceIndex.IndexOptions
        {
            SeedSize                   = 11,
            WindowPadding              = 64,
            MaxCandidateWindowsPerRead = 8,
            MaxSeedHitsPerKmer         = 64
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

            if (i % 7 == 0) { readBuf[40] = 'X'; /* soft-clip */ }
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
                throw new InvalidOperationException($"{executable} completed but produced zero mapped reads, which is suspicious for this synthetic dataset.");
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

/// <summary>
/// Smith-Waterman alignment micro-benchmarks at different reference window sizes.
/// Isolates the DP cost from seeding so we can profile the inner loop directly.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net10_0, warmupCount: 5, iterationCount: 30)]
public class SmithWatermanMicroBenchmarks
{
    private Sequence _ref200bp  = null!;
    private Sequence _ref500bp  = null!;
    private Sequence _ref2kbp   = null!;
    private Sequence _read75bp  = null!;
    private Sequence _read150bp = null!;

    private static Sequence MakeSeq(string id, int length, Random rng)
    {
        const string bases = "ACGT";
        var buf = new char[length];
        for (var i = 0; i < length; i++)
        {
            buf[i] = bases[rng.Next(4)];
        }

        return new Sequence(id, new string(buf).AsMemory(), new string('I', length).AsMemory());
    }

    [GlobalSetup]
    public void Setup()
    {
        var rng = new Random(17);
        _ref200bp  = MakeSeq("r200",   200, rng);
        _ref500bp  = MakeSeq("r500",   500, rng);
        _ref2kbp   = MakeSeq("r2k",  2_000, rng);
        _read75bp  = MakeSeq("q75",     75, rng);
        _read150bp = MakeSeq("q150",   150, rng);
    }

    [Benchmark(Baseline = true, Description = "SW-75bp-on-200bp-ref")]
    public AlignmentResult? Sw_75bp_200bp() => SmithWatermanAligner.Align(_ref200bp,  _read75bp,  minScore: 10);

    [Benchmark(Description = "SW-150bp-on-500bp-ref")]
    public AlignmentResult? Sw_150bp_500bp() => SmithWatermanAligner.Align(_ref500bp,  _read150bp, minScore: 10);

    [Benchmark(Description = "SW-150bp-on-2kb-ref")]
    public AlignmentResult? Sw_150bp_2kb()   => SmithWatermanAligner.Align(_ref2kbp,   _read150bp, minScore: 10);

    [Benchmark(Description = "SW-150bp-on-500bp-ref-banded")]
    public AlignmentResult? Sw_150bp_500bp_Banded() =>
        SmithWatermanAligner.Align(_ref500bp, _read150bp, bandWidth: 32, minScore: 10);

    [Benchmark(Description = "SW-150bp-on-2kb-ref-banded")]
    public AlignmentResult? Sw_150bp_2kb_Banded() =>
        SmithWatermanAligner.Align(_ref2kbp, _read150bp, bandWidth: 32, minScore: 10);
}






