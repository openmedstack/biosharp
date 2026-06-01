namespace OpenMedStack.BioSharp.Benchmarks;

using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using OpenMedStack.BioSharp.Io.Bcl;
using OpenMedStack.BioSharp.Io.FastQ;
using OpenMedStack.BioSharp.Model.Bcl;

/// <summary>
/// Runs BCL benchmarks on real Illumina run folder data (data/sampledata).
/// </summary>
internal static class BclRealDataCommand
{
    public static async Task<int> Run(string[] args)
    {
        // Parse arguments
        var runFolderPath = Path.Combine("data", "sampledata");
        var iterations = 3;
        var writeFastq = true;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--run-folder" when i + 1 < args.Length:
                    runFolderPath = args[++i];
                    break;
                case "--iterations" when i + 1 < args.Length:
                    iterations = int.Parse(args[++i], CultureInfo.InvariantCulture);
                    break;
                case "--no-fastq":
                    writeFastq = false;
                    break;
                case "--help":
                    PrintHelp();
                    return 0;
            }
        }

        if (!Directory.Exists(runFolderPath))
        {
            Console.WriteLine($"ERROR: Run folder not found: {runFolderPath}");
            return 1;
        }

        var runInfo = Path.Combine(runFolderPath, "RunInfo.xml");
        if (!File.Exists(runInfo))
        {
            Console.WriteLine($"ERROR: RunInfo.xml not found in {runFolderPath}");
            return 1;
        }

        Console.WriteLine("=== BCL Real Data Benchmark ===");
        Console.WriteLine($"Run folder: {Path.GetFullPath(runFolderPath)}");
        Console.WriteLine($"Iterations: {iterations}");
        Console.WriteLine($"Write FASTQ: {writeFastq}");
        Console.WriteLine();

        // Count BCL files
        var baseCallsPath = Path.Combine(runFolderPath, "Data", "Intensities", "BaseCalls");
        var bclFiles = Directory.GetFiles(baseCallsPath, "*.bcl.gz", SearchOption.AllDirectories)
            .Concat(Directory.GetFiles(baseCallsPath, "*.bcl", SearchOption.AllDirectories))
            .ToArray();
        var totalBclSize = bclFiles.Sum(f => new FileInfo(f).Length);
        Console.WriteLine($"BCL files: {bclFiles.Length}");
        Console.WriteLine($"Total BCL size: {totalBclSize / 1024.0 / 1024.0:F1} MB");
        Console.WriteLine();

        // Create output directory
        var tempDir = Path.Combine(Path.GetTempPath(), $"biosharp-bcl-bench-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            // Warm up
            Console.WriteLine("--- Warmup ---");
            var (warmupReads, warmupBytes, _) = await RunBioSharpBcl(runFolderPath, tempDir, writeFastq);
            Console.WriteLine($"Warmup: {warmupReads:N0} reads, {warmupBytes / 1024.0 / 1024.0:F2} MB FASTQ");
            Console.WriteLine();

            // Benchmark iterations
            Console.WriteLine("--- BioSharp BCL Benchmark ---");
            var times = new double[iterations];
            long totalReads = 0;
            long totalBytes = 0;

            for (var i = 0; i < iterations; i++)
            {
                var iterDir = Path.Combine(tempDir, $"iter{i}");
                Directory.CreateDirectory(iterDir);

                var (reads, bytes, elapsed) = await RunBioSharpBcl(runFolderPath, iterDir, writeFastq);
                times[i] = elapsed.TotalSeconds;
                totalReads = reads;
                totalBytes = bytes;

                Console.WriteLine($"  Iteration {i + 1}: {elapsed.TotalSeconds:F3}s ({reads:N0} reads, {bytes / 1024.0 / 1024.0:F2} MB)");

                // Cleanup iteration output
                try { Directory.Delete(iterDir, recursive: true); } catch { }
            }

            Console.WriteLine();
            Console.WriteLine("--- Results ---");
            Console.WriteLine($"Mean:   {times.Average():F3}s");
            Console.WriteLine($"Min:    {times.Min():F3}s");
            Console.WriteLine($"Max:    {times.Max():F3}s");
            Console.WriteLine($"StdDev: {StandardDeviation(times):F3}s");
            Console.WriteLine();

            var throughputMBps = (totalBclSize / 1024.0 / 1024.0) / times.Average();
            var readsPerSec = totalReads / times.Average();
            Console.WriteLine($"Throughput: {throughputMBps:F2} MB/s (BCL input)");
            Console.WriteLine($"Throughput: {readsPerSec:N0} reads/s");
            Console.WriteLine();

            // Output CSV row for comparison
            Console.WriteLine("--- CSV Output ---");
            Console.WriteLine("Tool,RunFolder,Iterations,MeanMs,MinMs,MaxMs,Reads,FastqMB");
            Console.WriteLine($"BioSharp,{Path.GetFileName(runFolderPath)},{iterations},{times.Average() * 1000:F1},{times.Min() * 1000:F1},{times.Max() * 1000:F1},{totalReads},{totalBytes / 1024.0 / 1024.0:F2}");

            return 0;
        }
        finally
        {
            // Cleanup
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    private static async Task<(long reads, long bytes, TimeSpan elapsed)> RunBioSharpBcl(
        string runFolderPath, string outputDir, bool writeFastq)
    {
        var sw = Stopwatch.StartNew();
        var reader = new IlluminaDataReader(
            new DirectoryInfo(runFolderPath),
            NullLoggerFactory.Instance,
            ReadStructure.Parse("124T8B"));  // 26T8B98T for the sampledata run

        var runInfo = reader.RunInfo();
        var lanes = reader.GetAllLanes().ToList();

        long totalReads = 0;
        long totalBytes = 0;

        foreach (var lane in lanes)
        {
            var fastqPath = Path.Combine(outputDir, $"L{lane:D3}_R1.fastq.gz");
            var indexPath = Path.Combine(outputDir, $"L{lane:D3}_R1.fastqi.gz");

            if (writeFastq)
            {
                await using var fastqStream = File.Create(fastqPath);
                await using var indexStream = File.Create(indexPath);
                await using var writer = new FastQWriter(
                    NullLogger.Instance, fastqStream, indexStream,
                    CompressionLevel.Fastest, leaveOpen: true);

                await foreach (var sampleReader in reader.ReadClusterData(lane, CancellationToken.None))
                {
                    await using var sr = sampleReader;
                    // Count reads during write using a wrapper
                    await foreach (var sequence in sr.ReadBclData(DefaultQualityTrimmer.Instance, CancellationToken.None))
                    {
                        await writer.Write(sequence, CancellationToken.None);
                        totalReads++;
                    }
                }

                totalBytes += new FileInfo(fastqPath).Length;
            }
            else
            {
                // Just count reads without writing
                await foreach (var sampleReader in reader.ReadClusterData(lane, CancellationToken.None))
                {
                    await using var sr = sampleReader;
                    await foreach (var _ in sr.ReadBclData(DefaultQualityTrimmer.Instance, CancellationToken.None))
                    {
                        totalReads++;
                    }
                }
            }
        }

        sw.Stop();
        return (totalReads, totalBytes, sw.Elapsed);
    }

    private static double StandardDeviation(double[] values)
    {
        var mean = values.Average();
        var sumSquares = values.Sum(v => (v - mean) * (v - mean));
        return Math.Sqrt(sumSquares / values.Length);
    }

    private static void PrintHelp()
    {
        Console.WriteLine("BioSharp BCL Real Data Benchmark");
        Console.WriteLine();
        Console.WriteLine("Usage: openmedstack.biosharp.benchmarks bcl-realdata [options]");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --run-folder <path>  Path to Illumina run folder (default: data/sampledata)");
        Console.WriteLine("  --iterations <n>     Number of iterations (default: 3)");
        Console.WriteLine("  --no-fastq           Skip FASTQ writing (decode only)");
        Console.WriteLine("  --help               Show this help");
    }
}
