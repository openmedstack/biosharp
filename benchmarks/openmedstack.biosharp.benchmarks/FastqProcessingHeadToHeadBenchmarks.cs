using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using OpenMedStack.BioSharp.Calculations;
using OpenMedStack.BioSharp.Io.FastQ;
using OpenMedStack.BioSharp.Model;

namespace OpenMedStack.BioSharp.Benchmarks;

[MarkdownExporterAttribute.GitHub]
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net10_0, warmupCount: 3, iterationCount: 10)]
public class FastqProcessingHeadToHeadBenchmarks
{
    private const int ThreadCount = 10;
    private const string Adapter = AdapterTrimmer.Presets.TruSeqR1;

    private Sequence[] _reads = null!;
    private string _tempDir = null!;
    private string _fastqPath = null!;
    private bool _fastqcAvailable;
    private bool _fastpAvailable;
    private bool _cutadaptAvailable;
    private string? _preatorDll;
    private string? _preatorPublishError;

    [Params(10_000)] public int ReadCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"biosharp-fastq-bench-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        _reads = GenerateReads(ReadCount, readLength: 150);
        _fastqPath = Path.Combine(_tempDir, "reads.fastq.gz");
        WriteFastqGz(_fastqPath, _reads);

        _fastqcAvailable = ExternalProcess.IsAvailable("fastqc");
        _fastpAvailable = ExternalProcess.IsAvailable("fastp");
        _cutadaptAvailable = ExternalProcess.IsAvailable("cutadapt");

        // Publish the preator binary once (shared across all benchmark classes in this process).
        _preatorDll = PreatorPublisher.GetPreatorDll();
        _preatorPublishError = PreatorPublisher.GetPublishError();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    [Benchmark(Baseline = true, Description = "BioSharp-FastQ-QC")]
    [BenchmarkCategory("QualityControl", "BioSharp")]
    public async Task<long> BioSharp_FastQQualityReport()
    {
        var report = await FastQQualityReport.Compute(ReadSequences(), adapterSequence: Adapter).ConfigureAwait(false);
        return report.TotalBases + report.TotalReads + report.PerBaseQuality.Count + report.GcContentHistogram.Count;
    }

    [Benchmark(Description = "BioSharp-AdapterTrim")]
    [BenchmarkCategory("Trimming", "BioSharp")]
    public long BioSharp_AdapterTrim()
    {
        var trimmer = new AdapterTrimmer(Adapter, maxMismatches: 2, minLength: 20);
        var result = trimmer.TrimAll(_reads);
        return result.Stats.BasesRemoved + result.Stats.ReadsTrimmed + result.Reads.Count(read => read != null);
    }

    [Benchmark(Description = "BioSharp-FastQ-QC+AdapterTrim+FastqWrite")]
    [BenchmarkCategory("QualityControl", "Trimming", "BioSharp", "Comparable")]
    public async Task<long> BioSharp_FastQ_QcTrimAndWrite()
    {
        var trimmer = new AdapterTrimmer(Adapter, maxMismatches: 2, minLength: 20);
        await using var output = new MemoryStream();
        await using var index = new MemoryStream();
        await using var writer = new FastQWriter(
            Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance,
            output,
            index,
            CompressionLevel.Fastest,
            leaveOpen: true);

        var result = await FastQQualityReport.ComputeTrimAndWrite(
            ReadSequences(),
            trimmer,
            writer,
            Adapter,
            CancellationToken.None).ConfigureAwait(false);

        return output.Length + result.QualityReport.TotalBases + result.TrimStats.BasesRemoved +
            result.TrimStats.ReadsTrimmed + result.ReadsWritten + result.FastqBytesWritten;
    }

    [Benchmark(Description = "fastqc (subprocess)")]
    [BenchmarkCategory("QualityControl", "External")]
    public long FastQc_Subprocess()
    {
        if (!_fastqcAvailable)
        {
            throw new InvalidOperationException(
                "fastqc is not installed on PATH. The FASTQ QC head-to-head benchmark cannot be run apples-to-apples on this machine.");
        }

        var outDir = Path.Combine(_tempDir, $"fastqc_{Guid.NewGuid():N}");
        var logPath = Path.Combine(_tempDir, $"fastqc_{Guid.NewGuid():N}.stderr.log");
        Directory.CreateDirectory(outDir);
        try
        {
            var exit = ExternalProcess.Shell(
                $"fastqc --quiet --outdir \"{outDir}\" \"{_fastqPath}\" > {ExternalProcess.NullDevice} 2> \"{logPath}\"",
                _tempDir,
                120_000);
            if (exit != 0)
            {
                throw new InvalidOperationException(
                    $"fastqc exited with code {exit}. STDERR: {ReadExternalLog(logPath)}");
            }

            return Directory.EnumerateFiles(outDir, "*", SearchOption.AllDirectories)
                .Sum(path => new FileInfo(path).Length);
        }
        finally
        {
            if (Directory.Exists(outDir))
            {
                Directory.Delete(outDir, recursive: true);
            }

            DeleteIfExists(logPath);
        }
    }

