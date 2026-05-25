namespace OpenMedStack.BioSharp.Benchmarks;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using OpenMedStack.BioSharp.Calculations;
using OpenMedStack.BioSharp.Calculations.Alignment;
using OpenMedStack.BioSharp.Calculations.DeBruijn;
using OpenMedStack.BioSharp.Io.FastQ;
using OpenMedStack.BioSharp.Io.Sam;
using OpenMedStack.BioSharp.Model;
using OpenMedStack.BioSharp.Model.Alignment;

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

    [Params(10_000)]
    public int ReadCount { get; set; }

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
            throw new InvalidOperationException("fastqc is not installed on PATH. The FASTQ QC head-to-head benchmark cannot be run apples-to-apples on this machine.");
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
                throw new InvalidOperationException($"fastqc exited with code {exit}. STDERR: {ReadExternalLog(logPath)}");
            }

            return Directory.EnumerateFiles(outDir, "*", SearchOption.AllDirectories).Sum(path => new FileInfo(path).Length);
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
            throw new InvalidOperationException("fastp is not installed on PATH. The FASTQ QC/trimming head-to-head benchmark cannot be run apples-to-apples on this machine.");
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
                throw new InvalidOperationException($"fastp exited with code {exit}. STDERR: {ReadExternalLog(logPath)}");
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
            throw new InvalidOperationException("cutadapt is not installed on PATH. The adapter-trimming head-to-head benchmark cannot be run apples-to-apples on this machine.");
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
                throw new InvalidOperationException($"cutadapt exited with code {exit}. STDERR: {ReadExternalLog(logPath)}");
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

[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net10_0, warmupCount: 3, iterationCount: 10)]
public class CoverageAndDuplicateHeadToHeadBenchmarks
{
    private const int ThreadCount = 10;
    private const int ReferenceLength = 20_000;
    private const int ReadLength = 100;

    private IReadOnlyList<AlignmentSection> _alignments = null!;
    private IReadOnlyList<BedInterval> _targetIntervals = null!;
    private string _tempDir = null!;
    private string _referencePath = null!;
    private string _coordinateSortedBamPath = null!;
    private bool _samtoolsAvailable;

    [Params(5_000)]
    public int AlignmentCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"biosharp-depth-dup-bench-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        _alignments = GenerateAlignments(AlignmentCount);
        _targetIntervals =
        [
            new BedInterval { Chrom = "chrSynth", Start = 0, End = ReferenceLength / 2 },
            new BedInterval { Chrom = "chrSynth", Start = ReferenceLength / 2, End = ReferenceLength }
        ];
        _referencePath = Path.Combine(_tempDir, "ref.fa");
        WriteReference(_referencePath, ReferenceLength);

