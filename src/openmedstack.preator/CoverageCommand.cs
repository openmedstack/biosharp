namespace OpenMedStack.Preator;

using System;
using System.Collections.Generic;
using System.CommandLine;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using OpenMedStack.BioSharp.Calculations.Alignment;
using OpenMedStack.BioSharp.Io.Bam;
using OpenMedStack.BioSharp.Io.Bed;
using OpenMedStack.BioSharp.Model;

internal static class CoverageCommand
{
    internal static CoverageOptions CreateOptions(ParseResult parseResult) =>
        new(
            BamPath: parseResult.GetRequiredValue(PreatorCommandOptions.BamRequiredOption),
            OutputPath: parseResult.GetValue(PreatorCommandOptions.OutputOption)!,
            OutputPrefix: parseResult.GetValue(PreatorCommandOptions.OutputPrefixOption) ?? "coverage",
            BedPath: parseResult.GetValue(PreatorCommandOptions.BedOption),
            WritePerPositionTsv: parseResult.GetValue(PreatorCommandOptions.WriteDepthTsvOption));

    internal static async Task<int> Invoke(ParseResult parseResult, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            return await Run(CreateOptions(parseResult), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            await Console.Error.WriteLineAsync($"Coverage depth failed: {exception.Message}").ConfigureAwait(false);
            return 1;
        }
        finally
        {
            stopwatch.Stop();
            Console.WriteLine($"Total runtime: {stopwatch.Elapsed}");
        }
    }

    public static async Task<int> Run(CoverageOptions options, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(options.BamPath))
        {
            throw new FileNotFoundException("BAM file not found.", options.BamPath);
        }

        Directory.CreateDirectory(options.OutputPath);

        var summaryPath = Path.Combine(options.OutputPath, $"{options.OutputPrefix}.coverage-summary.json");
        string? tsvPath = options.WritePerPositionTsv
            ? Path.Combine(options.OutputPath, $"{options.OutputPrefix}.depth.tsv")
            : null;

        Console.WriteLine($"Input BAM : {options.BamPath}");
        if (options.BedPath is not null)
        {
            Console.WriteLine($"BED file  : {options.BedPath}");
        }

        // Load optional target BED intervals
        IReadOnlyList<BedInterval>? targetIntervals = null;
        if (options.BedPath is not null)
        {
            if (!File.Exists(options.BedPath))
            {
                throw new FileNotFoundException("BED file not found.", options.BedPath);
            }

            var reader = new BedReader();
            targetIntervals = await reader.Read(options.BedPath, cancellationToken)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            Console.WriteLine($"Loaded {targetIntervals.Count} target intervals.");
        }

        // Read BAM
        var bamReader = new BamReader(options.BamPath, NullLogger<BamReader>.Instance);
        var definition = await bamReader.Read(cancellationToken).ConfigureAwait(false);

        var alignments = definition.AlignmentSections;

        // Determine reference length from the SQ headers; fall back to the maximum alignment end
        var referenceLength = definition.Sq.Length > 0
            ? (int)definition.Sq.Max(sq => sq.Length)
            : alignments.Length > 0
                ? alignments.Max(a => a.Position + a.Cigar.Sum(c => (int)c.count))
                : 0;

        if (referenceLength <= 0)
        {
            throw new InvalidOperationException(
                "Cannot determine reference length: no SQ headers present and no aligned reads.");
        }

        Console.WriteLine($"Reference length : {referenceLength:N0} bp");
        Console.WriteLine($"Aligned reads    : {alignments.Length:N0}");

        // Compute coverage
        var report = CoverageCalculator.Compute(alignments, referenceLength, targetIntervals);

        // Print summary to console
        Console.WriteLine();
        Console.WriteLine($"Mean depth      : {report.MeanDepth:F2}×");
        Console.WriteLine($"Median depth    : {report.MedianDepth:F2}×");
        Console.WriteLine($"Min depth       : {report.MinDepth:F0}×");
        Console.WriteLine($"Max depth       : {report.MaxDepth:F0}×");
        Console.WriteLine($"Bases covered   : {report.TargetBasesCovered:N0}");
        Console.WriteLine($"≥10×            : {report.FractionAt10x:P1}");
        Console.WriteLine($"≥20×            : {report.FractionAt20x:P1}");
        Console.WriteLine($"≥30×            : {report.FractionAt30x:P1}");
        Console.WriteLine($"≥100×           : {report.FractionAt100x:P1}");
        Console.WriteLine($"CoV             : {report.CoefficientOfVariation:F3}");

        // Write per-position depth TSV (like samtools depth -a output: chrom, pos, depth)
        if (tsvPath is not null)
        {
            var chromName = definition.Sq.Length > 0 ? definition.Sq[0].Name : "ref";
            await WriteDepthTsv(tsvPath, chromName, report.PerPositionDepth, cancellationToken)
                .ConfigureAwait(false);
            Console.WriteLine($"Depth TSV       : {tsvPath}");
        }

        // Write JSON summary
        var summary = new CoverageSummary
        {
            InputBam = options.BamPath,
            BedFile = options.BedPath,
            ReferenceLength = referenceLength,
            TargetBasesCovered = report.TargetBasesCovered,
            MeanDepth = report.MeanDepth,
            MedianDepth = report.MedianDepth,
            MinDepth = report.MinDepth,
            MaxDepth = report.MaxDepth,
            FractionAt10x = report.FractionAt10x,
            FractionAt20x = report.FractionAt20x,
            FractionAt30x = report.FractionAt30x,
            FractionAt100x = report.FractionAt100x,
            FractionAt500x = report.FractionAt500x,
            CoefficientOfVariation = report.CoefficientOfVariation,
            DepthTsvPath = tsvPath,
            SummaryJsonPath = summaryPath
        };

        var json = JsonSerializer.Serialize(summary, PreatorJsonContext.Default.CoverageSummary);
        await File.WriteAllTextAsync(summaryPath, json, cancellationToken).ConfigureAwait(false);
        Console.WriteLine($"Summary JSON    : {summaryPath}");

        return 0;
    }

    private static async Task WriteDepthTsv(
        string path,
        string chromName,
        int[] depth,
        CancellationToken cancellationToken)
    {
        await using var stream = File.Create(path);
        await using var writer = new StreamWriter(stream, Encoding.UTF8, bufferSize: 65536, leaveOpen: false);

        await writer.WriteLineAsync("chrom\tpos\tdepth").ConfigureAwait(false);

        // Write in chunks to avoid tight loop overhead
        var sb = new StringBuilder(capacity: 1024 * 64);
        for (var i = 0; i < depth.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            sb.Append(chromName).Append('\t').Append(i + 1).Append('\t').Append(depth[i]).Append('\n');
            if (sb.Length >= 65536)
            {
                await writer.WriteAsync(sb, cancellationToken).ConfigureAwait(false);
                sb.Clear();
            }
        }

        if (sb.Length > 0)
        {
            await writer.WriteAsync(sb, cancellationToken).ConfigureAwait(false);
        }
    }
}
