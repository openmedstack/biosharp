namespace OpenMedStack.BioSharp.Benchmarks;

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using OpenMedStack.BioSharp.AnnotationDb;
using OpenMedStack.BioSharp.Calculations;
using OpenMedStack.BioSharp.Model;

/// <summary>
/// Head-to-head variant-annotation benchmarks:
///   1. BioSharp in-process (<see cref="VariantAnnotationEngine"/> loaded from GTF)
///   2. <c>preator annotate</c> as a subprocess (uses the SQLite-backed engine)
///   3. <c>snpeff ann</c> as a subprocess (industry-standard tool)
///
/// All three tools annotate exactly the same synthetic VCF against the same synthetic
/// GTF gene model so results are directly comparable.
/// </summary>
[MarkdownExporterAttribute.GitHub]
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net10_0, warmupCount: 3, iterationCount: 10)]
public class AnnotateHeadToHeadBenchmarks
{
    private const int TranscriptCount = 20;
    private const int VariantCount = 100;
    private const int CdsOffset = 100;
    private const int TxLength = 1000;

    private string _tempDir = null!;
    private string _fastaPath = null!;
    private string _gtfPath = null!;
    private string _vcfPath = null!;
    private string _sqlitePath = null!;
    private string _snpeffConfigPath = null!;
    private string _snpeffDataDir = null!;

    private bool _snpeffAvailable;
    private string? _preatorDll;
    private string? _preatorPublishError;

    [GlobalSetup]
    public async Task Setup()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"biosharp-annotate-bench-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        // ── 1. Generate synthetic reference data ───────────────────────────
        _fastaPath = Path.Combine(_tempDir, "ref.fa");
        _gtfPath = Path.Combine(_tempDir, "genes.gtf");
        _vcfPath = Path.Combine(_tempDir, "variants.vcf");

        WriteSyntheticFasta(_fastaPath, TranscriptCount, TxLength);
        WriteSyntheticGtf(_gtfPath, TranscriptCount, TxLength, CdsOffset);
        WriteSyntheticVcf(_vcfPath, TranscriptCount, VariantCount);

        // ── 2. Build SQLite annotation database (used by preator annotate) ─
        _sqlitePath = Path.Combine(_tempDir, "annotations.db");
        await BuildSqliteDatabase(_sqlitePath, _gtfPath, _fastaPath);

        // ── 3. Build SnpEff custom database ───────────────────────────────
        _snpeffAvailable = ExternalProcess.IsAvailable("snpeff");
        if (_snpeffAvailable)
        {
            _snpeffDataDir = Path.Combine(_tempDir, "snpeff_data");
            _snpeffConfigPath = Path.Combine(_tempDir, "snpEff.config");
            BuildSnpEffDatabase();
        }

        // ── 4. Locate / publish the preator binary ────────────────────────
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

    // ── Benchmarks ────────────────────────────────────────────────────────

    /// <summary>BioSharp <see cref="VariantAnnotationEngine"/> loaded from GTF (in-process).</summary>
    [Benchmark(Baseline = true, Description = "biosharp-annotate (in-process)")]
    [BenchmarkCategory("VariantAnnotation", "BioSharp")]
    public int BioSharp_Annotate()
    {
        var engine = new VariantAnnotationEngine();
        engine.LoadTranscriptsFromGtf(_gtfPath, CancellationToken.None).GetAwaiter().GetResult();

        var count = 0;
        foreach (var _ in Task.Run(async () =>
        {
            var annotations = new List<VariantAnnotation>();
            await foreach (var a in engine.AnnotateVcf(_vcfPath).ConfigureAwait(false))
            {
                annotations.Add(a);
            }

            return annotations;
        }).GetAwaiter().GetResult())
        {
            count++;
        }

        return count;
    }

    /// <summary><c>preator annotate</c> subprocess (SQLite-backed).</summary>
    [Benchmark(Description = "preator-annotate (subprocess)")]
    [BenchmarkCategory("VariantAnnotation", "Preator")]
    public int Preator_Annotate_Subprocess()
    {
        if (_preatorDll == null)
        {
            throw new InvalidOperationException(
                $"preator binary is not available: {_preatorPublishError}");
        }

        var outDir = Path.Combine(_tempDir, $"preator_annotate_{Guid.NewGuid():N}");
        Directory.CreateDirectory(outDir);
        try
        {
            var runningInContainer = Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") == "true";
            var exit = runningInContainer
                ? ExternalProcess.Run(
                    "/app/preator/preator",
                    $"annotate" +
                    $" --vcf \"{_vcfPath}\"" +
                    $" --database \"{_sqlitePath}\"" +
                    $" --output \"{outDir}\"",
                    _tempDir,
                    300_000)
                : ExternalProcess.Run(
                    "dotnet",
                    $"\"{_preatorDll}\" annotate" +
                    $" --vcf \"{_vcfPath}\"" +
                    $" --database \"{_sqlitePath}\"" +
                    $" --output \"{outDir}\"",
                    _tempDir,
                    300_000);
            if (exit != 0)
            {
                throw new InvalidOperationException($"preator annotate exited with code {exit}.");
            }

            var outFile = Path.Combine(outDir, "annotated-variants.tsv");
            return File.Exists(outFile) ? (int)new FileInfo(outFile).Length : 0;
        }
        finally
        {
            if (Directory.Exists(outDir))
            {
                Directory.Delete(outDir, recursive: true);
            }
        }
    }

