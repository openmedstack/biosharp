namespace OpenMedStack.BioSharp.Benchmarks;

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using Microsoft.Extensions.Logging.Abstractions;
using OpenMedStack.BioSharp.Io.Bcl;
using OpenMedStack.BioSharp.Io.FastQ;
using OpenMedStack.BioSharp.Model;
using OpenMedStack.BioSharp.Model.Bcl;

/// <summary>
/// Head-to-head BCL decoding benchmarks: BioSharp vs. Illumina CLI converters.
///
/// What is measured
/// ─────────────────
/// • BioSharp: in-process BCL file reading + FASTQ writing.  The JIT is warm by
///   iteration 1 so subsequent iterations measure pure BCL parsing throughput.
/// • bcl-convert / bcl2fastq: full subprocess including process start, BCL parsing,
///   and FASTQ writing to a temp directory. Disk I/O is symmetric with BioSharp
///   (all paths write FASTQ bytes).
///
/// The test data is read from the real Illumina run folder at <c>data/sampledata</c>
/// (mounted as <c>/app/data/sampledata</c> in the benchmark Docker image).
/// The run has 1 lane, 1 tile (1101), and a 26T+8I+98T read structure.
///
/// ThreadCount is parameterised with 1 and 10 so the impact of multi-threading
/// can be observed directly in the benchmark results table.
/// </summary>
[MemoryDiagnoser]
[MarkdownExporterAttribute.GitHub]
[SimpleJob(RuntimeMoniker.Net10_0, warmupCount: 3, iterationCount: 10)]
public class BclHeadToHeadBenchmarks
{
    // ── Read structure constants for the sampledata run folder ───────────────
    // RunInfo.xml: Read1=26T  Read2=8I  Read3=98T
    private const string SampleDataReadStructure = "26T8B98T";
    private const int SampleDataRead1Cycles  = 26;
    private const int SampleDataIndexCycles  = 8;
    private const int SampleDataRead2Cycles  = 98;

    private string   _bclRunDir                   = null!;
    private string   _bclOutputDir                = null!;
    private string   _bclConvertSampleSheetPath   = null!;
    private string   _bcl2FastqSampleSheetPath    = null!;
    private string   _tempDir                     = null!;
    private FileInfo[] _cycleFiles     = null!;
    private TileIndexRecord _tile       = null!;
    private Read[]   _reads            = null!;
    private bool     _bclConvertAvailable;
    private bool     _bcl2FastqAvailable;
    // Stores any error that caused [GlobalSetup] validation to fail for each tool.
    // When non-null the corresponding benchmark method throws this message instead of running.
    // This prevents a single tool issue from failing ALL benchmarks in the class.
    private string?  _bioSharpSetupError;
    private string?  _bclConvertSetupError;
    private string?  _bcl2FastqSetupError;
    private string?  _preatorDll;
    private string?  _preatorPublishError;

    /// <summary>
    /// Number of threads passed to each BCL conversion tool.
    /// Parameterised so that single-threaded and multi-threaded performance can
    /// be compared side by side.
    /// </summary>
    [Params(1, 10)]
    public int ThreadCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _bclRunDir = GetSampleDataPath();
        _tempDir   = Path.Combine(Path.GetTempPath(), $"biosharp-bcl-hth-{Guid.NewGuid():N}");
        _bclOutputDir = Path.Combine(_tempDir, "output");
        // Sample sheets are written to the temp directory so that the sampledata
        // folder itself is never modified (it may be read-only in CI / Docker).
        _bclConvertSampleSheetPath = Path.Combine(_tempDir, "SampleSheet.bcl-convert.csv");
        _bcl2FastqSampleSheetPath  = Path.Combine(_tempDir, "SampleSheet.bcl2fastq.csv");
        Directory.CreateDirectory(_tempDir);
        Directory.CreateDirectory(_bclOutputDir);

        _bclConvertAvailable = ExternalProcess.IsAvailable("bcl-convert");
        _bcl2FastqAvailable  = ExternalProcess.IsAvailable("bcl2fastq");

