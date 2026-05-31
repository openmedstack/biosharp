using OpenMedStack.BioSharp.Model.Alignment;

namespace OpenMedStack.Preator;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using OpenMedStack.BioSharp.Calculations.Alignment;
using OpenMedStack.BioSharp.Model.Vcf;
using System.CommandLine;

internal static class E2ECommand
{
    internal static E2EOptions CreateOptions(ParseResult parseResult) =>
        new(
            FastqPath: parseResult.GetValue(PreatorCommandOptions.FastqOption),
            FastaPath: parseResult.GetValue(PreatorCommandOptions.FastaOption),
            OutputDirectory: parseResult.GetValue(PreatorCommandOptions.OutputOption)!,
            OutputPrefix: parseResult.GetValue(PreatorCommandOptions.OutputPrefixOption) ?? "variants",
            ReferencePath: parseResult.GetRequiredValue(PreatorCommandOptions.ReferenceOption),
            Chromosome: parseResult.GetValue(PreatorCommandOptions.ChromosomeOption),
            MaxReads: parseResult.GetValue(PreatorCommandOptions.MaxReadsOption),
            MinAlignmentScore: parseResult.GetValue(PreatorCommandOptions.MinAlignmentScoreOption),
            MinVariantQuality: parseResult.GetValue(PreatorCommandOptions.MinVariantQualityOption),
            MinAlternateObservationCount: parseResult.GetValue(PreatorCommandOptions.MinAlternateObservationCountOption),
            MinAlternateFraction: parseResult.GetValue(PreatorCommandOptions.MinAlternateFractionOption),
            EnableSoftClipRealignment: !parseResult.GetValue(PreatorCommandOptions.DisableSoftclipRealignOption),
            EnableGraphSvDetection: parseResult.GetValue(PreatorCommandOptions.EnableGraphSvOption),
            KmerSize: parseResult.GetValue(PreatorCommandOptions.KmerSizeOption),
            MinGraphCoverage: parseResult.GetValue(PreatorCommandOptions.MinGraphCoverageOption),
            GraphWindowBp: parseResult.GetValue(PreatorCommandOptions.GraphWindowBpOption),
            MaxCores: parseResult.GetValue(PreatorCommandOptions.MaxCoresOption),
            ReferenceIdContains: parseResult.GetValue(PreatorCommandOptions.ReferenceIdContainsOption),
            TranscriptDatabasePath: parseResult.GetValue(PreatorCommandOptions.DatabaseOption)!.FullName,
            TranscriptId: parseResult.GetValue(PreatorCommandOptions.TranscriptIdOption),
            MinQuality: parseResult.GetValue(PreatorCommandOptions.MinQualityOption));

    internal static async Task<int> Invoke(ParseResult parseResult, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            return await Run(CreateOptions(parseResult), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            await Console.Error.WriteLineAsync($"E2E analysis failed: {exception.Message}").ConfigureAwait(false);
            return 1;
        }
        finally
        {
            stopwatch.Stop();
            Console.WriteLine($"Total runtime: {stopwatch.Elapsed}");
        }
    }