    [Benchmark(Description = "fastp (subprocess)")]
    [BenchmarkCategory("QualityControl", "Trimming", "External")]
    public long FastP_Subprocess()
    {
        if (!_fastpAvailable)
        {
            throw new InvalidOperationException(
                "fastp is not installed on PATH. The FASTQ QC/trimming head-to-head benchmark cannot be run apples-to-apples on this machine.");
        }

        var outPath = Path.Combine(_tempDir, $"fastp_{Guid.NewGuid():N}.fastq.gz");
        var jsonPath = Path.Combine(_tempDir, $"fastp_{Guid.NewGuid():N}.json");
        var htmlPath = Path.Combine(_tempDir, $"fastp_{Guid.NewGuid():N}.html");
        var logPath = Path.Combine(_tempDir, $"fastp_{Guid.NewGuid():N}.stderr.log");
        try
        {
            var exit = ExternalProcess.Shell(
                $"fastp -w {ThreadCount} -i \"{_fastqPath}\" -o \"{outPath}\" -j \"{jsonPath}\" -h \"{htmlPath}\" > {ExternalProcess.NullDevice} 2> \"{logPath}\"",
                _tempDir,
                120_000);
            if (exit != 0)
            {
                throw new InvalidOperationException(
                    $"fastp exited with code {exit}. STDERR: {ReadExternalLog(logPath)}");
            }

            if (!File.Exists(outPath))
            {
                throw new InvalidOperationException("fastp completed but produced no FASTQ output.");
            }

            return new FileInfo(outPath).Length + ExistingFileLength(jsonPath) + ExistingFileLength(htmlPath);
        }
        finally
        {
            DeleteIfExists(outPath);
            DeleteIfExists(jsonPath);
            DeleteIfExists(htmlPath);
            DeleteIfExists(logPath);
        }
    }

    [Benchmark(Description = "cutadapt (subprocess)")]
    [BenchmarkCategory("Trimming", "External")]
    public long Cutadapt_Subprocess()
    {
        if (!_cutadaptAvailable)
        {
            throw new InvalidOperationException(
                "cutadapt is not installed on PATH. The adapter-trimming head-to-head benchmark cannot be run apples-to-apples on this machine.");
        }

        var outPath = Path.Combine(_tempDir, $"cutadapt_{Guid.NewGuid():N}.fastq.gz");
        var logPath = Path.Combine(_tempDir, $"cutadapt_{Guid.NewGuid():N}.stderr.log");
        try
        {
            var exit = ExternalProcess.Shell(
                $"cutadapt -j {ThreadCount} -a {Adapter} -m 20 -o \"{outPath}\" \"{_fastqPath}\" > {ExternalProcess.NullDevice} 2> \"{logPath}\"",
                _tempDir,
                120_000);
            if (exit != 0)
            {
                throw new InvalidOperationException(
                    $"cutadapt exited with code {exit}. STDERR: {ReadExternalLog(logPath)}");
            }

            if (!File.Exists(outPath))
            {
                throw new InvalidOperationException("cutadapt completed but produced no FASTQ output.");
            }

            return new FileInfo(outPath).Length;
        }
        finally
        {
            DeleteIfExists(outPath);
            DeleteIfExists(logPath);
        }
    }

