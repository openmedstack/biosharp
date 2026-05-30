namespace OpenMedStack.Preator;

using System;
using System.CommandLine;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using OpenMedStack.BioSharp.Calculations;
using OpenMedStack.BioSharp.Io.FastQ;

internal static class TrimCommand
{
    internal static TrimOptions CreateOptions(ParseResult parseResult) =>
        new(
            FastqPath: parseResult.GetRequiredValue(PreatorCommandOptions.FastqRequiredOption),
            Adapter: parseResult.GetRequiredValue(PreatorCommandOptions.AdapterOption),
            MinLength: parseResult.GetValue(PreatorCommandOptions.MinLengthOption),
            MaxMismatches: parseResult.GetValue(PreatorCommandOptions.MaxMismatchesOption),
            MaxReads: parseResult.GetValue(PreatorCommandOptions.MaxReadsOption),
            OutputDirectory: parseResult.GetValue(PreatorCommandOptions.OutputOption)!,
            OutputPrefix: parseResult.GetValue(PreatorCommandOptions.OutputPrefixOption) ?? "trimmed");

    internal static async Task<int> Invoke(ParseResult parseResult, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            return await Run(CreateOptions(parseResult), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            await Console.Error.WriteLineAsync($"Adapter trimming failed: {exception.Message}").ConfigureAwait(false);
            return 1;
        }
        finally
        {
            stopwatch.Stop();
            Console.WriteLine($"Total runtime: {stopwatch.Elapsed}");
        }
    }

    public static async Task<int> Run(TrimOptions options, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(options.FastqPath))
        {
            throw new FileNotFoundException("FASTQ file not found.", options.FastqPath);
        }

        if (string.IsNullOrWhiteSpace(options.Adapter))
        {
            throw new ArgumentException("Adapter sequence must not be empty.", nameof(options));
        }

        Directory.CreateDirectory(options.OutputDirectory);

        var outputPrefix = string.IsNullOrWhiteSpace(options.OutputPrefix) ? "trimmed" : options.OutputPrefix.Trim();
        var outputFastqPath = Path.Combine(options.OutputDirectory, $"{outputPrefix}.fastq.gz");
        var outputIndexPath = Path.Combine(options.OutputDirectory, $"{outputPrefix}.fastqi.gz");
        var summaryPath = Path.Combine(options.OutputDirectory, $"{outputPrefix}.trim-summary.json");

        Console.WriteLine($"Input FASTQ   : {options.FastqPath}");
        Console.WriteLine($"Adapter       : {options.Adapter}");
        Console.WriteLine($"Min length    : {options.MinLength} bp");
        Console.WriteLine($"Max mismatches: {options.MaxMismatches}");
        Console.WriteLine($"Output FASTQ  : {outputFastqPath}");

        var trimmer = new AdapterTrimmer(options.Adapter, options.MaxMismatches, options.MinLength);
        var reader = new FastQReader(NullLogger.Instance);
        var reads = reader.Read(options.FastqPath).Take(options.MaxReads ?? int.MaxValue);

        await using var fastqStream = new FileStream(
            outputFastqPath, FileMode.Create, FileAccess.Write, FileShare.None,
            bufferSize: 65536, useAsync: true);
        await using var indexStream = new FileStream(
            outputIndexPath, FileMode.Create, FileAccess.Write, FileShare.None,
            bufferSize: 65536, useAsync: true);
        await using var writer = new FastQWriter(
            NullLogger.Instance, fastqStream, indexStream, CompressionLevel.Fastest, leaveOpen: true);

        var result = await FastQQualityReport.ComputeTrimAndWrite(
            reads, trimmer, writer, options.Adapter, cancellationToken).ConfigureAwait(false);

        var stats = result.TrimStats;
        var report = result.QualityReport;

        Console.WriteLine();
        Console.WriteLine($"Input reads    : {report.TotalReads:N0}");
        Console.WriteLine($"Reads trimmed  : {stats.ReadsTrimmed:N0}");
        Console.WriteLine($"Reads discarded: {stats.ReadsDiscarded:N0}");
        Console.WriteLine($"Reads surviving: {result.ReadsWritten:N0}");
        Console.WriteLine($"Bases removed  : {stats.BasesRemoved:N0}");
        Console.WriteLine($"Bytes written  : {result.FastqBytesWritten:N0}");
        Console.WriteLine($"Output FASTQ   : {outputFastqPath}");
        Console.WriteLine($"Summary        : {summaryPath}");

        var summary = new TrimSummary
        {
            Adapter = options.Adapter,
            MinLength = options.MinLength,
            MaxMismatches = options.MaxMismatches,
            TotalReads = report.TotalReads,
            ReadsTrimmed = stats.ReadsTrimmed,
            ReadsDiscarded = stats.ReadsDiscarded,
            SurvivingReads = result.ReadsWritten,
            BasesRemoved = stats.BasesRemoved,
            BasesWritten = result.BasesWritten
        };

        var json = JsonSerializer.Serialize(summary, PreatorJsonContext.Default.TrimSummary);
        await File.WriteAllTextAsync(summaryPath, json, cancellationToken).ConfigureAwait(false);

        return 0;
    }
}
