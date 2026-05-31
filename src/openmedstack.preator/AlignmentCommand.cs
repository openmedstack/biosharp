using OpenMedStack.BioSharp.Model.Alignment;

namespace OpenMedStack.Preator;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.CommandLine;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using OpenMedStack.BioSharp.Calculations.Alignment;
using OpenMedStack.BioSharp.Calculations.BurrowsWheeler;
using OpenMedStack.BioSharp.Io.FastA;
using OpenMedStack.BioSharp.Io.FastQ;
using OpenMedStack.BioSharp.Io.Sam;
using OpenMedStack.BioSharp.Model;

internal static class AlignmentCommand
{
    internal static AlignmentOptions CreateOptions(ParseResult parseResult) =>
        new(
            ReferencePath: parseResult.GetRequiredValue(PreatorCommandOptions.ReferenceOption),
            FastqPath: parseResult.GetRequiredValue(PreatorCommandOptions.FastqRequiredOption),
            OutputPath: parseResult.GetValue(PreatorCommandOptions.OutputOption)!,
            MaxReads: parseResult.GetValue(PreatorCommandOptions.MaxReadsOption),
            MinAlignmentScore: parseResult.GetValue(PreatorCommandOptions.MinAlignmentScoreOption),
            MinSeedLen: parseResult.GetValue(PreatorCommandOptions.MinSeedLenOption),
            MaxSeedHitsThreshold: (int)parseResult.GetValue(PreatorCommandOptions.MaxSeedHitsThresholdOption),
            SeedStep: parseResult.GetValue(PreatorCommandOptions.SeedStepOption),
            WindowPadding: parseResult.GetValue(PreatorCommandOptions.WindowPaddingOption),
            MaxCandidateWindowsPerRead: parseResult.GetValue(PreatorCommandOptions.MaxCandidateWindowsPerReadOption),
            MaxCores: parseResult.GetValue(PreatorCommandOptions.MaxCoresOption),
            OutputPrefix: parseResult.GetValue(PreatorCommandOptions.OutputPrefixOption) ?? "aligned");

    internal static async Task<int> Invoke(ParseResult parseResult, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            return await Run(CreateOptions(parseResult), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            await Console.Error.WriteLineAsync($"Alignment failed: {exception.Message}").ConfigureAwait(false);
            return 1;
        }
        finally
        {
            stopwatch.Stop();
            Console.WriteLine($"Total runtime: {stopwatch.Elapsed}");
        }
    }

    public static async Task<int> Run(AlignmentOptions options, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(options.ReferencePath))
        {
            throw new FileNotFoundException("Reference FASTA file not found.", options.ReferencePath);
        }

        if (!File.Exists(options.FastqPath))
        {
            throw new FileNotFoundException("FASTQ file not found.", options.FastqPath);
        }

        Directory.CreateDirectory(options.OutputPath);

        Console.WriteLine($"Loading reference from {options.ReferencePath}");
        var reference = await LoadReference(options.ReferencePath, null);
        var contigName = NormalizeSequenceId(reference.Id);

        Console.WriteLine($"Selected reference sequence: {reference.Id} ({reference.Length:N0} bp)");

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
        Console.WriteLine($"FM-index seeder built ({seeder.ReferenceLength:N0} bp)");

        var readCount = 0;
        var mappedCount = 0;
        var unmappedCount = 0;
        var alignments = new ConcurrentBag<SamLineData>();

        var reader = new FastQReader(NullLogger.Instance);
        await foreach (var read in reader.Read(options.FastqPath)
             .TakeWhile(_ => options.MaxReads == null || readCount < options.MaxReads)
             .WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            readCount++;

             // Process single reads
            ProcessRead(read, reference, seeder, options.MinAlignmentScore, alignments, ref mappedCount, ref unmappedCount);


            if (readCount % 1000 == 0)
            {
                Console.WriteLine($"Processed {readCount:N0} reads ({mappedCount:N0} mapped, {unmappedCount:N0} unmapped)...");
            }
        }

        // Write SAM output
        var samPath = Path.Combine(options.OutputPath, $"{options.OutputPrefix}.sam");
        Console.WriteLine($"Writing alignments to {samPath}");
        await WriteSam(samPath, contigName, (int)reference.Length, alignments);

        var summaryPath = Path.Combine(options.OutputPath, $"{options.OutputPrefix}.summary.txt");
        await using var summaryWriter = new StreamWriter(summaryPath);
        await summaryWriter.WriteLineAsync("Alignment Summary");
        await summaryWriter.WriteLineAsync($"InputReads\t{readCount}");
        await summaryWriter.WriteLineAsync($"MappedReads\t{mappedCount}");
        await summaryWriter.WriteLineAsync($"UnmappedReads\t{unmappedCount}");
        var mappingPercent = readCount > 0 ? (mappedCount * 100.0 / readCount) : 0;
        await summaryWriter.WriteLineAsync($"MappingPercent\t{mappingPercent:F1}");

        Console.WriteLine();
        Console.WriteLine($"Aligned reads: {mappedCount:N0}");
        Console.WriteLine($"Unaligned reads: {unmappedCount:N0}");
        Console.WriteLine($"Mapping rate: {mappingPercent:F1}%");
        Console.WriteLine($"SAM output: {samPath}");
        Console.WriteLine($"Summary: {summaryPath}");

