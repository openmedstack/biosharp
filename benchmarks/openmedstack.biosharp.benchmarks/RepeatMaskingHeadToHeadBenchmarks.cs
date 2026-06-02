using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using OpenMedStack.BioSharp.Calculations.DeBruijn;

namespace OpenMedStack.BioSharp.Benchmarks;

[MemoryDiagnoser]
[MarkdownExporterAttribute.GitHub]
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
    private string _repeatLibraryJsonPath = null!;
    private string? _preatorPublishError;

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

        // Serialize repeat library in the {"Repeats":[...]} wrapper format expected by
        // RepeatMasker.LoadLibrary (which deserializes into the internal RepeatLibrary class).
        _repeatLibraryJsonPath = Path.Combine(_tempDir, "repeats.json");
        File.WriteAllText(_repeatLibraryJsonPath, JsonSerializer.Serialize(new { Repeats = _repeatLibrary }));
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

    [Benchmark(Description = "preator-repeatmask (subprocess)")]
    [BenchmarkCategory("RepeatMasking", "External")]
    public long PreatorRepeatMask_Subprocess()
    {
        if (_preatorPublishError != null)
        {
            throw new InvalidOperationException($"preator is not available: {_preatorPublishError}");
        }

        var outDir = Path.Combine(_tempDir, $"preator-repeatmask-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(outDir);
            var exit = PreatorPublisher.Run(
                $"repeatmask --fasta \"{_fastaPath}\" --library \"{_repeatLibraryJsonPath}\" --output \"{outDir}\" --output-prefix masked",
                _tempDir,
                300_000);
            if (exit != 0)
            {
                throw new InvalidOperationException($"preator repeatmask exited with code {exit}.");
            }

            return Directory.EnumerateFiles(outDir).Sum(f => new FileInfo(f).Length);
        }
        finally
        {
            if (Directory.Exists(outDir))
            {
                Directory.Delete(outDir, recursive: true);
            }
        }
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
