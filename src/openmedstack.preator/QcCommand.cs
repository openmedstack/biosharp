namespace OpenMedStack.Preator;

using System;
using System.CommandLine;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using OpenMedStack.BioSharp.Calculations;
using OpenMedStack.BioSharp.Io.FastQ;

internal static class QcCommand
{
    internal static QcOptions CreateOptions(ParseResult parseResult) =>
        new(
            FastqPath: parseResult.GetRequiredValue(PreatorCommandOptions.FastqRequiredOption),
            Adapter: parseResult.GetValue(PreatorCommandOptions.AdapterOption),
            MaxReads: parseResult.GetValue(PreatorCommandOptions.MaxReadsOption),
            OutputDirectory: parseResult.GetValue(PreatorCommandOptions.OutputDirOption)!.FullName,
            OutputPrefix: parseResult.GetValue(PreatorCommandOptions.OutputPrefixOption) ?? "qc-report");

    internal static async Task<int> Invoke(ParseResult parseResult, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            return await Run(CreateOptions(parseResult), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            await Console.Error.WriteLineAsync($"Quality control failed: {exception.Message}").ConfigureAwait(false);
            return 1;
        }
        finally
        {
            stopwatch.Stop();
            Console.WriteLine($"Total runtime: {stopwatch.Elapsed}");
        }
    }

    public static async Task<int> Run(QcOptions options, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(options.FastqPath))
        {
            throw new FileNotFoundException("FASTQ file not found.", options.FastqPath);
        }

        Directory.CreateDirectory(options.OutputDirectory);

        var outputPrefix = string.IsNullOrWhiteSpace(options.OutputPrefix) ? "qc-report" : options.OutputPrefix.Trim();
        var jsonPath = Path.Combine(options.OutputDirectory, $"{outputPrefix}.json");
        var summaryPath = Path.Combine(options.OutputDirectory, $"{outputPrefix}.summary.txt");

        Console.WriteLine($"Input FASTQ : {options.FastqPath}");
        if (!string.IsNullOrWhiteSpace(options.Adapter))
        {
            Console.WriteLine($"Adapter     : {options.Adapter}");
        }

        Console.WriteLine("Computing quality report...");

        var reader = new FastQReader(NullLogger.Instance);
        var reads = reader.Read(options.FastqPath).Take(options.MaxReads ?? int.MaxValue);

        var report = await FastQQualityReport.Compute(reads, options.Adapter, cancellationToken).ConfigureAwait(false);

        // Write JSON report
        var json = JsonSerializer.Serialize(report, PreatorJsonContext.Default.FastQReport);
        await File.WriteAllTextAsync(jsonPath, json, cancellationToken).ConfigureAwait(false);

        // Write human-readable summary
        await WriteSummary(summaryPath, report, options, cancellationToken).ConfigureAwait(false);

        // Print summary to console
        var meanQuality = report.PerBaseQuality.Count > 0
            ? report.PerBaseQuality.Values.Average(s => s.Mean)
            : 0.0;

        Console.WriteLine();
        Console.WriteLine($"Total reads         : {report.TotalReads:N0}");
        Console.WriteLine($"Total bases         : {report.TotalBases:N0}");
        Console.WriteLine($"Mean per-base qual  : {meanQuality:F2}");
        Console.WriteLine($"Duplication estimate: {report.DuplicationLevelEstimate:P1}");
        Console.WriteLine($"JSON report         : {jsonPath}");
        Console.WriteLine($"Summary             : {summaryPath}");

        return 0;
    }

    private static async Task WriteSummary(
        string summaryPath,
        OpenMedStack.BioSharp.Calculations.FastQReport report,
        QcOptions options,
        CancellationToken cancellationToken)
    {
        var meanQuality = report.PerBaseQuality.Count > 0
            ? report.PerBaseQuality.Values.Average(s => s.Mean)
            : 0.0;

        await using var writer = new StreamWriter(summaryPath);
        await writer.WriteLineAsync("FASTQ Quality Control Summary").ConfigureAwait(false);
        await writer.WriteLineAsync($"Input\t{options.FastqPath}").ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(options.Adapter))
        {
            await writer.WriteLineAsync($"Adapter\t{options.Adapter}").ConfigureAwait(false);
        }

        await writer.WriteLineAsync($"TotalReads\t{report.TotalReads}").ConfigureAwait(false);
        await writer.WriteLineAsync($"TotalBases\t{report.TotalBases}").ConfigureAwait(false);
        await writer.WriteLineAsync($"MeanPerBaseQuality\t{meanQuality:F4}").ConfigureAwait(false);
        await writer.WriteLineAsync($"DuplicationLevelEstimate\t{report.DuplicationLevelEstimate:F4}").ConfigureAwait(false);
        await writer.WriteLineAsync($"DuplicationSampleSize\t{report.DuplicationEstimateSampleSize}").ConfigureAwait(false);
        await writer.WriteLineAsync().ConfigureAwait(false);
        await writer.WriteLineAsync("Cycle\tMeanQuality\tMedian\tQ1\tQ3\tMin\tMax").ConfigureAwait(false);

        foreach (var (cycle, stats) in report.PerBaseQuality)
        {
            await writer.WriteLineAsync(
                $"{cycle}\t{stats.Mean:F2}\t{stats.Median:F2}\t{stats.LowerQuartile:F2}\t{stats.UpperQuartile:F2}\t{stats.Min:F2}\t{stats.Max:F2}")
                .ConfigureAwait(false);
        }
    }
}
