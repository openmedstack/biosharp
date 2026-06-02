namespace OpenMedStack.Preator;

using System;
using System.Collections.Generic;
using System.CommandLine;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using OpenMedStack.BioSharp.Calculations.Alignment;
using OpenMedStack.BioSharp.Io.Bgzf;
using OpenMedStack.BioSharp.Io.Bam;
using OpenMedStack.BioSharp.Io.Sam;
using OpenMedStack.BioSharp.Model;

internal static class MarkDupCommand
{
    internal static MarkDupOptions CreateOptions(ParseResult parseResult) =>
        new(
            BamPath: parseResult.GetRequiredValue(PreatorCommandOptions.BamRequiredOption),
            OutputPath: parseResult.GetValue(PreatorCommandOptions.OutputOption)!,
            OutputPrefix: parseResult.GetValue(PreatorCommandOptions.OutputPrefixOption) ?? "markdup",
            OpticalPixelDistance: parseResult.GetValue(PreatorCommandOptions.OpticalPixelDistanceOption));

    internal static async Task<int> Invoke(ParseResult parseResult, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            return await Run(CreateOptions(parseResult), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            await Console.Error.WriteLineAsync($"Duplicate marking failed: {exception.Message}").ConfigureAwait(false);
            return 1;
        }
        finally
        {
            stopwatch.Stop();
            Console.WriteLine($"Total runtime: {stopwatch.Elapsed}");
        }
    }

    public static async Task<int> Run(MarkDupOptions options, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(options.BamPath))
        {
            throw new FileNotFoundException("BAM file not found.", options.BamPath);
        }

        Directory.CreateDirectory(options.OutputPath);

        var outputBamPath = Path.Combine(options.OutputPath, $"{options.OutputPrefix}.bam");
        var summaryPath = Path.Combine(options.OutputPath, $"{options.OutputPrefix}.markdup-summary.json");

        Console.WriteLine($"Input BAM     : {options.BamPath}");
        Console.WriteLine($"Optical dist  : {options.OpticalPixelDistance} px");
        Console.WriteLine($"Output BAM    : {outputBamPath}");

        var reader = new BamReader(options.BamPath, NullLogger<BamReader>.Instance);
        var definition = await reader.Read(cancellationToken).ConfigureAwait(false);

        var (markedAlignments, metrics) = DuplicateMarker.MarkDuplicates(
            definition.AlignmentSections,
            options.OpticalPixelDistance);

        var markedDefinition = new SamDefinition(
            definition.Hd,
            definition.Sq,
            definition.Rg,
            definition.Pg,
            markedAlignments);

        await using var fileStream = File.Create(outputBamPath);
        await using var bgzfStream = new BgzfStream(fileStream, CompressionLevel.Fastest);
        var writer = new BamWriter(bgzfStream, NullLogger<BamWriter>.Instance);
        await writer.Write(markedDefinition, cancellationToken).ConfigureAwait(false);

        Console.WriteLine();
        Console.WriteLine($"Total reads     : {metrics.TotalReads:N0}");
        Console.WriteLine($"Duplicate reads : {metrics.DuplicateReads:N0}");
        Console.WriteLine($"Optical dups    : {metrics.OpticalDuplicateReads:N0}");
        Console.WriteLine($"Duplicate rate  : {metrics.DuplicateRate:P2}");
        Console.WriteLine($"Output BAM      : {outputBamPath}");
        Console.WriteLine($"Summary         : {summaryPath}");

        var summary = new MarkDupSummary
        {
            InputBam = options.BamPath,
            OutputBam = outputBamPath,
            TotalReads = metrics.TotalReads,
            DuplicateReads = metrics.DuplicateReads,
            OpticalDuplicateReads = metrics.OpticalDuplicateReads,
            DuplicateRate = metrics.DuplicateRate
        };

        var json = JsonSerializer.Serialize(summary, PreatorJsonContext.Default.MarkDupSummary);
        await File.WriteAllTextAsync(summaryPath, json, cancellationToken).ConfigureAwait(false);

        return 0;
    }
}