        // Publish the preator binary once (shared across benchmark classes in this process).
        _preatorDll = PreatorPublisher.GetPreatorDll();
        _preatorPublishError = PreatorPublisher.GetPublishError();

        // Build the cycle-file list for the single-tile BioSharp reader (lane 1, tile 1101).
        var baseCallsLane1 = Path.Combine(_bclRunDir, "Data", "Intensities", "BaseCalls", "L001");
        var totalCycles = Directory.GetDirectories(baseCallsLane1, "C*.1").Length;
        _cycleFiles = Enumerable.Range(1, totalCycles)
            .Select(c =>
            {
                // Prefer compressed BCL; fall back to uncompressed.
                var gz  = Path.Combine(baseCallsLane1, $"C{c}.1", "s_1_1101.bcl.gz");
                var raw = Path.Combine(baseCallsLane1, $"C{c}.1", "s_1_1101.bcl");
                return new FileInfo(File.Exists(gz) ? gz : raw);
            })
            .ToArray();

        // Determine cluster count from the first BCL file header.
        var clusterCount = ReadBclClusterCount(_cycleFiles[0].FullName);

        // Read descriptors match the sampledata RunInfo.xml exactly.
        _reads =
        [
            new Read { Number = 1, NumCycles = SampleDataRead1Cycles, IsIndexedRead = "N", Type = ReadType.T },
            new Read { Number = 2, NumCycles = SampleDataIndexCycles,  IsIndexedRead = "Y", Type = ReadType.B },
            new Read { Number = 3, NumCycles = SampleDataRead2Cycles, IsIndexedRead = "N", Type = ReadType.T }
        ];

        _tile = new TileIndexRecord(
            Tile:                      1101,
            NumClustersInTile:         clusterCount,
            IndexOfFirstClusterInTile: 0,
            ZeroBasedTileNumber:       0);

        // Write tool-specific sample sheets.
        WriteBclConvertSampleSheet(_bclConvertSampleSheetPath);
        WriteBcl2FastqSampleSheet(_bcl2FastqSampleSheetPath);

        // Validate the BioSharp comparable path. Failures are caught and stored so that a
        // BioSharp setup problem only fails BioSharp benchmark rows, not external-tool rows.
        try
        {
            ValidateBioSharpComparablePath().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            _bioSharpSetupError = ex.Message;
        }

        // If external BCL converters are present, attempt a validation run.
        // Validation failures are caught and stored rather than propagated: a problem with any
        // individual tool must not fail ALL benchmarks in the class.
        if (_bclConvertAvailable)
        {
            try
            {
                ValidateExternalToolOutput(
                    toolKey: "bclconvert",
                    toolDisplayName: "bcl-convert",
                    runExternal: RunBclConvertAndSummarize).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                _bclConvertSetupError = ex.Message;
                _bclConvertAvailable  = false;
            }
        }

