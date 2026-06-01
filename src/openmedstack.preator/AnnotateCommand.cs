using OpenMedStack.BioSharp.Model.Report;

namespace OpenMedStack.Preator;

using System;
using System.CommandLine;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OpenMedStack.BioSharp.AnnotationDb;
using OpenMedStack.BioSharp.Io.Vcf;
using OpenMedStack.BioSharp.Model;
using OpenMedStack.BioSharp.Model.Vcf;

internal static class AnnotateCommand
{
    internal static AnnotateOptions CreateOptions(ParseResult parseResult) =>
        new(
            VcfPath: parseResult.GetValue(PreatorCommandOptions.VcfOption)!.FullName,
            TranscriptDatabasePath: parseResult.GetValue(PreatorCommandOptions.DatabaseOption)!.FullName,
            TranscriptId: parseResult.GetValue(PreatorCommandOptions.TranscriptIdOption),
            MinQuality: parseResult.GetValue(PreatorCommandOptions.MinQualityOption),
            OutputDirectory: parseResult.GetValue(PreatorCommandOptions.OutputDirOption)!.FullName,
            OutputPrefix: parseResult.GetValue(PreatorCommandOptions.OutputPrefixOption) ?? "annotated-variants");

    internal static async Task<int> Invoke(ParseResult parseResult, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            return await Run(CreateOptions(parseResult), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            await Console.Error.WriteLineAsync($"Annotation failed: {exception.Message}").ConfigureAwait(false);
            return 1;
        }
        finally
        {
            stopwatch.Stop();
            Console.WriteLine($"Total runtime: {stopwatch.Elapsed}");
        }
    }

