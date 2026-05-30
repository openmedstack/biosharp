using OpenMedStack.BioSharp.Model.Alignment;

namespace OpenMedStack.Preator;

using System;
using System.CommandLine;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OpenMedStack.BioSharp.Calculations.Alignment;

internal static class VariantCallCommand
{
    internal static VariantCallOptions CreateOptions(ParseResult parseResult) =>
        new(
            BamPath: parseResult.GetRequiredValue(PreatorCommandOptions.BamOption),
            ReferencePath: parseResult.GetRequiredValue(PreatorCommandOptions.ReferenceOption),
            ReferenceIdContains: parseResult.GetValue(PreatorCommandOptions.ReferenceIdContainsOption),
            Chromosome: parseResult.GetValue(PreatorCommandOptions.ChromosomeOption),
            OutputDirectory: parseResult.GetValue(PreatorCommandOptions.OutputOption)!,
            OutputPrefix: parseResult.GetValue(PreatorCommandOptions.OutputPrefixOption) ?? "variants",
            MinAlignmentScore: parseResult.GetValue(PreatorCommandOptions.MinAlignmentScoreOption),
            MinVariantQuality: parseResult.GetValue(PreatorCommandOptions.MinVariantQualityOption),
            MinAlternateObservationCount: parseResult.GetValue(PreatorCommandOptions.MinAlternateObservationCountOption),
            MinAlternateFraction: parseResult.GetValue(PreatorCommandOptions.MinAlternateFractionOption),
            EnableSoftClipRealignment: !parseResult.GetValue(PreatorCommandOptions.DisableSoftclipRealignOption),
            EnableGraphSvDetection: parseResult.GetValue(PreatorCommandOptions.EnableGraphSvOption),
            KmerSize: parseResult.GetValue(PreatorCommandOptions.KmerSizeOption),
            MinGraphCoverage: parseResult.GetValue(PreatorCommandOptions.MinGraphCoverageOption),
            GraphWindowBp: parseResult.GetValue(PreatorCommandOptions.GraphWindowBpOption),
            MaxCores: parseResult.GetValue(PreatorCommandOptions.MaxCoresOption));

    internal static async Task<int> Invoke(ParseResult parseResult, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            return await Run(CreateOptions(parseResult), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            await Console.Error.WriteLineAsync($"Variant calling failed: {exception.Message}").ConfigureAwait(false);
            return 1;
        }
        finally
        {
            stopwatch.Stop();
            Console.WriteLine($"Total runtime: {stopwatch.Elapsed}");
        }
    }

    public static async Task<int> Run(VariantCallOptions options, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(options.BamPath))
        {
            throw new FileNotFoundException("BAM file not found.", options.BamPath);
        }

        if (!File.Exists(options.ReferencePath))
        {
            throw new FileNotFoundException("Reference FASTA file not found.", options.ReferencePath);
        }

        Directory.CreateDirectory(options.OutputDirectory);

        Console.WriteLine($"Loading reference from {options.ReferencePath}");
        var reference = await AnalysisCommand.LoadReference(options.ReferencePath, options.ReferenceIdContains)
            .ConfigureAwait(false);
        var chromosome = options.Chromosome ?? AnalysisCommand.NormalizeSequenceId(reference.Id);

        Console.WriteLine($"Selected reference sequence: {reference.Id} ({reference.Length:N0} bp)");
        Console.WriteLine($"Using chromosome/output contig name: {chromosome}");
        Console.WriteLine($"Loading BAM from {options.BamPath}");

        var pipeline = new VariantCallingPipeline(
            reference,
            chromosome,
            new VariantCallingPipeline.PipelineOptions
            {
                MinAlignmentScore = options.MinAlignmentScore,
                MinVariantQuality = options.MinVariantQuality,
                MinAlternateObservationCount = options.MinAlternateObservationCount,
                MinAlternateFraction = options.MinAlternateFraction,
                EnableSoftClipRealignment = options.EnableSoftClipRealignment,
                EnableGraphSvDetection = options.EnableGraphSvDetection,
                KmerSize = options.KmerSize,
                MinGraphCoverage = options.MinGraphCoverage,
                GraphWindowBp = options.GraphWindowBp,
                DegreeOfParallelism = options.MaxCores
            });

