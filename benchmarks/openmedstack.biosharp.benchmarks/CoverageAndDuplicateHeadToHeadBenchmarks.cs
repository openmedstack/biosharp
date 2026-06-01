namespace OpenMedStack.BioSharp.Benchmarks;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using OpenMedStack.BioSharp.Calculations.Alignment;
using OpenMedStack.BioSharp.Io.Sam;
using OpenMedStack.BioSharp.Model;

[MemoryDiagnoser]
[MarkdownExporterAttribute.GitHub]
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