        return 0;
    }

    private static void ProcessRead(
        Sequence read,
        Sequence reference,
        FmIndexSeeder seeder,
        int minAlignmentScore,
        ConcurrentBag<SamLineData> results,
        ref int mappedCount,
        ref int unmappedCount)
     {
        var windows = seeder.FindCandidateWindows(read);
        AlignmentResult? bestResult = null;
        int bestWindowStart = -1;

        foreach (var window in windows)
         {
                var windowLen = window.End - window.Start;
            if (windowLen <= 0)
                    continue;

            var refSpan = reference.GetData().Span;
            var refWindow = refSpan.Slice(window.Start, windowLen);
            var readSpan = read.GetData().Span;
            var result = SmithWatermanAligner.Align(refWindow, readSpan, minScore: minAlignmentScore);

            if (result != null && (bestResult == null || result.Score > bestResult.Score))
             {
                bestResult = result;
                bestWindowStart = window.Start;
             }
         }

        if (bestResult != null)
         {
            mappedCount++;
            var seq = new string(read.GetData().Span);
            var qual = new string(read.GetQuality().Span);
            results.Add(new SamLineData(read.Id, seq, qual, bestResult, bestWindowStart, false));
         }
        else
         {
            unmappedCount++;
            var seq = new string(read.GetData().Span);
            var qual = new string(read.GetQuality().Span);
            results.Add(new SamLineData(read.Id, seq, qual, null, -1, true));
         }
     }

    private static async Task<Sequence> LoadReference(string referencePath, string? referenceIdContains)
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

    private static bool IsGzip(string path)
    {
        return path.EndsWith(".gz", StringComparison.OrdinalIgnoreCase);
    }

    internal static string NormalizeSequenceId(string sequenceId)
    {
        return sequenceId.Split(' ', '\t', StringSplitOptions.RemoveEmptyEntries)[0];
    }

    private static async Task WriteSam(
        string path,
        string referenceName,
        int referenceLength,
        IEnumerable<SamLineData> alignments)
    {
        var stream = File.Create(path);
        using var writer = new StreamWriter(stream);

        // SAM Header
        await writer.WriteLineAsync("@HD\tVN:1.6\tSO:unsorted");
        await writer.WriteLineAsync($"@SQ\tSN:{referenceName}\tLN:{referenceLength}");
        await writer.WriteLineAsync("@PG\tID:preator-align\tPN:preator\tVN:1.0");
        await writer.WriteLineAsync("@RG\tID:preator\tPL:Illumina\tPU:preator-align-run");

        // Write alignment records
        foreach (var record in alignments)
        {
            var line = BuildSamLine(record);
            await writer.WriteLineAsync(line);
        }

        await writer.FlushAsync();
    }

    private static string BuildSamLine(SamLineData record)
    {
        if (record.IsUnmapped)
        {
                var flag = (short)(AlignmentSection.AlignmentFlag.FirstSegmentInTemplate
                | AlignmentSection.AlignmentFlag.SegmentUnmapped);
            return $"{record.QueryName}\t{flag}\t*\t0\t0\t*\t*\t0\t{ToSamSequence(record.Sequence)}\t{ToSamSequence(record.Quality)}\tNM:i:0";
        }

        var result = record.Result!;

         // Build CIGAR from alignment
        var cigarOps = CigarBuilder.BuildCigarOps(result);
        var cigar = string.Concat(cigarOps.Select(o => $"{o.Item1}{CigarOpChar(o.Item2)}"));

         // SAM position is 1-based
        var pos = result.ReferenceStartPosition + 1;

         // Mapping quality based on score
        var mapq = (result.Score >= 50) ? (byte)60 : (result.Score >= 30) ? (byte)40 : (byte)25;

         // Flags: single-end read, mapped
        var mappedFlag = (short)(AlignmentSection.AlignmentFlag.FirstSegmentInTemplate);

         // NM tag: edit distance
        var editDistance = CalculateEditDistance(result);

        return $"{record.QueryName}\t{mappedFlag}\t*\t{pos}\t{mapq}\t{cigar}\t*\t0\t"
            + $"{ToSamSequence(record.Sequence)}\t{ToSamSequence(record.Quality)}\t"
            + $"NM:i:{editDistance}";
    }

    private static char CigarOpChar(char op)
    {
        return op switch
        {
            'M' => 'M',
            'I' => 'I',
            'D' => 'D',
            'S' => 'S',
            _ => 'M'
        };
    }

    private static int CalculateEditDistance(AlignmentResult result)
    {
        var distance = 0;
        var len = Math.Min(result.AlignedReference.Length, result.AlignedRead.Length);
        for (var i = 0; i < len; i++)
        {
            if (result.AlignedReference[i] == '-' || result.AlignedRead[i] == '-')
            {
                distance++;
            }
            else if (result.AlignedReference[i] == result.AlignedRead[i])
            {
                continue;
            }
            else
            {
                distance++;
            }
        }
        distance += result.LeftSoftClip + result.RightSoftClip;
        return distance;
    }

    private static string ToSamSequence(ReadOnlySpan<char> span)
    {
        var result = new char[span.Length];
        for (var i = 0; i < span.Length; i++)
        {
            var c = char.ToUpperInvariant(span[i]);
            result[i] = (c >= 'A' && c <= 'Z') ? c : 'N';
        }
        return new string(result);
    }

    internal readonly record struct SamLineData(
        string QueryName,
        string Sequence,
        string Quality,
        AlignmentResult? Result,
        int WindowStart,
        bool IsUnmapped);
}