        if (_bcl2FastqAvailable)
        {
            try
            {
                ValidateExternalToolOutput(
                    toolKey: "bcl2fastq",
                    toolDisplayName: "bcl2fastq",
                    runExternal: RunBcl2FastqAndSummarize).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                _bcl2FastqSetupError = ex.Message;
                _bcl2FastqAvailable  = false;
            }
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
    /// BioSharp: decode one BCL tile and count clusters.
    /// Baseline — measures only the BCL parsing without FASTQ serialisation.
    /// </summary>
    [Benchmark(Baseline = true, Description = "BioSharp-BCL-Decode-Only")]
    [BenchmarkCategory("BCL", "BioSharp")]
    public async Task<int> BioSharp_Bcl_DecodeOnly()
    {
        if (_bioSharpSetupError != null)
        {
            throw new InvalidOperationException(_bioSharpSetupError);
        }

        await using var reader = await CreateBclReader().ConfigureAwait(false);
        var count = 0;
        await foreach (var _ in reader)
            count++;
        return count;
    }

    /// <summary>
    /// BioSharp: decode one BCL tile and write compressed FASTQ bytes to a MemoryStream.
    /// Mirrors the full bcl2fastq pipeline cost (parsing + compression + writing).
    /// </summary>
    [Benchmark(Description = "BioSharp-BCL-Decode+FastqWrite")]
    [BenchmarkCategory("BCL", "BioSharp")]
    public async Task<long> BioSharp_Bcl_DecodeAndWriteFastq()
    {
        if (_bioSharpSetupError != null)
        {
            throw new InvalidOperationException(_bioSharpSetupError);
        }

        await using var reader = await CreateBclReader().ConfigureAwait(false);
        await using var output = new MemoryStream();
        await using var idxOut = new MemoryStream();
        await using var writer = new FastQWriter(
            NullLogger.Instance, output, idxOut,
            System.IO.Compression.CompressionLevel.Fastest, leaveOpen: true);

        await writer.Write(ToSequences(reader), CancellationToken.None).ConfigureAwait(false);

        return output.Length;
    }

    /// <summary>
    /// BioSharp: convert the sampledata run folder to FASTQ on disk.
    /// This is the directly comparable BioSharp side of the head-to-head benchmark.
    /// </summary>
    [Benchmark(Description = "BioSharp-BCL-RunFolder+FastqWrite")]
    [BenchmarkCategory("BCL", "BioSharp", "DiskIO", "Comparable")]
    public async Task<long> BioSharp_Bcl_DecodeAndWriteToDisk()
    {
        if (_bioSharpSetupError != null)
        {
            throw new InvalidOperationException(_bioSharpSetupError);
        }

        var outDir = Path.Combine(_bclOutputDir, $"biosharp_{Guid.NewGuid():N}");
        Directory.CreateDirectory(outDir);
        var summary = await RunBioSharpRunFolderToFastq(outDir).ConfigureAwait(false);
        Directory.Delete(outDir, recursive: true);
        return summary.TotalCompressedBytes;
    }

    // ── External tool benchmarks ─────────────────────────────────────────────

    /// <summary>
    /// bcl-convert: convert the sampledata BCL run directory to FASTQ files.
    /// Includes process start, BCL reading, demultiplexing, and FASTQ writing to disk.
    /// Throws if <c>bcl-convert</c> is not installed or if it fails to produce FASTQ output,
    /// because silent short-circuiting would create misleading near-zero timings.
    /// </summary>
    [Benchmark(Description = "bcl-convert (subprocess)")]
    [BenchmarkCategory("BCL", "External", "Comparable")]
    public long BclConvert_Subprocess()
    {
        if (_bclConvertSetupError != null)
        {
            throw new InvalidOperationException(_bclConvertSetupError);
        }

        if (!_bclConvertAvailable)
        {
            throw new InvalidOperationException(
                "bcl-convert is not installed on PATH. The BCL head-to-head external benchmark cannot be run apples-to-apples on this machine.");
        }

        var outDir = Path.Combine(_bclOutputDir, $"bclconvert_{Guid.NewGuid():N}");
        Directory.CreateDirectory(outDir);
        try
        {
            var summary = RunBclConvertAndSummarize(outDir);
            return summary.TotalCompressedBytes;
        }
        finally
        {
            if (Directory.Exists(outDir))
            {
                Directory.Delete(outDir, recursive: true);
            }
        }
    }

    /// <summary>
    /// bcl2fastq: convert the sampledata BCL run directory to FASTQ files.
    /// Includes process start, BCL reading, demultiplexing, and FASTQ writing to disk.
    /// Throws if <c>bcl2fastq</c> is not installed or if it fails to produce FASTQ output,
    /// because silent short-circuiting would create misleading near-zero timings.
    /// </summary>
    [Benchmark(Description = "bcl2fastq (subprocess)")]
    [BenchmarkCategory("BCL", "External", "Comparable")]
    public long Bcl2Fastq_Subprocess()
    {
        if (_bcl2FastqSetupError != null)
        {
            throw new InvalidOperationException(_bcl2FastqSetupError);
        }

        if (!_bcl2FastqAvailable)
        {
            throw new InvalidOperationException(
                "bcl2fastq is not installed on PATH. The BCL head-to-head external benchmark cannot be run apples-to-apples on this machine.");
        }

        var outDir = Path.Combine(_bclOutputDir, $"bcl2fastq_{Guid.NewGuid():N}");
        Directory.CreateDirectory(outDir);
        try
        {
            var summary = RunBcl2FastqAndSummarize(outDir);
            return summary.TotalCompressedBytes;
        }
        finally
        {
            if (Directory.Exists(outDir))
            {
                Directory.Delete(outDir, recursive: true);
            }
        }
    }

    // ── BCL throughput metric benchmark ─────────────────────────────────────

    /// <summary>
    /// preator (compiled subprocess): run <c>preator bcl</c> on the same sampledata run folder.
    /// Uses the published, pre-compiled preator binary so process start, .NET runtime load, BCL
    /// parsing, and FASTQ writing are all included — directly comparable to bcl-convert/bcl2fastq.
    /// </summary>
    [Benchmark(Description = "preator-bcl (subprocess)")]
    [BenchmarkCategory("BCL", "Preator", "DiskIO", "Comparable")]
    public long Preator_Bcl_Subprocess()
    {
        if (_preatorDll == null)
        {
            throw new InvalidOperationException(
                $"preator binary is not available: {_preatorPublishError}");
        }

        var outDir = Path.Combine(_bclOutputDir, $"preator_{Guid.NewGuid():N}");
        Directory.CreateDirectory(outDir);
        try
        {var runningInContainer = Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") == "true";
            var exit = runningInContainer
                ? ExternalProcess.Run(
                    "/app/preator/preator",
                    $"bcl --input \"{_bclRunDir}\" --output \"{outDir}\" --readstructure \"{SampleDataReadStructure}\"",
                    _tempDir,
                    300_000)
                : ExternalProcess.Run(
                "dotnet",
                $"\"{_preatorDll}\" bcl --input \"{_bclRunDir}\" --output \"{outDir}\" --readstructure \"{SampleDataReadStructure}\"",
                _tempDir,
                300_000);
            if (exit != 0)
            {
                throw new InvalidOperationException($"preator bcl exited with code {exit}.");
            }

            var summary = SummarizeFastqDirectory(outDir);
            if (summary.FastqFileCount == 0 || summary.ReadCount == 0)
            {
                throw new InvalidOperationException(
                    "preator bcl completed but produced no FASTQ reads. " +
                    "The benchmark would not be apples-to-apples.");
            }

            return summary.TotalCompressedBytes;
        }
        finally
        {
            if (Directory.Exists(outDir))
            {
                Directory.Delete(outDir, recursive: true);
            }
        }
    }



    /// <summary>
    /// Measures cluster throughput (clusters / second) for BioSharp BCL decoding.
    /// Useful for comparing against bcl2fastq's documented throughput figures.
    /// </summary>
    [Benchmark(Description = "BioSharp-BCL-ThroughputProbe")]
    [BenchmarkCategory("BCL", "BioSharp", "Throughput")]
    public async Task<double> BioSharp_Bcl_ThroughputProbe()
    {
        if (_bioSharpSetupError != null)
        {
            throw new InvalidOperationException(_bioSharpSetupError);
        }

        var start = System.Diagnostics.Stopwatch.GetTimestamp();
        await using var reader = await CreateBclReader().ConfigureAwait(false);
        var count = 0;
        await foreach (var _ in reader) count++;
        var elapsedSec = (double)(System.Diagnostics.Stopwatch.GetTimestamp() - start)
                         / System.Diagnostics.Stopwatch.Frequency;
        return count / elapsedSec; // clusters per second
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Resolves the path to the sampledata Illumina run folder.
    /// Checks the Docker mount point first, then falls back to walking up from the
    /// current working directory / application base looking for <c>data/sampledata</c>.
    /// </summary>
    private static string GetSampleDataPath()
    {
        const string docker = "/app/data/sampledata";
        if (Directory.Exists(docker))
            return docker;

        // Walk up from the working directory.
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "data", "sampledata");
            if (Directory.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }

        // Walk up from the assembly location (useful when running from bin/Debug/…).
        dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "data", "sampledata");
            if (Directory.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException(
            "Cannot locate 'data/sampledata' relative to the working directory, " +
            "the application base directory, or the Docker mount '/app/data/sampledata'. " +
            "Make sure the sampledata folder is accessible before running BCL head-to-head benchmarks.");
    }

    /// <summary>
    /// Reads the cluster count stored in the 4-byte header of a BCL (or BCL.gz) file.
    /// </summary>
    private static int ReadBclClusterCount(string bclPath)
    {
        using var fs = File.OpenRead(bclPath);
        Stream readStream = bclPath.EndsWith(".gz", StringComparison.OrdinalIgnoreCase)
            ? new GZipStream(fs, CompressionMode.Decompress, leaveOpen: false)
            : fs;
        try
        {
            var buf = new byte[4];
            readStream.ReadExactly(buf);
            return BitConverter.ToInt32(buf, 0);
        }
        finally
        {
            if (!ReferenceEquals(readStream, fs))
                readStream.Dispose();
        }
    }

    private Task<BclReader> CreateBclReader()
    {
        return BclReader.Create(
            _cycleFiles,
            _reads,
            _tile,
            new BclQualityEvaluationStrategy(2),
            NullLogger<BclReader>.Instance);
    }

    private async Task<FastqDirectorySummary> RunBioSharpRunFolderToFastq(string outDir)
    {
        var reader = new IlluminaDataReader(
            new DirectoryInfo(_bclRunDir),
            NullLoggerFactory.Instance,
            ReadStructure.Parse(SampleDataReadStructure));

        _ = reader.RunInfo();
        foreach (var lane in reader.GetAllLanes())
        {
            var outFastq = Path.Combine(outDir, $"biosharp_L{lane:D3}.fastq.gz");
            var outIndex = Path.Combine(outDir, $"biosharp_L{lane:D3}.fastqi.gz");
            await using var fastqFile = File.Open(
                outFastq,
                new FileStreamOptions
                {
                    Access = FileAccess.Write,
                    Mode = FileMode.Create,
                    Options = FileOptions.Asynchronous,
                    Share = FileShare.None
                });
            await using var indexFile = File.Open(
                outIndex,
                new FileStreamOptions
                {
                    Access = FileAccess.Write,
                    Mode = FileMode.Create,
                    Options = FileOptions.Asynchronous,
                    Share = FileShare.None
                });
            await using var writer = new FastQWriter(NullLogger.Instance, fastqFile, indexFile, CompressionLevel.Fastest, leaveOpen: true);

            await foreach (var sampleReader in reader.ReadClusterData(lane, CancellationToken.None).ConfigureAwait(false))
            {
                await using var sr = sampleReader;
                await writer.Write(sr.ReadBclData(DefaultQualityTrimmer.Instance, CancellationToken.None), CancellationToken.None)
                    .ConfigureAwait(false);
            }
        }

        return SummarizeFastqDirectory(outDir);
    }

    private FastqDirectorySummary RunBclConvertAndSummarize(string outDir)
    {
        var logPath = Path.Combine(outDir, "bclconvert.stderr.log");
        var command =
            $"bcl-convert --bcl-input-directory \"{_bclRunDir}\"" +
            $" --output-directory \"{outDir}\"" +
            $" --sample-sheet \"{_bclConvertSampleSheetPath}\"" +
            $" --bcl-num-conversion-threads {ThreadCount}" +
            $" --bcl-num-compression-threads {ThreadCount}" +
            " --bcl-sampleproject-subdirectories false" +
            // --no-lane-splitting false = produce one FASTQ file per lane per sample.
            " --no-lane-splitting false" +
            // Suppress strict input-validation failures on the sampledata run folder
            // (e.g. missing InterOp files, non-standard instrument name in RunInfo.xml).
//            " --bcl-validation-stringency none" +
            " --force" +
            $" > {ExternalProcess.NullDevice} 2> \"{logPath}\"";

        var exit = ExternalProcess.Shell(command, timeoutMs: 300_000);
        if (exit != 0)
        {
            var log = File.Exists(logPath) ? File.ReadAllText(logPath) : string.Empty;
            throw new InvalidOperationException($"bcl-convert exited with code {exit}. STDERR: {log}");
        }

        var summary = SummarizeFastqDirectory(outDir);
        if (summary.FastqFileCount == 0 || summary.ReadCount == 0)
        {
            throw new InvalidOperationException(
                "bcl-convert completed but produced no FASTQ reads. The benchmark would not be apples-to-apples.");
        }

        return summary;
    }

    private FastqDirectorySummary RunBcl2FastqAndSummarize(string outDir)
    {
        var logPath = Path.Combine(outDir, "bcl2fastq.stderr.log");
        var command =
            $"bcl2fastq --runfolder-dir \"{_bclRunDir}\"" +
            $" --output-dir \"{outDir}\"" +
            $" --sample-sheet \"{_bcl2FastqSampleSheetPath}\"" +
            $" --loading-threads {ThreadCount}" +
            $" --processing-threads {ThreadCount}" +
            $" --writing-threads {ThreadCount}" +
            // Tolerate any missing ancillary files in the sampledata run folder.
            " --ignore-missing-bcls" +
            " --ignore-missing-filter" +
            " --ignore-missing-positions" +
//            " --force" +
            $" > {ExternalProcess.NullDevice} 2> \"{logPath}\"";

        var exit = ExternalProcess.Shell(command, timeoutMs: 300_000);
        if (exit != 0)
        {
            var log = File.Exists(logPath) ? File.ReadAllText(logPath) : string.Empty;
            throw new InvalidOperationException($"bcl2fastq exited with code {exit}. STDERR: {log}");
        }

        var summary = SummarizeFastqDirectory(outDir);
        if (summary.FastqFileCount == 0 || summary.ReadCount == 0)
        {
            throw new InvalidOperationException(
                "bcl2fastq completed but produced no FASTQ reads. The benchmark would not be apples-to-apples.");
        }

        return summary;
    }

    private async Task ValidateBioSharpComparablePath()
    {
        var validationDir = Path.Combine(_bclOutputDir, "validate_biosharp");
        Directory.CreateDirectory(validationDir);
        try
        {
            var summary = await RunBioSharpRunFolderToFastq(validationDir).ConfigureAwait(false);
            if (summary.FastqFileCount == 0)
            {
                throw new InvalidOperationException("BioSharp comparable BCL benchmark produced no FASTQ output files.");
            }

            if (summary.ReadCount == 0)
            {
                throw new InvalidOperationException("BioSharp comparable BCL benchmark produced no reads.");
            }
        }
        finally
        {
            if (Directory.Exists(validationDir))
            {
                Directory.Delete(validationDir, recursive: true);
            }
        }
    }

    /// <summary>
    /// Validates that an external BCL conversion tool runs successfully against the
    /// sampledata run folder and produces at least one FASTQ read.
    /// Unlike the previous per-read-count equality check (which was correct for synthetic
    /// data), here we only assert that the tool produced non-zero output, because each tool
    /// may produce different sets of FASTQ files (R1/R2/I1 naming differs) and we only need
    /// to confirm the tool is functional before timing it.
    /// </summary>
    private async Task ValidateExternalToolOutput(
        string toolKey,
        string toolDisplayName,
        Func<string, FastqDirectorySummary> runExternal)
    {
        var externalDir = Path.Combine(_bclOutputDir, $"validate_{toolKey}");
        Directory.CreateDirectory(externalDir);
        try
        {
            var externalSummary = runExternal(externalDir);

            if (externalSummary.ReadCount == 0)
            {
                throw new InvalidOperationException(
                    $"{toolDisplayName} produced 0 reads during validation run. " +
                    "The benchmark would not be meaningful.");
            }
        }
        finally
        {
            if (Directory.Exists(externalDir))
            {
                Directory.Delete(externalDir, recursive: true);
            }
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }

    private static FastqDirectorySummary SummarizeFastqDirectory(string dir)
    {
        var fastqFiles = Directory.EnumerateFiles(dir, "*.fastq.gz", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
        long readCount = 0;
        long totalBases = 0;
        long totalCompressedBytes = 0;

        foreach (var file in fastqFiles)
        {
            totalCompressedBytes += new FileInfo(file).Length;
            using var fs = File.OpenRead(file);
            using var gzip = new GZipStream(fs, CompressionMode.Decompress);
            using var reader = new StreamReader(gzip, Encoding.UTF8, leaveOpen: false);

            string? line;
            var lineIndex = 0;
            while ((line = reader.ReadLine()) != null)
            {
                switch (lineIndex % 4)
                {
                    case 1:
                        totalBases += line.Length;
                        break;
                    case 3:
                        readCount++;
                        break;
                }

                lineIndex++;
            }

            if (lineIndex % 4 != 0)
            {
                throw new InvalidOperationException($"FASTQ file {file} had incomplete records.");
            }
        }

        return new FastqDirectorySummary(fastqFiles.Length, readCount, totalBases, totalCompressedBytes);
    }

    private static Sequence ToSequence(ReadData[] readData)
    {
        var r = readData[0];
        var quals = new char[r.Qualities.Length];
        var src = r.Qualities.Span;
        for (var i = 0; i < src.Length; i++)
        {
            quals[i] = (char)(src[i] + 33);
        }

        return new Sequence($"c-{r.ReadIndex}", r.Bases.ToArray(), quals);
    }

    /// <summary>
    /// Writes a bcl-convert v2 format SampleSheet for the sampledata run structure
    /// (26T + 8I + 98T).
    ///
    /// Rules for bcl-convert 4.x with FileFormatVersion=2:
    ///  • [BCLConvert_Settings] must use key=value pairs — bare numbers are rejected.
    ///  • SoftwareVersion is intentionally omitted to avoid build-string mismatch errors.
    ///  • [Data] is excluded: mixing [BCLConvert_Data] and [Data] can cause warnings.
    /// </summary>
    private void WriteBclConvertSampleSheet(string path)
    {
        File.WriteAllText(path, $"""
            [Header]
            FileFormatVersion,2

            [Reads]
            Read1Cycles,{SampleDataRead1Cycles}
            Read2Cycles,{SampleDataRead2Cycles}
            Index1Cycles,{SampleDataIndexCycles}

            [BCLConvert_Data]
            Lane,Sample_ID,index
            1,BenchSample,ATCCACTG
            
            [BCLConvert_Settings]
            SoftwareVersion,4.1.23
            FastqCompressionFormat,gzip
            """);
    }

    /// <summary>
    /// Writes a bcl2fastq IEM v1 format SampleSheet for the sampledata run structure.
    /// bcl2fastq 2.x reads [Data] only and ignores unknown sections.
    /// The [Reads] section uses bare cycle counts (one cycle count per read).
    /// </summary>
    private void WriteBcl2FastqSampleSheet(string path)
    {
        File.WriteAllText(path, $"""
            [Header]
            IEMFileVersion,4
            Date,2026-01-01
            Workflow,GenerateFASTQ
            Application,FASTQ Only

            [Reads]
            {SampleDataRead1Cycles}
            {SampleDataIndexCycles}
            {SampleDataRead2Cycles}

            [Data]
            Sample_ID,Sample_Name
            BenchSample,BenchSample
            """);
    }

    private static async IAsyncEnumerable<Sequence> ToSequences(
        IAsyncEnumerable<ReadData[]> readData,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var data in readData.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            yield return ToSequence(data);
        }
    }

    private readonly record struct FastqDirectorySummary(
        int FastqFileCount,
        long ReadCount,
        long TotalBases,
        long TotalCompressedBytes);
}


