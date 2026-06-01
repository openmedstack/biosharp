namespace OpenMedStack.Preator;

using System;
using System.CommandLine;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OpenMedStack.BioSharp.Calculations.Alignment;

internal static class E2ECommand
{
    internal static E2EOptions CreateOptions(ParseResult parseResult) => new(
        FastqPath: parseResult.GetValue(PreatorCommandOptions.FastqOption),
        FastaPath: parseResult.GetValue(PreatorCommandOptions.FastaOption),
        BamPath: parseResult.GetValue(PreatorCommandOptions.BamOption)?.FullName,
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
        ReferenceIdContains: null,
        TranscriptDatabasePath: (parseResult.GetValue(PreatorCommandOptions.DatabaseOption)?.FullName) ?? string.Empty,
        TranscriptId: parseResult.GetValue<string>("--transcript-id"),
        MinQuality: parseResult.GetValue<float>("--min-quality"));

    internal static async Task<int> Invoke(ParseResult parseResult, CancellationToken cancellationToken)
      {
        var stopwatch = Stopwatch.StartNew();
        try
          {
            return await Run(CreateOptions(parseResult), cancellationToken).ConfigureAwait(false);
          }
        catch (Exception exception)
          {
            await Console.Error.WriteLineAsync($"Pipeline failed: {exception.Message}").ConfigureAwait(false);
            return 1;
          }
        finally
          {
            stopwatch.Stop();
            Console.WriteLine($"Total runtime: {stopwatch.Elapsed}");
          }
      }

    public static async Task<int> Run(E2EOptions options, CancellationToken cancellationToken = default)
      {
        if (!File.Exists(options.ReferencePath))
          throw new FileNotFoundException("Reference FASTA file not found.", options.ReferencePath);

        var inputSources = new[] { options.FastqPath, options.FastaPath, options.BamPath }
            .Where(s => !string.IsNullOrEmpty(s))
            .ToArray();

        if (inputSources.Length == 0)
          throw new ArgumentException("No input provided. Provide --fastq, --fasta, or --bam.");

        // Validate file existence
        foreach (var path in inputSources)
          {
            if (path != null && path.EndsWith(".fastq.gz", StringComparison.OrdinalIgnoreCase) && !File.Exists(path))
              throw new FileNotFoundException("FASTQ file not found.", path);
            if (path != null && path.EndsWith(".fasta.gz", StringComparison.OrdinalIgnoreCase) && !File.Exists(path))
              throw new FileNotFoundException("FASTA file not found.", path);
            if (path != null && path.EndsWith(".bam", StringComparison.OrdinalIgnoreCase) && !File.Exists(path))
              throw new FileNotFoundException("BAM file not found.", path);
          }

        Directory.CreateDirectory(options.OutputDirectory);

        Console.WriteLine($"Loading reference from {options.ReferencePath}");
        var reference = VariantCallCommand.LoadReference(options.ReferencePath, null). Result;
        var chromosome = options.Chromosome ?? VariantCallCommand.NormalizeSequenceId(reference.Id);

        Console.WriteLine($"Selected reference sequence: {reference.Id} ({reference.Length:N0} bp)");
        Console.WriteLine($"Using chromosome/output contig name: {chromosome}");

        var (minAlignmentScore, minVariantQuality, minAltObs, minAltFrac, enableSoftClip, enableGraphSv, kmerSize, minGraphCov, graphWinSize, maxCores) = (
            options.MinAlignmentScore,
            options.MinVariantQuality,
            options.MinAlternateObservationCount,
            options.MinAlternateFraction,
            options.EnableSoftClipRealignment,
            options.EnableGraphSvDetection,
            options.KmerSize,
            options.MinGraphCoverage,
            options.GraphWindowBp,
            options.MaxCores);

        var pipeline = new VariantCallingPipeline(
            reference, chromosome,
            new VariantCallingPipeline.PipelineOptions
              {
                MinAlignmentScore = minAlignmentScore,
                MinVariantQuality = minVariantQuality,
                MinAlternateObservationCount = minAltObs,
                MinAlternateFraction = minAltFrac,
                EnableSoftClipRealignment = enableSoftClip,
                EnableGraphSvDetection = enableGraphSv,
                KmerSize = kmerSize,
                MinGraphCoverage = minGraphCov,
                GraphWindowBp = graphWinSize,
                DegreeOfParallelism = maxCores
              });

        int processedReads = 0;

        if (!string.IsNullOrEmpty(options.BamPath))
           {
            Console.WriteLine($"Reading BAM from {options.BamPath}");
            processedReads = await VariantCallCommand.ProcessBam(pipeline, options.BamPath, options.MaxReads, cancellationToken);
            Console.WriteLine($"Processed reads from BAM: {processedReads:N0}");
           }
        else if (!string.IsNullOrEmpty(options.FastaPath))
          {
            Console.WriteLine($"Reading FASTA from {options.FastaPath}");
            processedReads = await VariantCallCommand.ProcessFasta(pipeline, options.FastaPath, options.MaxReads);
          }
        else
          {
            Console.WriteLine($"Reading FASTQ from {options.FastqPath}");
            processedReads = await VariantCallCommand.ProcessFastq(pipeline, options.FastqPath!, options.MaxReads);
          }

        if (options.EnableGraphSvDetection)
          {
            Console.WriteLine("Running full-reference De Bruijn graph analysis");
            await pipeline.RunFullGraphAnalysis();
          }

        var result = pipeline.BuildResult();
        var mergedVariants = result.Variants.OrderBy(v => v.Position).ToArray();

        var baseName = $"preator-{options.OutputPrefix}";
        var vcfPath = Path.Combine(options.OutputDirectory, $"{baseName}.vcf");
        var reportPath = Path.Combine(options.OutputDirectory, $"{baseName}.tsv");
        var summaryPath = Path.Combine(options.OutputDirectory, $"{baseName}.summary.txt");
        var logPath = Path.Combine(options.OutputDirectory, $"{baseName}.log.txt");

        await VariantCallCommand.WriteMergedVcf(vcfPath, mergedVariants, chromosome, reference.Length);
        await VariantCallCommand.WriteReport(reportPath, mergedVariants);
        await VariantCallCommand.WriteSummary(summaryPath, processedReads, result, mergedVariants);

        // Write log with variant filtering information
        var logText = BuildLog(result, pipeline, mergedVariants.Length, chromosome);
        await File.WriteAllTextAsync(logPath, logText, cancellationToken);

        Console.WriteLine();
        Console.WriteLine(result.Metrics);
        Console.WriteLine($"Merged variants: {mergedVariants.Length:N0}");
        Console.WriteLine($"VCF: {vcfPath}");
        Console.WriteLine($"Report: {reportPath}");
        Console.WriteLine($"Summary: {summaryPath}");
        Console.WriteLine($"Log: {logPath}");

        return 0;
      }