        _samtoolsAvailable = ExternalProcess.IsAvailable("samtools");
        if (_samtoolsAvailable)
        {
            PrepareSamtoolsBam();
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

    [Benchmark(Baseline = true, Description = "BioSharp-CoverageCalculator")]
    [BenchmarkCategory("Coverage", "BioSharp")]
    public double BioSharp_CoverageCalculator()
    {
        var report = CoverageCalculator.Compute(_alignments, ReferenceLength, _targetIntervals);
        return report.MeanDepth + report.MedianDepth + report.CoefficientOfVariation + report.FractionAt10x;
    }

    [Benchmark(Description = "samtools-depth (subprocess)")]
    [BenchmarkCategory("Coverage", "External")]
    public long SamtoolsDepth_Subprocess()
    {
        EnsureSamtoolsPrepared();
        var depthPath = Path.Combine(_tempDir, $"depth_{Guid.NewGuid():N}.txt");
        try
        {
            var command = $"samtools depth -a \"{_coordinateSortedBamPath}\" > \"{depthPath}\"";
            var exit = ExternalProcess.Shell(command, _tempDir, 120_000);
            if (exit != 0)
            {
                throw new InvalidOperationException($"samtools depth exited with code {exit}.");
            }

            return File.ReadLines(depthPath).LongCount();
        }
        finally
        {
            if (File.Exists(depthPath))
            {
                File.Delete(depthPath);
            }
        }
    }

    [Benchmark(Description = "BioSharp-DuplicateMarker")]
    [BenchmarkCategory("DuplicateMarking", "BioSharp")]
    public long BioSharp_DuplicateMarker()
    {
        var (_, metrics) = DuplicateMarker.MarkDuplicates(_alignments);
        return metrics.DuplicateReads + metrics.OpticalDuplicateReads + metrics.TotalReads;
    }

    [Benchmark(Description = "samtools-markdup (subprocess)")]
    [BenchmarkCategory("DuplicateMarking", "External")]
    public long SamtoolsMarkdup_Subprocess()
    {
        EnsureSamtoolsPrepared();
        var outPath = Path.Combine(_tempDir, $"markdup_{Guid.NewGuid():N}.bam");
        try
        {
            var exit = ExternalProcess.Run(
                "samtools",
                $"markdup -@ {ThreadCount} \"{_coordinateSortedBamPath}\" \"{outPath}\"",
                _tempDir,
                120_000);
            if (exit != 0)
            {
                throw new InvalidOperationException($"samtools markdup exited with code {exit}.");
            }

            return new FileInfo(outPath).Length;
        }
        finally
        {
            if (File.Exists(outPath))
            {
                File.Delete(outPath);
            }
        }
    }

    private void EnsureSamtoolsPrepared()
    {
        if (!_samtoolsAvailable)
        {
            throw new InvalidOperationException("samtools is not installed on PATH. The coverage/depth and duplicate-marking external benchmarks cannot be run apples-to-apples on this machine.");
        }

        if (string.IsNullOrEmpty(_coordinateSortedBamPath) || !File.Exists(_coordinateSortedBamPath))
        {
            throw new InvalidOperationException("samtools input BAM was not prepared successfully.");
        }
    }

    private void PrepareSamtoolsBam()
    {
        var samPath = Path.Combine(_tempDir, "alignments.sam");
        var unsortedBam = Path.Combine(_tempDir, "alignments.bam");
        var nameSortedBam = Path.Combine(_tempDir, "alignments.name.bam");
        var fixmateBam = Path.Combine(_tempDir, "alignments.fixmate.bam");
        _coordinateSortedBamPath = Path.Combine(_tempDir, "alignments.sorted.bam");

        WriteSam(samPath, _alignments);
        RunSamtoolsOrThrow($"faidx \"{_referencePath}\"");
        RunSamtoolsOrThrow($"view -@ {ThreadCount} -bS \"{samPath}\" -o \"{unsortedBam}\"");
        RunSamtoolsOrThrow($"sort -@ {ThreadCount} -n \"{unsortedBam}\" -o \"{nameSortedBam}\"");
        RunSamtoolsOrThrow($"fixmate -@ {ThreadCount} -m \"{nameSortedBam}\" \"{fixmateBam}\"");
        RunSamtoolsOrThrow($"sort -@ {ThreadCount} \"{fixmateBam}\" -o \"{_coordinateSortedBamPath}\"");
        RunSamtoolsOrThrow($"index \"{_coordinateSortedBamPath}\"");
    }

    private static IReadOnlyList<AlignmentSection> GenerateAlignments(int count)
    {
        var alignments = new List<AlignmentSection>(count);
        const string bases = "ACGT";
        var sequence = string.Concat(Enumerable.Range(0, ReadLength).Select(i => bases[i % bases.Length]));
        var quality = new string('I', ReadLength);
        for (var i = 0; i < count; i++)
        {
            var template = i / 2;
            var duplicateGroup = template % 10 == 0 ? template - template % 4 : template;
            var position = 1 + Math.Abs((duplicateGroup * 37) % (ReferenceLength - ReadLength - 1));
            var flag = AlignmentSection.AlignmentFlag.MultipleSegments |
                       AlignmentSection.AlignmentFlag.EachSegmentProperlyAligned |
                       (i % 2 == 0
                           ? AlignmentSection.AlignmentFlag.FirstSegmentInTemplate
                           : AlignmentSection.AlignmentFlag.LastSegmentInTemplate);
            var name = $"INST:1:FLOW:1:1101:{1000 + template % 200}:{2000 + template % 200}";
            alignments.Add(new AlignmentSection(
                name,
                flag,
                "chrSynth",
                position,
                (byte)(60 - i % 20),
                [((uint)ReadLength, CigarOp.Match)],
                0,
                position + ReadLength + 50,
                ReadLength + 50,
                sequence,
                quality,
                Array.Empty<AlignmentTag>(),
                0));
        }

        return alignments;
    }

    private static void WriteReference(string path, int length)
    {
        const string bases = "ACGT";
        using var writer = new StreamWriter(path);
        writer.WriteLine(">chrSynth");
        for (var i = 0; i < length; i += 80)
        {
            var lineLength = Math.Min(80, length - i);
            var chars = new char[lineLength];
            for (var j = 0; j < chars.Length; j++)
            {
                chars[j] = bases[(i + j) % bases.Length];
            }

            writer.WriteLine(chars);
        }
    }

    private static void WriteSam(string path, IReadOnlyList<AlignmentSection> alignments)
    {
        using var writer = new StreamWriter(path);
        writer.WriteLine("@HD\tVN:1.6\tSO:unsorted");
        writer.WriteLine($"@SQ\tSN:chrSynth\tLN:{ReferenceLength.ToString(CultureInfo.InvariantCulture)}");
        foreach (var alignment in alignments)
        {
            writer.WriteLine(string.Join('\t',
                alignment.QName,
                ((ushort)alignment.Flag).ToString(CultureInfo.InvariantCulture),
                alignment.ReadName,
                alignment.Position.ToString(CultureInfo.InvariantCulture),
                alignment.MappingQuality.ToString(CultureInfo.InvariantCulture),
                $"{ReadLength}M",
                "=",
                alignment.NextPosition.ToString(CultureInfo.InvariantCulture),
                alignment.TemplateLength.ToString(CultureInfo.InvariantCulture),
                alignment.Sequence,
                alignment.Quality));
        }
    }

    private void RunSamtoolsOrThrow(string arguments)
    {
        var exit = ExternalProcess.Run("samtools", arguments, _tempDir, 120_000);
        if (exit != 0)
        {
            throw new InvalidOperationException($"samtools {arguments} exited with code {exit}.");
        }
    }
}

[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net10_0, warmupCount: 3, iterationCount: 10)]
public class RepeatMaskingHeadToHeadBenchmarks
{
    private const int ThreadCount = 10;