    public static async Task<int> Run(AnnotateOptions options, CancellationToken cancellationToken)
    {
        if (!File.Exists(options.VcfPath))
        {
            throw new FileNotFoundException("Input VCF file not found.", options.VcfPath);
        }

        var engine = await CreateAnnotationEngine(options.TranscriptDatabasePath, cancellationToken)
            .ConfigureAwait(false);
        await using var _ = engine.ConfigureAwait(false);

        Directory.CreateDirectory(options.OutputDirectory);

        var outputPrefix = string.IsNullOrWhiteSpace(options.OutputPrefix)
            ? "annotated-variants"
            : options.OutputPrefix.Trim();

        Console.WriteLine($"Reading VCF from {options.VcfPath}");
        Console.WriteLine($"Using transcript database {Path.GetFullPath(options.TranscriptDatabasePath)}");
        if (!string.IsNullOrWhiteSpace(options.TranscriptId))
        {
            Console.WriteLine($"Restricting annotations to transcript {options.TranscriptId}");
        }

        var vcfReader = new VcfFileReader();
        var vcf = await vcfReader.Read(options.VcfPath, cancellationToken).ConfigureAwait(false);
        await using var __ = ((IAsyncDisposable)vcf).ConfigureAwait(false);

        return await RunWithVariants(
            vcf,
            engine.Database,
            options.TranscriptId,
            options.MinQuality,
            options.OutputDirectory,
            outputPrefix,
            GetSampleName(options.VcfPath),
            Path.GetFullPath(options.TranscriptDatabasePath),
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Runs the annotation pipeline on an in-memory stream of VCF variants.
    /// Writes TSV, JSON, HTML and summary output files to <paramref name="outputDirectory"/>.
    /// </summary>
    public static async Task<int> RunWithVariants(
        IAsyncEnumerable<VcfVariant> variants,
        TranscriptAnnotationDatabase database,
        string? transcriptId,
        float minQuality,
        string outputDirectory,
        string outputPrefix,
        string sampleName,
        string databaseDisplayPath,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(outputDirectory);

        var tsvPath = Path.Combine(outputDirectory, $"{outputPrefix}.annotations.tsv");
        var jsonPath = Path.Combine(outputDirectory, $"{outputPrefix}.clinical-report.json");
        var htmlPath = Path.Combine(outputDirectory, $"{outputPrefix}.clinical-report.html");
        var summaryPath = Path.Combine(outputDirectory, $"{outputPrefix}.summary.txt");

        var engine = new DatabaseVariantAnnotationEngine(database);
        var annotatedVariants = new List<VariantAnnotation>();
        var totalVariants = 0;
        var annotatedVariantCount = 0;
        var unannotatedVariantCount = 0;

        await using var tsvWriter = new StreamWriter(tsvPath);
        await tsvWriter.WriteLineAsync(
            "Chromosome\tPosition\tReference\tAlternate\tTranscript\tConsequence\tHgvsCoding\tHgvsProtein\tClinicalSignificance");

        await foreach (var variant in variants.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            totalVariants++;
            var annotations = await engine.AnnotateVariant(
                variant,
                transcriptId,
                minQuality,
                cancellationToken).ConfigureAwait(false);

            if (annotations.Length == 0)
            {
                unannotatedVariantCount++;
                await WriteTsvRow(tsvWriter, variant, null).ConfigureAwait(false);
                continue;
            }

            annotatedVariantCount++;
            annotatedVariants.AddRange(annotations);

            foreach (var annotation in annotations)
            {
                await WriteTsvRow(tsvWriter, variant, annotation).ConfigureAwait(false);
            }
        }

        var metadata = new ClinicalReportMetadata
        {
            SampleName = sampleName,
            RunDate = DateTimeOffset.UtcNow,
            PipelineVersion = typeof(Program).Assembly.GetName().Version?.ToString() ?? "unknown",
            ReferenceGenome = Path.GetFileName(databaseDisplayPath),
            LabName = "OpenMedStack",
            Operator = Environment.UserName
        };

        await File.WriteAllTextAsync(
            jsonPath,
            ClinicalReportWriter.WriteJson(annotatedVariants, metadata),
            cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(
            htmlPath,
            ClinicalReportWriter.WriteHtml(annotatedVariants, metadata),
            cancellationToken).ConfigureAwait(false);
        await WriteSummary(
            summaryPath,
            databaseDisplayPath,
            transcriptId,
            minQuality,
            totalVariants,
            annotatedVariantCount,
            unannotatedVariantCount,
            annotatedVariants).ConfigureAwait(false);

        Console.WriteLine($"Annotated variants: {annotatedVariantCount:N0} / {totalVariants:N0}");
        Console.WriteLine($"Annotation rows: {annotatedVariants.Count:N0}");
        Console.WriteLine($"TSV: {tsvPath}");
        Console.WriteLine($"JSON report: {jsonPath}");
        Console.WriteLine($"HTML report: {htmlPath}");
        Console.WriteLine($"Summary: {summaryPath}");

        return 0;
    }

    /// <summary>
    /// Creates and initialises a <see cref="TranscriptAnnotationDatabase"/> from a SQLite file.
    /// Returned object is <see cref="IAsyncDisposable"/> and must be disposed by the caller.
    /// </summary>
    public static async Task<IAnnotationDatabaseHandle> CreateAnnotationEngine(
        string transcriptDatabasePath,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(transcriptDatabasePath);
        var databasePath = Path.GetFullPath(transcriptDatabasePath);
        if (!File.Exists(databasePath))
        {
            throw new FileNotFoundException("Transcript SQLite database file not found.", databasePath);
        }

        var context = new TranscriptAnnotationDbContext($"Data Source={databasePath}");
        var database = new TranscriptAnnotationDatabase(context);
        await database.Initialize(cancellationToken).ConfigureAwait(false);
        return new AnnotationDatabaseHandle(context, database);
    }

    private static async Task WriteSummary(
        string outputPath,
        string databasePath,
        string? transcriptId,
        float minQuality,
        int totalVariants,
        int annotatedVariantCount,
        int unannotatedVariantCount,
        IReadOnlyCollection<VariantAnnotation> annotations)
    {
        var consequenceCounts = annotations
            .GroupBy(annotation => annotation.Consequence)
            .OrderBy(group => group.Key.ToString(), StringComparer.Ordinal)
            .ToArray();
        var transcriptCount = annotations
            .Select(annotation => annotation.AffectedGene)
            .Where(transcript => !string.IsNullOrWhiteSpace(transcript))
            .Distinct(StringComparer.Ordinal)
            .Count();

        await using var writer = new StreamWriter(outputPath);
        await writer.WriteLineAsync("Variant Annotation Summary");
        await writer.WriteLineAsync($"TranscriptDatabase\t{databasePath}");
        await writer.WriteLineAsync($"TranscriptFilter\t{transcriptId ?? "(all matching transcripts)"}");
        await writer.WriteLineAsync($"MinQuality\t{minQuality}");
        await writer.WriteLineAsync($"TotalVariants\t{totalVariants}");
        await writer.WriteLineAsync($"AnnotatedVariants\t{annotatedVariantCount}");
        await writer.WriteLineAsync($"UnannotatedVariants\t{unannotatedVariantCount}");
        await writer.WriteLineAsync($"AnnotationRows\t{annotations.Count}");
        await writer.WriteLineAsync($"DistinctTranscripts\t{transcriptCount}");
        await writer.WriteLineAsync();
        await writer.WriteLineAsync("Consequence\tCount");

        foreach (var group in consequenceCounts)
        {
            await writer.WriteLineAsync($"{group.Key}\t{group.Count()}");
        }
    }

    private static Task WriteTsvRow(StreamWriter writer, VcfVariant variant, VariantAnnotation? annotation)
    {
        var fields = new[]
        {
            variant.Chromosome,
            variant.Position.ToString(),
            variant.Reference,
            variant.Alternate,
            annotation?.AffectedGene ?? string.Empty,
            annotation?.Consequence.ToString() ?? string.Empty,
            annotation?.HgvsCoding ?? string.Empty,
            annotation?.HgvsProtein ?? string.Empty,
            annotation?.ClinicalSignificance ?? string.Empty
        };

        return writer.WriteLineAsync(string.Join('\t', fields));
    }

    private static string GetSampleName(string vcfPath)
    {
        var fileName = Path.GetFileName(vcfPath);
        if (fileName.EndsWith(".vcf.gz", StringComparison.OrdinalIgnoreCase))
        {
            return fileName[..^7];
        }

        return Path.GetFileNameWithoutExtension(fileName);
    }

    /// <summary>A disposable wrapper around the annotation database context and database instance.</summary>
    public interface IAnnotationDatabaseHandle : IAsyncDisposable
    {
        TranscriptAnnotationDatabase Database { get; }
    }

    private sealed class AnnotationDatabaseHandle(
        TranscriptAnnotationDbContext context,
        TranscriptAnnotationDatabase database)
        : IAnnotationDatabaseHandle
    {
        public TranscriptAnnotationDatabase Database => database;

        public async ValueTask DisposeAsync()
        {
            await context.DisposeAsync().ConfigureAwait(false);
        }
    }
}