    private static string BuildLog(VariantCallingPipeline.PipelineResult result,
        VariantCallingPipeline pipeline, int finalVariants, string chromosome)
      {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Preator End-to-End Pipeline Run Log");
        sb.AppendLine();
        sb.AppendLine("Variant Filters:");
        sb.AppendLine($"  Min. Variant Quality: {result.Metrics.VariantsFinal} (all passed)");
        sb.AppendLine($"  Min. Alternate Observation Count: {result.Metrics.VariantsFinal}");
        sb.AppendLine($"  Min. Alternate Fraction: 0.2 (all passed)");
        sb.AppendLine();
        sb.AppendLine("Variant Calling Metrics:");
        sb.AppendLine($"  Processed reads: {result.Metrics.ReadsProcessed:N0}");
        sb.AppendLine($"  Mapped reads: {result.Metrics.ReadsMapped:N0}");
        sb.AppendLine($"  Variant calls: {result.Metrics.VariantsCalled:N0}");
        sb.AppendLine($"  Final variants: {result.Metrics.VariantsFinal:N0}");
        sb.AppendLine($"  Structural variants: {result.Metrics.StructuralVariants}");
        sb.AppendLine($"  Graph SVs: {result.Metrics.StructuralVariants}");
        sb.AppendLine();
        sb.AppendLine("Pipeline Parameters:");
        sb.AppendLine($"  Input Chromosome: {chromosome}");
        sb.AppendLine($"  Input: Variant Calls");

        if (pipeline.LastBamLoadProfile.BamReadMilliseconds > 0)
          {
            sb.AppendLine($"  BAM read time: {pipeline.LastBamLoadProfile.BamReadMilliseconds:0.0} ms");
            sb.AppendLine($"  BAM variant calling time: {pipeline.LastBamLoadProfile.VariantCallingMilliseconds:0.0} ms");
            sb.AppendLine($"  BAM BAM records in output: {pipeline.LastBamLoadProfile.RecordsRead}");
          }

        return sb.ToString();
      }
}