    /// <summary>
    /// preator (compiled subprocess): run <c>preator qc</c> on the same FASTQ file.
    /// Uses the published, pre-compiled preator binary — measured exactly like external tools.
    /// Includes process start, .NET runtime load, and full QC computation.
    /// </summary>
    [Benchmark(Description = "preator-qc (subprocess)")]
    [BenchmarkCategory("QualityControl", "Preator")]
    public long Preator_Qc_Subprocess()
    {
        if (_preatorDll == null)
        {
            throw new InvalidOperationException(
                $"preator binary is not available: {_preatorPublishError}");
        }

        var outDir = Path.Combine(_tempDir, $"preator_qc_{Guid.NewGuid():N}");
        Directory.CreateDirectory(outDir);
        try
        {
            var runningInContainer = Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") == "true";
            var exit = runningInContainer
                ? ExternalProcess.Run(
                    "/app/preator/preator",
                    $"qc --fastq \"{_fastqPath}\" --adapter \"{Adapter}\" --output-dir \"{outDir}\" --output-prefix qc",
                    _tempDir)
                : ExternalProcess.Run(
                    "dotnet",
                    $"\"{_preatorDll}\" qc --fastq \"{_fastqPath}\" --adapter \"{Adapter}\" --output-dir \"{outDir}\" --output-prefix qc",
                    _tempDir);
            if (exit != 0)
            {
                throw new InvalidOperationException($"preator qc exited with code {exit}.");
            }

            return Directory.EnumerateFiles(outDir, "*", SearchOption.AllDirectories)
                .Sum(path => new FileInfo(path).Length);
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
    /// preator (compiled subprocess): run <c>preator trim</c> on the same FASTQ file.
    /// Equivalent to the <c>BioSharp-FastQ-QC+AdapterTrim+FastqWrite</c> benchmark but
    /// measured as a published binary subprocess, directly comparable to fastp/cutadapt timings.
    /// </summary>
    [Benchmark(Description = "preator-trim (subprocess)")]
    [BenchmarkCategory("Trimming", "Preator", "Comparable")]
    public long Preator_Trim_Subprocess()
    {
        if (_preatorDll == null)
        {
            throw new InvalidOperationException(
                $"preator binary is not available: {_preatorPublishError}");
        }

        var outDir = Path.Combine(_tempDir, $"preator_trim_{Guid.NewGuid():N}");
        Directory.CreateDirectory(outDir);
        try
        {
            var runningInContainer = Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") == "true";
            var exit = runningInContainer
                ? ExternalProcess.Run(
                    "/app/preator/preator",
                    $"trim --fastq \"{_fastqPath}\" --adapter \"{Adapter}\" --min-length 20 --max-mismatches 2 --output \"{outDir}\" --output-prefix trimmed",
                    _tempDir)
                : ExternalProcess.Run(
                    "dotnet",
                    $"\"{_preatorDll}\" trim --fastq \"{_fastqPath}\" --adapter \"{Adapter}\" --min-length 20 --max-mismatches 2 --output \"{outDir}\" --output-prefix trimmed",
                    _tempDir);
            if (exit != 0)
            {
                throw new InvalidOperationException($"preator trim exited with code {exit}.");
            }

            var fastqOut = Path.Combine(outDir, "trimmed.fastq.gz");
            if (!File.Exists(fastqOut))
            {
                throw new InvalidOperationException("preator trim completed but produced no FASTQ output.");
            }

            return new FileInfo(fastqOut).Length;
        }
        finally
        {
            if (Directory.Exists(outDir))
            {
                Directory.Delete(outDir, recursive: true);
            }
        }
    }

    private async IAsyncEnumerable<Sequence> ReadSequences()
    {
        foreach (var read in _reads)
        {
            yield return read;
        }

        await Task.CompletedTask;
    }

    private static Sequence[] GenerateReads(int count, int readLength)
    {
        const string bases = "ACGT";
        var random = new Random(123);
        var reads = new Sequence[count];
        for (var i = 0; i < count; i++)
        {
            var sequence = new char[readLength];
            for (var j = 0; j < readLength; j++)
            {
                sequence[j] = bases[random.Next(bases.Length)];
            }

            if (i % 4 == 0)
            {
                var insertAt = readLength - 32;
                Adapter.AsSpan(0, Math.Min(32, Adapter.Length)).CopyTo(sequence.AsSpan(insertAt));
            }

            var qualities = new char[readLength];
            for (var j = 0; j < qualities.Length; j++)
            {
                qualities[j] = (char)('!' + 25 + (j % 12));
            }

            reads[i] = new Sequence($"INST:1:FLOW:1:1101:{1000 + i}:{2000 + i}", sequence, qualities);
        }

        return reads;
    }

    private static void WriteFastqGz(string path, IReadOnlyList<Sequence> reads)
    {
        using var file = File.Create(path);
        using var gzip = new GZipStream(file, CompressionLevel.Fastest);
        using var writer = new StreamWriter(gzip, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        foreach (var read in reads)
        {
            writer.WriteLine($"@{read.Id}");
            writer.WriteLine(new string(read.GetData().Span));
            writer.WriteLine("+");
            writer.WriteLine(new string(read.GetQuality().Span));
        }
    }

    private static long ExistingFileLength(string path) => File.Exists(path) ? new FileInfo(path).Length : 0;

    private static string ReadExternalLog(string path)
    {
        if (!File.Exists(path))
        {
            return string.Empty;
        }

        var log = File.ReadAllText(path);
        return log.Length <= 4096 ? log : log[..4096];
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