    internal static async Task<int> Run(E2EOptions options, CancellationToken cancellationToken)
    {
        // ── Validate inputs ──────────────────────────────────────────────────
        if (!File.Exists(options.ReferencePath))
        {
            throw new FileNotFoundException("Reference FASTA file not found.", options.ReferencePath);
        }

        if (!File.Exists(options.FastqPath) && !File.Exists(options.FastaPath))
        {
            throw new FileNotFoundException("Input read file not found. Provide --fastq or --fasta.");
        }

        if (!string.IsNullOrWhiteSpace(options.FastqPath) &&
            !options.FastqPath.EndsWith(".gz", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "BioSharp's FastQReader expects gzipped FASTQ input. Provide a .fastq.gz file.");
        }

        var outputDir = Path.GetFullPath(options.OutputDirectory);
        Directory.CreateDirectory(outputDir);

        // ── Load reference ───────────────────────────────────────────────────
        Console.WriteLine($"Loading reference from {options.ReferencePath}");
        var reference = await VariantCallCommand.LoadReference(
            options.ReferencePath,
            options.ReferenceIdContains).ConfigureAwait(false);
        var chromosome = options.Chromosome ?? VariantCallCommand.NormalizeSequenceId(reference.Id);

        Console.WriteLine($"Selected reference sequence: {reference.Id} ({reference.Length:N0} bp)");
        Console.WriteLine($"Using chromosome/output contig name: {chromosome}");

        // ── Build pipeline ───────────────────────────────────────────────────
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

        // ── Process reads (FASTQ or FASTA) ───────────────────────────────────
        var isFastA = string.IsNullOrEmpty(options.FastqPath);
        Console.WriteLine(
            $"Reading {(isFastA ? "FASTA" : "FASTQ")} from {(isFastA ? options.FastaPath : options.FastqPath)}");

        var processedReads = isFastA
            ? await VariantCallCommand.ProcessFasta(pipeline, options.FastaPath!, options.MaxReads)
                .ConfigureAwait(false)
            : await VariantCallCommand.ProcessFastq(pipeline, options.FastqPath!, options.MaxReads)
                .ConfigureAwait(false);

        Console.WriteLine($"Processed reads: {processedReads:N0}");

        if (options.EnableGraphSvDetection)
        {
            Console.WriteLine("Running full-reference De Bruijn graph analysis");
            await pipeline.RunFullGraphAnalysis(cancellationToken).ConfigureAwait(false);
        }

        // ── Collect variants (kept in memory for annotation) ─────────────────
        var pipelineResult = pipeline.BuildResult();
        var mergedVariants = pipelineResult.Variants.OrderBy(v => v.Position).ToArray();

        // ── Write variant call outputs ────────────────────────────────────────
        var vcfPath = Path.Combine(outputDir, $"{options.OutputPrefix}.vcf");
        var reportPath = Path.Combine(outputDir, $"{options.OutputPrefix}.tsv");
        var summaryPath = Path.Combine(outputDir, $"{options.OutputPrefix}.summary.txt");

        await VariantCallCommand.WriteMergedVcf(vcfPath, mergedVariants, chromosome, reference.Length)
            .ConfigureAwait(false);
        await VariantCallCommand.WriteReport(reportPath, mergedVariants).ConfigureAwait(false);
        await VariantCallCommand.WriteSummary(summaryPath, processedReads, pipelineResult, mergedVariants)
            .ConfigureAwait(false);

        Console.WriteLine();
        Console.WriteLine(pipelineResult.Metrics);
        Console.WriteLine($"Merged variants: {mergedVariants.Length:N0}");
        Console.WriteLine($"VCF: {vcfPath}");
        Console.WriteLine($"Report: {reportPath}");
        Console.WriteLine($"Summary: {summaryPath}");

        // ── Run annotation on in-memory variants (no file round-trip) ─────────
        Console.WriteLine();
        Console.WriteLine("Running variant annotation...");

        var dbHandle = await AnnotateCommand.CreateAnnotationEngine(
            options.TranscriptDatabasePath,
            cancellationToken).ConfigureAwait(false);
        await using var _ = dbHandle.ConfigureAwait(false);

        return await AnnotateCommand.RunWithVariants(
            ToVcfVariants(mergedVariants, chromosome, cancellationToken),
            dbHandle.Database,
            options.TranscriptId,
            options.MinQuality,
            outputDir,
            options.OutputPrefix,
            options.OutputPrefix,
            Path.GetFullPath(options.TranscriptDatabasePath),
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Converts an array of <see cref="LocalVariantResult"/> objects to an async enumerable of
    /// <see cref="VcfVariant"/> without writing to or reading from any file.
    /// </summary>
    private static async IAsyncEnumerable<VcfVariant> ToVcfVariants(
        IReadOnlyList<LocalVariantResult> variants,
        string chromosome,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var variant in variants)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var filter = variant.IsPass ? "PASS" : "LOW_QUAL";

            var infoParts = new List<string> { $"DP={variant.Depth}" };

            if (variant is { IsStructuralVariant: true, SvType: not null })
            {
                infoParts.Add($"SVTYPE={variant.SvType.Value.ToString().ToUpperInvariant()}");
                if (variant.EndPosition > 0)
                {
                    infoParts.Add($"END={variant.EndPosition}");
                }
            }

            yield return new VcfVariant
            {
                Chromosome = chromosome,
                Position = variant.Position,
                MarkerIdentifiers = ".",
                Reference = variant.Reference,
                Alternate = string.Join(",", variant.AllAlternateAlleles),
                ErrorProbabilities = [variant.QuantitativeQuality],
                FailedFilter = [filter],
                AdditionalInformation = string.Join(";", infoParts)
            };

            await Task.CompletedTask; // keeps the compiler happy for the async iterator
        }
    }
}
