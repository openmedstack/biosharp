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
using OpenMedStack.BioSharp.Io.Sam;
using OpenMedStack.BioSharp.Io.Bam;
using OpenMedStack.BioSharp.Model;

internal static class VariantCallCommand
{
    internal static VariantCallOptions CreateOptions(ParseResult parseResult) =>
        new(
            ReferencePath: parseResult.GetRequiredValue(PreatorCommandOptions.ReferenceOption),
            FastqPath: parseResult.GetValue(PreatorCommandOptions.FastqOption),
            FastaPath: parseResult.GetValue(PreatorCommandOptions.FastaOption),
            BamPath: parseResult.GetValue(PreatorCommandOptions.BamOption)?.FullName,
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
            return await Run(CreateOptions(parseResult), cancellationToken).ConfigureAwait(false);
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

    public static async Task<int> Run(VariantCallOptions options, CancellationToken cancellationToken = default)
       {
        if (!File.Exists(options.ReferencePath))
          {
            throw new FileNotFoundException("Reference FASTA file not found.", options.ReferencePath);
          }

        var inputCount = 0;
        if (!string.IsNullOrEmpty(options.FastqPath)) inputCount++;
        if (!string.IsNullOrEmpty(options.FastaPath)) inputCount++;
        if (!string.IsNullOrEmpty(options.BamPath)) inputCount++;

        if (inputCount == 0)
          {
            throw new FileNotFoundException("No input provided. Provide --fastq, --fasta, or --bam.");
          }

         // Validate file existence for FASTQ/FASTA/BAM
        if (!string.IsNullOrEmpty(options.FastqPath))
          {
            var fqPath = options.FastqPath;
            if (!File.Exists(fqPath))
              throw new FileNotFoundException("FASTQ file not found.", fqPath);
            if (!fqPath.EndsWith(".gz", StringComparison.OrdinalIgnoreCase))
              throw new ArgumentException("BioSharp's FastQReader expects gzipped FASTQ input. Provide a .fastq.gz file.");
          }
        if (!string.IsNullOrEmpty(options.FastaPath))
          {
            var faPath = options.FastaPath;
            if (!File.Exists(faPath))
              throw new FileNotFoundException("FASTA file not found.", faPath);
          }
        if (!string.IsNullOrEmpty(options.BamPath))
          {
            var bamPath = options.BamPath;
            if (!File.Exists(bamPath))
              throw new FileNotFoundException("BAM file not found.", bamPath);
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

        int processedReads = 0;

        if (!string.IsNullOrEmpty(options.BamPath))
          {
            Console.WriteLine($"Reading BAM from {options.BamPath}");
            processedReads = await ProcessBam(pipeline, options.BamPath, options.MaxReads, cancellationToken);
            Console.WriteLine($"Processed reads from BAM: {processedReads:N0}");
          }
        else if (!string.IsNullOrEmpty(options.FastaPath))
          {
            Console.WriteLine($"Reading FASTA from {options.FastaPath}");
            processedReads = await ProcessFasta(pipeline, options.FastaPath, options.MaxReads, cancellationToken);
          }
        else
           {
            Console.WriteLine($"Reading FASTQ from {options.FastqPath!}");
            processedReads = await ProcessFastq(pipeline, options.FastqPath!, options.MaxReads, cancellationToken);
           }

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
        int? maxReads,
        CancellationToken cancellationToken = default)
        {
        var reader = new FastQReader(NullLogger.Instance);
        var reads = reader.Read(fastqPath, cancellationToken);
        return await ProcessReads(pipeline, LimitedReads(reads, maxReads), cancellationToken);
        }

    internal static async Task<int> ProcessFasta(
        VariantCallingPipeline pipeline,
        string fastaPath,
        int? maxReads,
        CancellationToken cancellationToken = default)
        {
        var reader = new FastAReader(NullLogger<FastAReader>.Instance);
        var reads = IsGzip(fastaPath) ? reader.ReadGz(fastaPath, cancellationToken): reader.Read(fastaPath, cancellationToken);
        return await ProcessReads(pipeline, LimitedReads(reads, maxReads), cancellationToken);
        }

    internal static async Task<int> ProcessBam(
        VariantCallingPipeline pipeline,
        string bamPath,
        int? maxReads,
        CancellationToken cancellationToken = default)
        {
        var reader = new BamReader(bamPath, NullLogger<BamReader>.Instance);
        var callableReads = StreamCallableReadsFromBam(reader, cancellationToken);
        var limited = LimitedReads(callableReads, maxReads);
        return await ProcessReads(pipeline, limited, cancellationToken);
        }

    private static async IAsyncEnumerable<Sequence> LimitedReads(
        System.Collections.Generic.IAsyncEnumerable<Sequence> source,
        int? maxReads,
           [System.Runtime.CompilerServices.EnumeratorCancellation]
        CancellationToken cancellationToken = default)
       {
        var count = 0;
        var it = source.WithCancellation(cancellationToken);
        await foreach (var read in it)
           {
            if (maxReads.HasValue && count >= maxReads.Value)
                break;
            count++;
            yield return read;
           }
       }

    private static async IAsyncEnumerable<Sequence> StreamCallableReadsFromBam(
        BamReader reader,
          [System.Runtime.CompilerServices.EnumeratorCancellation]
        CancellationToken cancellationToken = default)
       {
        await foreach (var section in reader.ReadAlignmentSections(cancellationToken).ConfigureAwait(false))
           {
            if (!IsCallableAlignment(section))
              {
                continue;
              }

            yield return new Sequence(
                section.QName,
                section.Sequence.TrimEnd().AsMemory(),
                section.Quality.TrimEnd().AsMemory());
           }
       }

    internal static async Task<int> ProcessReads(
        VariantCallingPipeline pipeline,
        IAsyncEnumerable<Sequence> reads,
        CancellationToken cancellationToken = default)
       {
        var processedReads = 0;
        var nextProgressReport = 1000;

        await pipeline.LoadFastQ(InstrumentedReads()).ConfigureAwait(false);
        return processedReads;

        async IAsyncEnumerable<Sequence> InstrumentedReads(
              [System.Runtime.CompilerServices.EnumeratorCancellation]
            CancellationToken ct = default)
           {
            await foreach (var read in reads.WithCancellation(ct).ConfigureAwait(false))
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

     // Helper to determine if a BAM alignment section can be used for variant calling
     // (maps to the same logic as VariantCallingPipeline.IsCallableBamAlignment)
    private static bool IsCallableAlignment(AlignmentSection section)
       {
        if (string.IsNullOrEmpty(section.Sequence) || string.IsNullOrEmpty(section.Quality))
          {
            return false;
          }

        if (section.Sequence.Length < 70 && section.Sequence.TrimEnd().Length < 70)
           {
            return false;
           }

           // Skip reads with excessive ambiguity
        var trimmed = section.Sequence.TrimEnd();
        if (trimmed.Length > 0)
           {
            var ambiguityCount = trimmed.Count(c => c == 'N' || c == 'n');
            if ((ambiguityCount * 100.0 / trimmed.Length) >= 0.30)
               {
                return false;
               }
           }

        return true;
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
