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
using OpenMedStack.BioSharp.Calculations.BurrowsWheeler;
using OpenMedStack.BioSharp.Io.Bam;
using OpenMedStack.BioSharp.Io.FastA;
using OpenMedStack.BioSharp.Model;

internal static class IndexCommand
{
    internal static IndexOptions CreateOptions(ParseResult parseResult) => new(
        FastaPath: parseResult.GetValue(PreatorCommandOptions.FastaOption),
        BamPath: parseResult.GetValue(PreatorCommandOptions.BamOption)?.FullName,
        OutputPath: parseResult.GetValue(PreatorCommandOptions.OutputOption)!,
        OutputPrefix: parseResult.GetValue(PreatorCommandOptions.OutputPrefixOption) ?? "index",
        FmSampleRate: parseResult.GetValue(PreatorCommandOptions.FmSampleRateOption),
        AlsoBuildReferenceIndex: parseResult.GetValue(PreatorCommandOptions.AlsoBuildReferenceIndexOption),
        ReferenceIdContains: parseResult.GetValue(PreatorCommandOptions.ReferenceIdContainsOption));

    internal static async Task<int> Invoke(ParseResult parseResult, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            return await Run(CreateOptions(parseResult), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            await Console.Error.WriteLineAsync($"Index build failed: {exception.Message}").ConfigureAwait(false);
            return 1;
        }
        finally
        {
            stopwatch.Stop();
            Console.WriteLine($"Total runtime: {stopwatch.Elapsed}");
        }
    }

    public static async Task<int> Run(IndexOptions options, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(options.OutputPath);

        var buildStopwatch = Stopwatch.StartNew();

        string inputPath;
        string inputFormat;
        string indexedText;

        if (options.FastaPath is not null)
        {
            if (!File.Exists(options.FastaPath))
            {
                throw new FileNotFoundException("FASTA file not found.", options.FastaPath);
            }

            inputPath = options.FastaPath;
            inputFormat = "fasta";
            Console.WriteLine($"Loading reference from {options.FastaPath}");

            var reference = await LoadFastaReference(options.FastaPath, options.ReferenceIdContains)
                .ConfigureAwait(false);
            indexedText = new string(reference.GetData().Span);
            Console.WriteLine($"Reference sequence: {reference.Id} ({indexedText.Length:N0} bp)");
        }
        else
        {
            // BAM path — concatenate all mapped read sequences with a sentinel separator.
            // This enables FM-index based pattern matching across the read set, useful for
            // de-novo assembly validation and read-vs-read sequence lookups.
            if (!File.Exists(options.BamPath))
            {
                throw new FileNotFoundException("BAM file not found.", options.BamPath);
            }

            inputPath = options.BamPath!;
            inputFormat = "bam";
            Console.WriteLine($"Loading reads from {options.BamPath}");

            indexedText = await ConcatenateReadsFromBam(options.BamPath!, cancellationToken)
                .ConfigureAwait(false);
            Console.WriteLine($"Concatenated {indexedText.Length:N0} bases from BAM reads.");
        }

        var indexTypes = new List<string>();
        string? fmiPath = null;
        string? bsiPath = null;

        // Build FM-index
        Console.WriteLine($"Building FM-index (sample rate {options.FmSampleRate})...");
        var fmIndex = FmIndex.Build(indexedText.AsSpan(), options.FmSampleRate);
        fmiPath = Path.Combine(options.OutputPath, $"{options.OutputPrefix}.fmi");
        fmIndex.Save(fmiPath);
        indexTypes.Add("FmIndex");
        Console.WriteLine($"FM-index saved → {fmiPath}");

        // Optionally build ReferenceIndex (k-mer hash map) — FASTA only, requires a Sequence
        if (options.AlsoBuildReferenceIndex && options.FastaPath is not null)
        {
            Console.WriteLine("Building k-mer reference index...");
            var refSeq = await LoadFastaReference(options.FastaPath, options.ReferenceIdContains)
                .ConfigureAwait(false);
            var refIndex = new ReferenceIndex(refSeq);
            bsiPath = Path.Combine(options.OutputPath, $"{options.OutputPrefix}.bsi");
            refIndex.Save(bsiPath);
            indexTypes.Add("ReferenceIndex");
            Console.WriteLine($"K-mer index saved   → {bsiPath}");
        }
        else if (options.AlsoBuildReferenceIndex && inputFormat == "bam")
        {
            Console.WriteLine(
                "Warning: --also-build-reference-index is only supported for FASTA input; skipping.");
        }

        buildStopwatch.Stop();

        // Write summary JSON
        var summary = new IndexSummary
        {
            InputPath = inputPath,
            InputFormat = inputFormat,
            IndexTypes = [.. indexTypes],
            FmIndexPath = fmiPath,
            ReferenceIndexPath = bsiPath,
            SequenceLength = indexedText.Length,
            FmSampleRate = options.FmSampleRate,
            BuildTimeMs = buildStopwatch.Elapsed.TotalMilliseconds
        };

        var summaryPath = Path.Combine(options.OutputPath, $"{options.OutputPrefix}.index-summary.json");
        var json = JsonSerializer.Serialize(summary, PreatorJsonContext.Default.IndexSummary);
        await File.WriteAllTextAsync(summaryPath, json, cancellationToken).ConfigureAwait(false);
        Console.WriteLine($"Summary JSON        → {summaryPath}");

        return 0;
    }

    private static async Task<Sequence> LoadFastaReference(string path, string? idContains)
    {
        var reader = new FastAReader(NullLogger<FastAReader>.Instance);
        var sequences = path.EndsWith(".gz", StringComparison.OrdinalIgnoreCase)
            ? reader.ReadGz(path)
            : reader.Read(path);

        await foreach (var seq in sequences)
        {
            if (string.IsNullOrWhiteSpace(idContains) ||
                seq.Id.Contains(idContains, StringComparison.OrdinalIgnoreCase))
            {
                return seq;
            }
        }

        throw new InvalidOperationException(
            string.IsNullOrWhiteSpace(idContains)
                ? "No sequences found in the FASTA file."
                : $"No sequence with ID containing '{idContains}' found in the FASTA file.");
    }

    private static async Task<string> ConcatenateReadsFromBam(
        string bamPath,
        CancellationToken cancellationToken)
    {
        var bamReader = new BamReader(bamPath, NullLogger<BamReader>.Instance);
        var sb = new StringBuilder();
        var readCount = 0;

        await foreach (var section in bamReader.ReadAlignmentSections(cancellationToken))
        {
            if (!string.IsNullOrEmpty(section.Sequence) && section.Sequence != "*")
            {
                if (sb.Length > 0)
                {
                    sb.Append('$'); // sentinel separator between reads
                }

                sb.Append(section.Sequence);
                readCount++;
            }
        }

        Console.WriteLine($"Read {readCount:N0} sequences from BAM.");
        return sb.ToString();
    }
}