        var loaded = await pipeline.LoadBam(options.BamPath, cancellationToken).ConfigureAwait(false);
        if (!loaded)
        {
            throw new InvalidOperationException($"Failed to load BAM file: {options.BamPath}");
        }

        if (options.EnableGraphSvDetection)
        {
            Console.WriteLine("Running full-reference De Bruijn graph analysis");
            await pipeline.RunFullGraphAnalysis().ConfigureAwait(false);
        }

        var result = pipeline.BuildResult();
        var mergedVariants = result.Variants.OrderBy(variant => variant.Position).ToArray();

        var outputPrefix = string.IsNullOrWhiteSpace(options.OutputPrefix) ? "variants" : options.OutputPrefix.Trim();
        var vcfPath = Path.Combine(options.OutputDirectory, $"{outputPrefix}.vcf");
        var reportPath = Path.Combine(options.OutputDirectory, $"{outputPrefix}.tsv");
        var summaryPath = Path.Combine(options.OutputDirectory, $"{outputPrefix}.summary.txt");

        await AnalysisCommand.WriteMergedVcf(vcfPath, mergedVariants, chromosome, reference.Length).ConfigureAwait(false);
        await AnalysisCommand.WriteReport(reportPath, mergedVariants).ConfigureAwait(false);
        await WriteSummary(summaryPath, options.BamPath, result, mergedVariants).ConfigureAwait(false);

        Console.WriteLine();
        Console.WriteLine(result.Metrics);
        Console.WriteLine($"Merged variants: {mergedVariants.Length:N0}");
        Console.WriteLine($"VCF    : {vcfPath}");
        Console.WriteLine($"Report : {reportPath}");
        Console.WriteLine($"Summary: {summaryPath}");

        return 0;
    }

    private static async Task WriteSummary(
        string outputPath,
        string bamPath,
        VariantCallingPipeline.PipelineResult result,
        IReadOnlyCollection<LocalVariantResult> mergedVariants)
    {
        var counts = mergedVariants
            .GroupBy(GetVariantType)
            .OrderBy(group => group.Key)
            .ToDictionary(group => group.Key, group => group.Count());

        await using var writer = new StreamWriter(outputPath);
        await writer.WriteLineAsync("Variant Calling Summary (BAM input)").ConfigureAwait(false);
        await writer.WriteLineAsync($"InputBAM\t{bamPath}").ConfigureAwait(false);
        await writer.WriteLineAsync($"MappedReads\t{result.Metrics.ReadsMapped}").ConfigureAwait(false);
        await writer.WriteLineAsync($"SoftClipRealignedReads\t{result.Metrics.ReadsRealigned}").ConfigureAwait(false);
        await writer.WriteLineAsync($"RawVariantCalls\t{result.Metrics.VariantsCalled}").ConfigureAwait(false);
        await writer.WriteLineAsync($"MergedVariantCalls\t{result.Metrics.VariantsFinal}").ConfigureAwait(false);
        await writer.WriteLineAsync($"StructuralVariants\t{result.Metrics.StructuralVariants}").ConfigureAwait(false);
        await writer.WriteLineAsync().ConfigureAwait(false);
        await writer.WriteLineAsync("VariantType\tCount").ConfigureAwait(false);

        foreach (var count in counts)
        {
            await writer.WriteLineAsync($"{count.Key}\t{count.Value}").ConfigureAwait(false);
        }
    }

    private static string GetVariantType(LocalVariantResult variant)
    {
        if (variant.IsStructuralVariant)
        {
            return variant.SvType?.ToString() ?? "SV";
        }

        if (variant.IsInsertion)
        {
            return "Insertion";
        }

        if (variant.IsDeletion)
        {
            return "Deletion";
        }

        return "SNP";
    }
}