    private string _tempDir = null!;
    private string _sequence = null!;
    private string _fastaPath = null!;
    private IList<RepeatElement> _repeatLibrary = null!;
    private bool _repeatMaskerAvailable;
    private bool _trfAvailable;

    [Params(50_000)]
    public int SequenceLength { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"biosharp-repeat-bench-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _sequence = BuildRepeatRichSequence(SequenceLength);
        _fastaPath = Path.Combine(_tempDir, "repeat-rich.fa");
        WriteFasta(_fastaPath, _sequence);
        _repeatLibrary = BuildRepeatLibrary();
        _repeatMaskerAvailable = ExternalProcess.IsAvailable("RepeatMasker");
        _trfAvailable = ExternalProcess.IsAvailable("trf");
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    [Benchmark(Baseline = true, Description = "BioSharp-RepeatMasker")]
    [BenchmarkCategory("RepeatMasking", "BioSharp")]
    public async Task<int> BioSharp_RepeatMasker()
    {
        // Validate that every motif in the library appears at least once in the sequence
        foreach (var motif in _repeatLibrary.Select(r => r.Motif))
        {
            if (!_sequence.Contains(motif, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Repeat motif '{motif}' from the library is missing in the synthetic sequence. Fixture or library mismatch.");
            }
        }

        var regions = await OpenMedStack.BioSharp.Calculations.DeBruijn.RepeatMasker.MaskRepeats(_sequence, _repeatLibrary).ConfigureAwait(false);
        if (regions.Count == 0)
        {
            throw new InvalidOperationException("BioSharp-RepeatMasker found no masked regions. This likely indicates a bug in motif matching or a fixture/library mismatch.");
        }
        return regions.Count;
    }

    [Benchmark(Description = "RepeatMasker (subprocess)")]
    [BenchmarkCategory("RepeatMasking", "External")]
    public long RepeatMasker_Subprocess()
    {
        if (!_repeatMaskerAvailable)
        {
            throw new InvalidOperationException("RepeatMasker is not installed on PATH. The repeat-masking head-to-head benchmark cannot be run apples-to-apples on this machine.");
        }

        var outDir = Path.Combine(_tempDir, $"repeatmasker_{Guid.NewGuid():N}");
        Directory.CreateDirectory(outDir);
        try
        {
            var exit = ExternalProcess.Run("RepeatMasker", $"-pa {ThreadCount} -dir \"{outDir}\" \"{_fastaPath}\"", _tempDir, 300_000);
            if (exit != 0)
            {
                throw new InvalidOperationException($"RepeatMasker exited with code {exit}.");
            }

            return Directory.EnumerateFiles(outDir, "*", SearchOption.AllDirectories).Sum(path => new FileInfo(path).Length);
        }
        finally
        {
            if (Directory.Exists(outDir))
            {
                Directory.Delete(outDir, recursive: true);
            }
        }
    }

    [Benchmark(Description = "trf (subprocess)")]
    [BenchmarkCategory("RepeatMasking", "External")]
    public long Trf_Subprocess()
    {
        if (!_trfAvailable)
        {
            throw new InvalidOperationException("trf is not installed on PATH. The tandem-repeat head-to-head benchmark cannot be run apples-to-apples on this machine.");
        }

        var outDir = Path.Combine(_tempDir, $"trf_{Guid.NewGuid():N}");
        Directory.CreateDirectory(outDir);
        try
        {
            var fastaCopy = Path.Combine(outDir, Path.GetFileName(_fastaPath));
            File.Copy(_fastaPath, fastaCopy);
            var exit = ExternalProcess.Run("trf", $"\"{fastaCopy}\" 2 7 7 80 10 50 500 -d -h", outDir, 120_000);
            if (exit != 0)
            {
                throw new InvalidOperationException($"trf exited with code {exit}.");
            }

            return Directory.EnumerateFiles(outDir, "*", SearchOption.AllDirectories).Sum(path => new FileInfo(path).Length);
        }
        finally
        {
            if (Directory.Exists(outDir))
            {
                Directory.Delete(outDir, recursive: true);
            }
        }
    }

    private static string BuildRepeatRichSequence(int length)
    {
        var motifs = new[] { "ACACACACACAC", "GATTACAGATTACA", "TTAGGGTTAGGG", "CGGCGGCGG" };
        var builder = new StringBuilder(length);
        var random = new Random(77);
        const string bases = "ACGT";
        while (builder.Length < length)
        {
            if (builder.Length % 997 < 120)
            {
                builder.Append(motifs[(builder.Length / 997) % motifs.Length]);
            }
            else
            {
                builder.Append(bases[random.Next(bases.Length)]);
            }
        }

        return builder.ToString(0, length);
    }

    private static IList<RepeatElement> BuildRepeatLibrary()
    {
        return
        [
            new RepeatElement { Id = "AC-dinucleotide", Name = "AC dinucleotide", Motif = "ACACACACACAC", Type = "tandem" },
            new RepeatElement { Id = "GATTACA", Name = "GATTACA repeat", Motif = "GATTACAGATTACA", Type = "tandem" },
            new RepeatElement { Id = "telomeric", Name = "TTAGGG repeat", Motif = "TTAGGGTTAGGG", Type = "tandem" },
            new RepeatElement { Id = "CGG", Name = "CGG trinucleotide", Motif = "CGGCGGCGG", Type = "tandem" }
        ];
    }

    private static void WriteFasta(string path, string sequence)
    {
        using var writer = new StreamWriter(path);
        writer.WriteLine(">repeat-rich");
        for (var i = 0; i < sequence.Length; i += 80)
        {
            writer.WriteLine(sequence.AsSpan(i, Math.Min(80, sequence.Length - i)));
        }
    }
}
