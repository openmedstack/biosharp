namespace OpenMedStack.BioSharp.Benchmarks;

using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using OpenMedStack.BioSharp.Calculations.Alignment;
using OpenMedStack.BioSharp.Io.Bam;
using OpenMedStack.BioSharp.Model;

internal static class VariantCallingSmokeCommand
{
    public static async Task<int> Run(string[] args)
    {
        var repoRoot = FindRepoRoot() ?? Directory.GetCurrentDirectory();
        var bamPath = args.Length > 0 ? args[0] : Path.Combine(repoRoot, "data", "small_test_sorted.bam");
        var fastaPath = args.Length > 1 ? args[1] : Path.Combine(repoRoot, "data", "small_test.fasta");
        var reference = LoadFasta(fastaPath);

        Console.WriteLine($"BAM: {bamPath}");
        Console.WriteLine($"Reference: {reference.Id} {reference.Length} bp");

        var readStopwatch = Stopwatch.StartNew();
        var reader = new BamReader(bamPath, NullLogger<BamReader>.Instance);
        var streamed = 0;
        await foreach (var _ in reader.ReadAlignmentSections(CancellationToken.None))
        {
            streamed++;
            if (streamed % 1000 == 0)
            {
                Console.WriteLine($"streamed={streamed}");
            }
        }

        readStopwatch.Stop();
        Console.WriteLine($"streamed_total={streamed} read_ms={readStopwatch.Elapsed.TotalMilliseconds:F3}");

        var pipeline = new VariantCallingPipeline(
            ReferenceAlignmentContext.GetShared(reference),
            reference.Id,
            new VariantCallingPipeline.PipelineOptions
            {
                KmerSize = 31,
                MinAlignmentScore = 20,
                MinAlternateFraction = 0.15,
                MinAlternateObservationCount = 2,
                EnableGraphSvDetection = false,
                EnableSoftClipRealignment = false,
                UseBamAlignmentCoordinates = true,
                DegreeOfParallelism = 10
            });

        var loadStopwatch = Stopwatch.StartNew();
        await pipeline.LoadBam(bamPath).ConfigureAwait(false);
        loadStopwatch.Stop();
        var result = pipeline.BuildResult();
        Console.WriteLine($"load_ms={loadStopwatch.Elapsed.TotalMilliseconds:F3}");
        Console.WriteLine($"records_read={pipeline.LastBamLoadProfile.RecordsRead}");
        Console.WriteLine($"records_accepted={pipeline.LastBamLoadProfile.RecordsAccepted}");
        Console.WriteLine($"bam_read_ms={pipeline.LastBamLoadProfile.BamReadMilliseconds:F3}");
        Console.WriteLine($"variant_call_ms={pipeline.LastBamLoadProfile.VariantCallingMilliseconds:F3}");
        Console.WriteLine($"reads={result.Metrics.ReadsProcessed} mapped={result.Metrics.ReadsMapped} raw={result.Metrics.VariantsCalled} final={result.Metrics.VariantsFinal}");
        return 0;
    }

    private static string? FindRepoRoot()
    {
        for (DirectoryInfo? current = new(AppContext.BaseDirectory); current != null; current = current.Parent)
        {
            if (File.Exists(Path.Combine(current.FullName, "openmedstack-biosharp.sln")))
            {
                return current.FullName;
            }
        }

        return null;
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

        var sequence = sb.ToString();
        return new Sequence(id, sequence.AsMemory(), new string('I', sequence.Length).AsMemory());
    }
}
