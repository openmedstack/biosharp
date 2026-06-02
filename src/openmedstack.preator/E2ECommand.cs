namespace OpenMedStack.Preator;

using System;
using System.Collections.Generic;
using System.CommandLine;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using OpenMedStack.BioSharp.Calculations;
using OpenMedStack.BioSharp.Calculations.Alignment;
using OpenMedStack.BioSharp.Calculations.BurrowsWheeler;
using OpenMedStack.BioSharp.Io.Bam;
using OpenMedStack.BioSharp.Io.FastA;
using OpenMedStack.BioSharp.Io.FastQ;
using OpenMedStack.BioSharp.Model;
using OpenMedStack.BioSharp.Model.Alignment;
using OpenMedStack.BioSharp.Model.Vcf;
using OpenMedStack.BioSharp.Io.Sam;

public static class E2ECommand
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
        TranscriptDatabasePath: parseResult.GetValue(PreatorCommandOptions.OptionalDatabaseOption)?.FullName,
        TranscriptId: parseResult.GetValue(PreatorCommandOptions.TranscriptIdOption),
        MinQuality: parseResult.GetValue(PreatorCommandOptions.MinQualityOption),
        Adapter: parseResult.GetValue(PreatorCommandOptions.AdapterOption),
        MinLength: parseResult.GetValue(PreatorCommandOptions.MinLengthOption),
        MaxMismatches: parseResult.GetValue(PreatorCommandOptions.MaxMismatchesOption),
        MinSeedLen: parseResult.GetValue(PreatorCommandOptions.MinSeedLenOption),
        MaxSeedHitsThreshold: (int)parseResult.GetValue(PreatorCommandOptions.MaxSeedHitsThresholdOption),
        SeedStep: parseResult.GetValue(PreatorCommandOptions.SeedStepOption),
        WindowPadding: parseResult.GetValue(PreatorCommandOptions.WindowPaddingOption),
        MaxCandidateWindowsPerRead: parseResult.GetValue(PreatorCommandOptions.MaxCandidateWindowsPerReadOption),
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
        {
            throw new FileNotFoundException("Reference FASTA file not found.", options.ReferencePath);
        }

        var inputSources = new[] { options.FastqPath, options.FastaPath, options.BamPath }
            .Where(s => !string.IsNullOrEmpty(s))
            .ToArray();

        if (inputSources.Length == 0)
        {
            throw new ArgumentException("No input provided. Provide --fastq, --fasta, or --bam.");
        }

        foreach (var path in inputSources)
        {
            if (path != null && !File.Exists(path))
            {
                throw new FileNotFoundException("Input file not found.", path);
            }
        }

        Directory.CreateDirectory(options.OutputDirectory);

        // ── Step 1: load reference ────────────────────────────────────────────
        Console.WriteLine($"[1/5] Loading reference from {options.ReferencePath}");
        var reference = await VariantCallCommand.LoadReference(options.ReferencePath, options.ReferenceIdContains)
            .ConfigureAwait(false);
        var chromosome = options.Chromosome ?? VariantCallCommand.NormalizeSequenceId(reference.Id);
        Console.WriteLine($"      Reference: {reference.Id} ({reference.Length:N0} bp) → contig '{chromosome}'");

        // ── Build FM-index once and share across alignment and variant calling ─
        // Building the FM-index is the most expensive single-shot operation in the pipeline.
        // The same FmIndexSeeder instance is assigned to pipeline.Seeder so that soft-clip
        // realignment inside variant calling reuses it instead of building a separate
        // ReferenceIndex (k-mer hash).
        Console.WriteLine($"[1/5] Building FM-index (seed len: {options.MinSeedLen}, " +
            $"max seed hits: {options.MaxSeedHitsThreshold})");
        var seederOptions = new FmIndexSeeder.Options
        {
            MinSeedLen = options.MinSeedLen,
            MaxSeedHits = options.MaxSeedHitsThreshold,
            SeedStep = options.SeedStep,
            WindowPadding = options.WindowPadding,
            MaxCandidateWindowsPerRead = options.MaxCandidateWindowsPerRead,
            UseMemSeeds = true,
            MergeDistance = 32,
        };
        var seeder = new FmIndexSeeder(reference, seederOptions);

        // ── Step 2: read → (trim) → align → markdup ──────────────────────────
        IReadOnlyList<AlignmentSection> markedAlignments;
        DuplicateMetrics dupMetrics;
        int rawReadCount;

        if (!string.IsNullOrEmpty(options.BamPath))
        {
            // BAM input: stream alignment records directly into duplicate marking —
            // no intermediate list required, MarkDuplicatesAsync materialises internally.
            Console.WriteLine($"[2/5] Streaming BAM alignments from {options.BamPath}");
            var bamReader = new BamReader(options.BamPath!, NullLogger<BamReader>.Instance);

            Console.WriteLine($"[3/5] Marking duplicates (optical pixel distance: {options.OpticalPixelDistance})");
            (markedAlignments, dupMetrics) = await DuplicateMarker.MarkDuplicatesAsync(
                bamReader.ReadAlignmentSections(cancellationToken),
                options.OpticalPixelDistance,
                cancellationToken).ConfigureAwait(false);

            rawReadCount = markedAlignments.Count;
        }
        else
        {
            // FASTQ / FASTA path:
            //  1. Stream reads (possibly through adapter trimmer) — no upfront materialisation
            //  2. Feed the stream into AlignReadsInMemory which pipelines I/O and alignment
            //     via a bounded Channel; first results arrive before all reads are consumed
            //  3. Collected AlignmentSection list is passed to DuplicateMarker (requires full list)
            var rawReads = ReadReads(options, cancellationToken);

            IAsyncEnumerable<Sequence> alignableReads;
            if (!string.IsNullOrWhiteSpace(options.Adapter))
            {
                Console.WriteLine($"[2/5] Trimming adapter '{options.Adapter}' " +
                    $"(min-length: {options.MinLength}, max-mismatches: {options.MaxMismatches})");
                var trimmer = new AdapterTrimmer(options.Adapter, options.MaxMismatches, options.MinLength);
                alignableReads = TrimReads(rawReads, trimmer, cancellationToken);
            }
            else
            {
                Console.WriteLine("[2/5] Skipping adapter trimming (no --adapter provided)");
                alignableReads = rawReads;
            }

            // Stream reads through parallel FM-index alignment workers — reads are never
            // fully materialised before alignment starts; the Channel bounds memory usage.
            Console.WriteLine($"[3/5] Aligning reads via FM-index " +
                $"(seed len: {options.MinSeedLen}, cores: {options.MaxCores})");
            var alignedSections = await AlignmentCommand.AlignReadsInMemory(
                alignableReads,
                reference,
                seeder,
                chromosome,
                options.MinAlignmentScore,
                options.MaxCores,
                cancellationToken).ConfigureAwait(false);

            rawReadCount = alignedSections.Count;
            Console.WriteLine($"      Aligned {rawReadCount:N0} records");

            Console.WriteLine($"[3/5] Marking duplicates (optical pixel distance: {options.OpticalPixelDistance})");
            (markedAlignments, dupMetrics) = DuplicateMarker.MarkDuplicates(
                alignedSections,
                options.OpticalPixelDistance);
        }

        Console.WriteLine(
            $"      Duplicates: {dupMetrics.DuplicateReads:N0} ({dupMetrics.DuplicateRate:P1}) — " +
            $"{dupMetrics.OpticalDuplicateReads:N0} optical");

        // ── Step 4: variant calling ───────────────────────────────────────────
        // Set pipeline.Seeder to the pre-built FmIndexSeeder so the same index is reused
        // for soft-clip realignment inside the pipeline — prevents a second k-mer index build.
        Console.WriteLine("[4/5] Running variant calling pipeline");
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
                DegreeOfParallelism = options.MaxCores,
                // Use BAM coordinates for AlignmentSection records (pre-aligned)
                UseBamAlignmentCoordinates = true,
            })
        {
            // Reuse the shared FM-index so variant calling never builds a separate ReferenceIndex
            Seeder = seeder,
        };

        // markedAlignments is IReadOnlyList<AlignmentSection> which implements IEnumerable —
        // LoadAlignments iterates it lazily inside Task.Run, so no second copy is made.
        await pipeline.LoadAlignments(markedAlignments, cancellationToken).ConfigureAwait(false);

        if (options.EnableGraphSvDetection)
        {
            Console.WriteLine("      Running full-reference De Bruijn graph analysis");
            await pipeline.RunFullGraphAnalysis().ConfigureAwait(false);
        }

        var result = pipeline.BuildResult();
        var mergedVariants = result.Variants.OrderBy(v => v.Position).ToArray();
        Console.WriteLine(
            $"      Reads processed: {result.Metrics.ReadsProcessed:N0}, " +
            $"mapped: {result.Metrics.ReadsMapped:N0}, " +
            $"variants: {mergedVariants.Length:N0}");

        // ── Step 5: write outputs ─────────────────────────────────────────────
        Console.WriteLine("[5/5] Writing outputs");
        var baseName = $"preator-{options.OutputPrefix}";
        var vcfPath = Path.Combine(options.OutputDirectory, $"{baseName}.vcf");
        var reportPath = Path.Combine(options.OutputDirectory, $"{baseName}.tsv");
        var summaryPath = Path.Combine(options.OutputDirectory, $"{baseName}.summary.txt");
        var logPath = Path.Combine(options.OutputDirectory, $"{baseName}.log.txt");

        await VariantCallCommand.WriteMergedVcf(vcfPath, mergedVariants, chromosome, reference.Length)
            .ConfigureAwait(false);
        await VariantCallCommand.WriteReport(reportPath, mergedVariants).ConfigureAwait(false);
        await VariantCallCommand.WriteSummary(summaryPath, rawReadCount, result, mergedVariants)
            .ConfigureAwait(false);

        var logText = BuildLog(options, result, dupMetrics, mergedVariants.Length, chromosome);
        await File.WriteAllTextAsync(logPath, logText, cancellationToken).ConfigureAwait(false);

        Console.WriteLine();
        Console.WriteLine(result.Metrics);
        Console.WriteLine($"Merged variants: {mergedVariants.Length:N0}");
        Console.WriteLine($"VCF:             {vcfPath}");
        Console.WriteLine($"Report:          {reportPath}");
        Console.WriteLine($"Summary:         {summaryPath}");
        Console.WriteLine($"Log:             {logPath}");

        // ── Step 5b: in-process annotation (if database supplied) ─────────────
        if (!string.IsNullOrWhiteSpace(options.TranscriptDatabasePath))
        {
            Console.WriteLine();
            Console.WriteLine($"[+] Annotating variants against {options.TranscriptDatabasePath}");
            var annotationHandle = await AnnotateCommand.CreateAnnotationEngine(
                options.TranscriptDatabasePath, cancellationToken).ConfigureAwait(false);
            await using var _ = annotationHandle.ConfigureAwait(false);

            var vcfVariants = AsVcfVariants(mergedVariants, chromosome);
            await AnnotateCommand.RunWithVariants(
                vcfVariants,
                annotationHandle.Database,
                options.TranscriptId,
                options.MinQuality,
                options.OutputDirectory,
                baseName + "-annotated",
                options.OutputPrefix,
                Path.GetFullPath(options.TranscriptDatabasePath),
                cancellationToken).ConfigureAwait(false);
        }

        return 0;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static IAsyncEnumerable<Sequence> ReadReads(
        E2EOptions options,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(options.FastaPath))
        {
            Console.WriteLine($"[2/5] Reading FASTA reads from {options.FastaPath}");
            var reader = new FastAReader(NullLogger<FastAReader>.Instance);
            var all = options.FastaPath.EndsWith(".gz", StringComparison.OrdinalIgnoreCase)
                ? reader.ReadGz(options.FastaPath, cancellationToken)
                : reader.Read(options.FastaPath, cancellationToken);
            return options.MaxReads.HasValue ? all.Take(options.MaxReads.Value) : all;
        }

        Console.WriteLine($"[2/5] Reading FASTQ reads from {options.FastqPath}");
        var fastqReader = new FastQReader(NullLogger.Instance);
        var reads = fastqReader.Read(options.FastqPath!, cancellationToken);
        return options.MaxReads.HasValue ? reads.Take(options.MaxReads.Value) : reads;
    }

    private static async IAsyncEnumerable<Sequence> TrimReads(
        IAsyncEnumerable<Sequence> reads,
        AdapterTrimmer trimmer,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var read in reads.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            var (trimmed, _) = trimmer.Trim(read);
            if (trimmed != null)
            {
                yield return trimmed;
            }
        }
    }

    /// <summary>
    /// Converts variant results to VCF variant records so they can be fed directly into
    /// the in-process annotation engine without writing/reading a VCF file.
    /// </summary>
    private static async IAsyncEnumerable<VcfVariant> AsVcfVariants(
        IReadOnlyList<LocalVariantResult> variants,
        string chromosome,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var v in variants)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return new VcfVariant
            {
                Chromosome = chromosome,
                Position = v.Position,
                MarkerIdentifiers = ".",
                Reference = v.Reference,
                Alternate = v.Alternate,
                ErrorProbabilities = [(int)Math.Round((double)v.QuantitativeQuality)],
                FailedFilter = v.IsPass ? ["PASS"] : ["LOW_QUAL"],
                AdditionalInformation = BuildVcfInfo(v),
            };
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }

    private static string BuildVcfInfo(LocalVariantResult v)
    {
        var info = $"DP={v.Depth}";
        if (v is { IsStructuralVariant: true, SvType: not null })
        {
            info += $";SVTYPE={v.SvType.Value}";
            if (v.EndPosition > 0)
            {
                info += $";END={v.EndPosition}";
            }
        }

        return info;
    }

    private static string BuildLog(
        E2EOptions options,
        VariantCallingPipeline.PipelineResult result,
        DuplicateMetrics dupMetrics,
        int finalVariants,
        string chromosome)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("Preator End-to-End Pipeline Run Log");
        sb.AppendLine();
        sb.AppendLine("Input:");
        if (!string.IsNullOrEmpty(options.FastqPath)) sb.AppendLine($"  FASTQ: {options.FastqPath}");
        if (!string.IsNullOrEmpty(options.FastaPath)) sb.AppendLine($"  FASTA: {options.FastaPath}");
        if (!string.IsNullOrEmpty(options.BamPath))   sb.AppendLine($"  BAM:   {options.BamPath}");
        sb.AppendLine($"  Reference: {options.ReferencePath}");
        sb.AppendLine($"  Contig: {chromosome}");
        sb.AppendLine();
        if (!string.IsNullOrWhiteSpace(options.Adapter))
        {
            sb.AppendLine("Adapter Trimming:");
            sb.AppendLine($"  Adapter:       {options.Adapter}");
            sb.AppendLine($"  Min length:    {options.MinLength} bp");
            sb.AppendLine($"  Max mismatches: {options.MaxMismatches}");
            sb.AppendLine();
        }

        if (string.IsNullOrEmpty(options.BamPath))
        {
            sb.AppendLine("Alignment (FM-index + Smith-Waterman):");
            sb.AppendLine($"  Min seed length:            {options.MinSeedLen}");
            sb.AppendLine($"  Max seed hits:              {options.MaxSeedHitsThreshold}");
            sb.AppendLine($"  Seed step:                  {options.SeedStep}");
            sb.AppendLine($"  Window padding:             {options.WindowPadding}");
            sb.AppendLine($"  Max windows per read:       {options.MaxCandidateWindowsPerRead}");
            sb.AppendLine($"  Min alignment score:        {options.MinAlignmentScore}");
            sb.AppendLine();
        }

        sb.AppendLine("Duplicate Marking:");
        sb.AppendLine($"  Optical pixel distance: {options.OpticalPixelDistance}");
        sb.AppendLine($"  Total reads:            {dupMetrics.TotalReads:N0}");
        sb.AppendLine($"  Duplicate reads:        {dupMetrics.DuplicateReads:N0}");
        sb.AppendLine($"  Optical duplicates:     {dupMetrics.OpticalDuplicateReads:N0}");
        sb.AppendLine($"  Duplicate rate:         {dupMetrics.DuplicateRate:P2}");
        sb.AppendLine();
        sb.AppendLine("Variant Filters:");
        sb.AppendLine($"  Min variant quality:              {options.MinVariantQuality}");
        sb.AppendLine($"  Min alternate observation count:  {options.MinAlternateObservationCount}");
        sb.AppendLine($"  Min alternate fraction:           {options.MinAlternateFraction:F2}");
        sb.AppendLine();
        sb.AppendLine("Variant Calling Metrics:");
        sb.AppendLine($"  Processed reads:   {result.Metrics.ReadsProcessed:N0}");
        sb.AppendLine($"  Mapped reads:      {result.Metrics.ReadsMapped:N0}");
        sb.AppendLine($"  Variant calls:     {result.Metrics.VariantsCalled:N0}");
        sb.AppendLine($"  Final variants:    {result.Metrics.VariantsFinal:N0}");
        sb.AppendLine($"  Structural variants: {result.Metrics.StructuralVariants}");
        return sb.ToString();
    }
}

