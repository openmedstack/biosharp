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
using Microsoft.Extensions.Logging.Abstractions;
using OpenMedStack.BioSharp.Calculations.Alignment;
using OpenMedStack.BioSharp.Io.FastA;
using OpenMedStack.BioSharp.Io.FastQ;
using OpenMedStack.BioSharp.Model;

internal static class AnalysisCommand
{
    internal static AnalysisOptions CreateOptions(ParseResult parseResult) =>
        new(
            ReferencePath: parseResult.GetRequiredValue(PreatorCommandOptions.ReferenceOption),
            FastqPath: parseResult.GetValue(PreatorCommandOptions.FastqOption),
            FastaPath: parseResult.GetValue(PreatorCommandOptions.FastaOption),
            ReferenceIdContains: parseResult.GetValue(PreatorCommandOptions.ReferenceIdContainsOption),
            Chromosome: parseResult.GetValue(PreatorCommandOptions.ChromosomeOption),
            OutputDirectory: parseResult.GetValue(PreatorCommandOptions.OutputOption)!,
            OutputPrefix: parseResult.GetValue(PreatorCommandOptions.OutputPrefixOption) ?? "variants",
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
            MaxCores: parseResult.GetValue(PreatorCommandOptions.MaxCoresOption));

    internal static async Task<int> Invoke(ParseResult parseResult, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            return await Run(CreateOptions(parseResult)).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            await Console.Error.WriteLineAsync($"Analysis failed: {exception.Message}").ConfigureAwait(false);
            return 1;
        }
        finally
        {
            stopwatch.Stop();
            Console.WriteLine($"Total runtime: {stopwatch.Elapsed}");
        }
    }

    public static async Task<int> Run(AnalysisOptions options)
    {
        if (!File.Exists(options.ReferencePath))
        {
            throw new FileNotFoundException("Reference FASTA file not found.", options.ReferencePath);
        }

        if (!File.Exists(options.FastqPath) && !File.Exists(options.FastaPath))
        {
            throw new FileNotFoundException("FASTQ file not found.", options.FastqPath);
        }

        if (!string.IsNullOrWhiteSpace(options.FastqPath) &&
            !options.FastqPath.EndsWith(".gz", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "BioSharp's FastQReader expects gzipped FASTQ input. Provide a .fastq.gz file.");
        }

        Directory.CreateDirectory(options.OutputDirectory);

        Console.WriteLine($"Loading reference from {options.ReferencePath}");
        var reference = await LoadReference(options.ReferencePath, options.ReferenceIdContains);
        var chromosome = options.Chromosome ?? NormalizeSequenceId(reference.Id);

        Console.WriteLine($"Selected reference sequence: {reference.Id} ({reference.Length:N0} bp)");
        Console.WriteLine($"Using chromosome/output contig name: {chromosome}");

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

        var isFastA = string.IsNullOrEmpty(options.FastqPath);
        Console.WriteLine(
            $"Reading {(isFastA ? "FASTA" : "FASTQ")} from {(isFastA ? options.FastaPath : options.FastqPath)}");
        var processedReads = isFastA
            ? await ProcessFasta(pipeline, options.FastaPath!, options.MaxReads)
            : await ProcessFastq(pipeline, options.FastqPath!, options.MaxReads);

        Console.WriteLine($"Processed reads: {processedReads:N0}");

        if (options.EnableGraphSvDetection)
        {
            Console.WriteLine("Running full-reference De Bruijn graph analysis");
            await pipeline.RunFullGraphAnalysis();
        }

        var result = pipeline.BuildResult();
        var mergedVariants = result.Variants.OrderBy(variant => variant.Position).ToArray();

        var vcfPath = Path.Combine(options.OutputDirectory, $"{options.OutputPrefix}.vcf");
        var reportPath = Path.Combine(options.OutputDirectory, $"{options.OutputPrefix}.tsv");
        var summaryPath = Path.Combine(options.OutputDirectory, $"{options.OutputPrefix}.summary.txt");

        await WriteMergedVcf(vcfPath, mergedVariants, chromosome, reference.Length);
        await WriteReport(reportPath, mergedVariants);
        await WriteSummary(summaryPath, processedReads, result, mergedVariants);

        Console.WriteLine();
        Console.WriteLine(result.Metrics);
        Console.WriteLine($"Merged variants: {mergedVariants.Length:N0}");
        Console.WriteLine($"VCF: {vcfPath}");
        Console.WriteLine($"Report: {reportPath}");
        Console.WriteLine($"Summary: {summaryPath}");

        return 0;
    }

    internal static async Task<Sequence> LoadReference(string referencePath, string? referenceIdContains)
    {
        var reader = new FastAReader(NullLogger<FastAReader>.Instance);
        var sequences = IsGzip(referencePath)
            ? reader.ReadGz(referencePath)
            : reader.Read(referencePath);

        await foreach (var sequence in sequences)
        {
            if (string.IsNullOrWhiteSpace(referenceIdContains) ||
                sequence.Id.Contains(referenceIdContains, StringComparison.OrdinalIgnoreCase))
            {
                return sequence;
            }
        }

        throw new InvalidOperationException(
            string.IsNullOrWhiteSpace(referenceIdContains)
                ? "No reference sequences were found in the FASTA file."
                : $"No reference ID containing '{referenceIdContains}' was found in the FASTA file.");
    }

    internal static async Task<int> ProcessFastq(
        VariantCallingPipeline pipeline,
        string fastqPath,
        int? maxReads)
    {
        var reader = new FastQReader(NullLogger.Instance);
        var reads = reader.Read(fastqPath).Take(maxReads ?? int.MaxValue);
        return await ProcessReads(pipeline, reads);
    }

    internal static async Task<int> ProcessFasta(
        VariantCallingPipeline pipeline,
        string fastaPath,
        int? maxReads)
    {
        var reader = new FastAReader(NullLogger<FastAReader>.Instance);
        var reads = fastaPath.EndsWith(".gz", StringComparison.OrdinalIgnoreCase)
            ? reader.ReadGz(fastaPath)
            : reader.Read(fastaPath);

        return await ProcessReads(pipeline, reads.Take(maxReads ?? int.MaxValue));
    }

    internal static async Task<int> ProcessReads(
        VariantCallingPipeline pipeline,
        IAsyncEnumerable<Sequence> reads)
    {
        var processedReads = 0;
        var nextProgressReport = 1000;

        await pipeline.LoadFastQ(InstrumentedReads()).ConfigureAwait(false);
        return processedReads;

        async IAsyncEnumerable<Sequence> InstrumentedReads(
            [System.Runtime.CompilerServices.EnumeratorCancellation]
            CancellationToken cancellationToken = default)
        {
            await foreach (var read in reads.WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                processedReads++;
                if (processedReads >= nextProgressReport)
                {
                    var msg = $"Processed {processedReads:N0} reads...";
                    Console.Write(msg);
                    Console.SetCursorPosition(Console.CursorLeft - msg.Length, Console.CursorTop);
                    nextProgressReport += 1000;
                }

                yield return read;
            }
        }
    }

    internal static async Task WriteMergedVcf(
        string outputPath,
        IReadOnlyList<LocalVariantResult> variants,
        string chromosome,
        long chromLength)
    {
        await using var stream = File.Create(outputPath);
        await VcfWriter.Write(stream, variants, chromosome, chromLength: chromLength);
    }

    internal static async Task WriteReport(string outputPath, IReadOnlyCollection<LocalVariantResult> variants)
    {
        await using var writer = new StreamWriter(outputPath);
        await writer.WriteLineAsync(
            "Chromosome\tPosition\tEndPosition\tType\tReference\tAlternate\tQuality\tDepth\tFilter\tStructuralVariant\tSvType\tGenotype");

        foreach (var variant in variants.OrderBy(item => item.Position))
        {
            var line = string.Join(
                '\t',
                variant.Chromosome,
                variant.Position.ToString(),
                variant.EndPosition.ToString(),
                GetVariantType(variant),
                variant.Reference,
                variant.Alternate,
                variant.QuantitativeQuality.ToString(),
                variant.Depth.ToString(),
                variant.IsPass ? "PASS" : "LOW_QUAL",
                variant.IsStructuralVariant ? "true" : "false",
                variant.SvType?.ToString() ?? string.Empty,
                variant.Genotype?.ToString() ?? string.Empty);

            await writer.WriteLineAsync(line);
        }
    }

    internal static async Task WriteSummary(
        string outputPath,
        int processedReads,
        VariantCallingPipeline.PipelineResult result,
        IReadOnlyCollection<LocalVariantResult> mergedVariants)
    {
        var counts = mergedVariants
            .GroupBy(GetVariantType)
            .OrderBy(group => group.Key)
            .ToDictionary(group => group.Key, group => group.Count());

        await using var writer = new StreamWriter(outputPath);
        await writer.WriteLineAsync("DNA Analysis Summary");
        await writer.WriteLineAsync($"ProcessedReads\t{processedReads}");
        await writer.WriteLineAsync($"MappedReads\t{result.Metrics.ReadsMapped}");
        await writer.WriteLineAsync($"SoftClipRealignedReads\t{result.Metrics.ReadsRealigned}");
        await writer.WriteLineAsync($"RawVariantCalls\t{result.Metrics.VariantsCalled}");
        await writer.WriteLineAsync($"MergedVariantCalls\t{result.Metrics.VariantsFinal}");
        await writer.WriteLineAsync($"StructuralVariants\t{result.Metrics.StructuralVariants}");
        await writer.WriteLineAsync();
        await writer.WriteLineAsync("VariantType\tCount");

        foreach (var count in counts)
        {
            await writer.WriteLineAsync($"{count.Key}\t{count.Value}");
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

    private static bool IsGzip(string path)
    {
        return path.EndsWith(".gz", StringComparison.OrdinalIgnoreCase);
    }

    internal static string NormalizeSequenceId(string sequenceId)
    {
        return sequenceId.Split(' ', '\t', StringSplitOptions.RemoveEmptyEntries)[0];
    }
}