    /// <summary><c>snpeff ann</c> subprocess using a custom database built from the synthetic GTF.</summary>
    [Benchmark(Description = "snpeff-ann (subprocess)")]
    [BenchmarkCategory("VariantAnnotation", "SnpEff")]
    public int SnpEff_Subprocess()
    {
        if (!_snpeffAvailable)
        {
            throw new InvalidOperationException("snpeff is not available on PATH.");
        }

        var outVcf = Path.Combine(_tempDir, $"snpeff_out_{Guid.NewGuid():N}.vcf");
        try
        {
            var exit = ExternalProcess.Shell(
                $"snpeff ann -c \"{_snpeffConfigPath}\" -noLog synth \"{_vcfPath}\" > \"{outVcf}\" 2>&1",
                _tempDir,
                180_000);
            if (exit != 0)
            {
                throw new InvalidOperationException($"snpeff ann exited with code {exit}.");
            }

            if (!File.Exists(outVcf))
            {
                return 0;
            }

            var count = 0;
            foreach (var line in File.ReadLines(outVcf))
            {
                if (!line.StartsWith('#'))
                {
                    count++;
                }
            }

            return count;
        }
        finally
        {
            if (File.Exists(outVcf))
            {
                File.Delete(outVcf);
            }
        }
    }

    // ── Private helpers ───────────────────────────────────────────────────

    private static void WriteSyntheticFasta(string path, int transcriptCount, int txLength)
    {
        var bases = "ACGT";
        var sb = new StringBuilder();
        for (var t = 0; t < transcriptCount; t++)
        {
            sb.Append('>').AppendLine($"synth{t + 1}");
            for (var i = 0; i < txLength; i++)
            {
                sb.Append(bases[i % 4]);
            }

            sb.AppendLine();
        }

        File.WriteAllText(path, sb.ToString());
    }

    private static void WriteSyntheticGtf(string path, int transcriptCount, int txLength, int cdsOffset)
    {
        using var w = File.CreateText(path);
        for (var t = 0; t < transcriptCount; t++)
        {
            var chrom = $"synth{t + 1}";
            var txId = $"TX{t + 1:D3}";
            var cdsEnd = txLength - cdsOffset;
            w.WriteLine($"{chrom}\tSynth\ttranscript\t1\t{txLength}\t.\t+\t.\tgene_id \"{txId}\"; transcript_id \"{txId}\";");
            w.WriteLine($"{chrom}\tSynth\texon\t1\t{txLength}\t.\t+\t.\tgene_id \"{txId}\"; transcript_id \"{txId}\";");
            w.WriteLine($"{chrom}\tSynth\tCDS\t{cdsOffset}\t{cdsEnd}\t.\t+\t0\tgene_id \"{txId}\"; transcript_id \"{txId}\";");
        }
    }

    private static void WriteSyntheticVcf(string path, int transcriptCount, int variantCount)
    {
        using var w = File.CreateText(path);
        w.WriteLine("##fileformat=VCFv4.1");
        for (var t = 0; t < transcriptCount; t++)
        {
            w.WriteLine($"##contig=<ID=synth{t + 1}>");
        }

        w.WriteLine("#CHROM\tPOS\tID\tREF\tALT\tQUAL\tFILTER\tINFO");

        var altBases = new[] { "T", "C", "G", "T" };
        for (var v = 0; v < variantCount; v++)
        {
            var tIdx = v % transcriptCount;
            var pos = 50 + (v / transcriptCount * 3) + (tIdx * 10) + 1;
            w.WriteLine($"synth{tIdx + 1}\t{pos}\t.\tA\t{altBases[v % 4]}\t30\tPASS\t.");
        }
    }

    private static async Task BuildSqliteDatabase(string sqlitePath, string gtfPath, string fastaPath)
    {
        await using var ctx = new TranscriptAnnotationDbContext($"Data Source={sqlitePath}");
        var db = new TranscriptAnnotationDatabase(ctx);
        db.Initialize(CancellationToken.None).GetAwaiter().GetResult();

        var request = new TranscriptImportRequest(
            AnnotationPath: gtfPath,
            SequencePath: fastaPath,
            Assembly: "synth",
            SourceVersion: "1.0");

        db.Import(new GencodeTranscriptDatabaseImporter(), request, CancellationToken.None)
            .GetAwaiter().GetResult();
    }

    private void BuildSnpEffDatabase()
    {
        Directory.CreateDirectory(Path.Combine(_snpeffDataDir, "synth"));
        File.Copy(_fastaPath, Path.Combine(_snpeffDataDir, "synth", "sequences.fa"), overwrite: true);
        File.Copy(_gtfPath, Path.Combine(_snpeffDataDir, "synth", "genes.gtf"), overwrite: true);
        File.WriteAllText(_snpeffConfigPath, $"data.dir = {_snpeffDataDir}\nsynth.genome : Synthetic\n");

        var exit = ExternalProcess.Shell(
            $"snpeff build -gtf22 -c \"{_snpeffConfigPath}\" -v synth 2>&1",
            _tempDir,
            180_000);
        if (exit != 0)
        {
            throw new InvalidOperationException("snpeff build failed during benchmark setup.");
        }
    }
}
