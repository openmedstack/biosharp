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
using OpenMedStack.BioSharp.Io.Bam;
using OpenMedStack.BioSharp.Model;

internal static class AlignmentCommand
{
    internal static AlignmentOptions CreateOptions(ParseResult parseResult) => new(
        ReferencePath: parseResult.GetRequiredValue(PreatorCommandOptions.ReferenceOption),
        FastqPath: parseResult.GetValue(PreatorCommandOptions.FastqOption),
        BamPath: parseResult.GetValue(PreatorCommandOptions.BamOption)?.FullName,
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
            throw new FileNotFoundException("Reference FASTA file not found.", options.ReferencePath);

        if ((options.FastqPath == null || !File.Exists(options.FastqPath)) &&
             (options.BamPath == null || !File.Exists(options.BamPath)))
         {
            throw new FileNotFoundException("Provide either --fastq or --bam file.");
         }

        Directory.CreateDirectory(options.OutputPath);

        Console.WriteLine($"Loading reference from {options.ReferencePath}");
        var reference = await LoadReference(options.ReferencePath, null);
        var contigName = NormalizeSequenceId(reference.Id);
        Console.WriteLine($"Selected reference sequence: {reference.Id} ({reference.Length:N0} bp)");

        if (options.BamPath != null)
         {
            Console.WriteLine($"Processing BAM from {options.BamPath}");
            return await RunFromBam(options, reference, contigName, cancellationToken);
         }

        // FASTQ path - existing logic (unchanged)
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
        await foreach (var read in reader.Read(options.FastqPath!)
              .TakeWhile(_ => options.MaxReads == null || readCount < options.MaxReads)
              .WithCancellation(cancellationToken).ConfigureAwait(false))
         {
            readCount++;
            ProcessRead(read, reference, seeder, options.MinAlignmentScore, alignments, ref mappedCount, ref unmappedCount);
            if (readCount % 1000 == 0)
                Console.WriteLine($"Processed {readCount:N0} reads ({mappedCount:N0} mapped, {unmappedCount:N0} unmapped)...");
         }

        await WriteSamOutput(options, contigName, reference, alignments);
        LogSummary(mappedCount, unmappedCount, readCount, options.OutputPath, options.OutputPrefix);
        return 0;
     }

    // BAM path: reads BAM alignment sections and writes SAM output directly
    private static async Task<int> RunFromBam(AlignmentOptions options, Sequence reference, string contigName, CancellationToken cancellationToken)
     {
        var reader = new BamReader(options.BamPath!, NullLogger<BamReader>.Instance);
        var samLines = new List<string>();
        samLines.Add("@HD\tVN:1.6\tSO:unsorted");
        samLines.Add($"@SQ\tSN:{contigName}\tLN:{reference.Length}");
        samLines.Add("@PG\tID:preator-align\tPN:preator\tVN:1.0");
        samLines.Add("@RG\tID:preator\tPL:Illumina\tPU:preator-align-run");

        var readCount = 0;
        var mappedCount = 0;
        var unmappedCount = 0;

        await foreach (var section in reader.ReadAlignmentSections(cancellationToken).ConfigureAwait(false))
         {
            readCount++;
            var isUnmapped = (section.Flag & AlignmentSection.AlignmentFlag.SegmentUnmapped) != 0;
            var seq = string.IsNullOrEmpty(section.Sequence) ? "*" : ToSamSequence(section.Sequence);
            var qual = string.IsNullOrEmpty(section.Quality) ? "*" : ToSamSequence(section.Quality);

            if (isUnmapped)
              {
                var flag = (short)(AlignmentSection.AlignmentFlag.FirstSegmentInTemplate | AlignmentSection.AlignmentFlag.SegmentUnmapped);
                samLines.Add($"{section.QName}\t{flag}\t*\t0\t0\t*\t*\t0\t{seq}\t{qual}\tNM:i:0");
                unmappedCount++;
              }
            else
              {
                var cigar = string.Concat(section.Cigar.Select(o => $"{o.Item1}{CigarOpChar(o.Item2)}"));
                var editDistance = CalculateEditDistanceFromCigar(section.Cigar);
                var flag = (short)(AlignmentSection.AlignmentFlag.FirstSegmentInTemplate);
                var pos = section.Position > 0 ? section.Position : 1;
                var mapq = (byte)Math.Min(60, (int)section.MappingQuality);
                samLines.Add($"{section.QName}\t{flag}\t*\t{pos}\t{mapq}\t{cigar}\t*\t0\t{seq}\t{qual}\tNM:i:{editDistance}");
                mappedCount++;
             }

            if (readCount % 1000 == 0)
                Console.WriteLine($"Processed {readCount:N0} reads ({mappedCount:N0} mapped, {unmappedCount:N0} unmapped)...");

            if (options.MaxReads.HasValue && readCount >= options.MaxReads.Value)
                break;
         }

        var samPath = Path.Combine(options.OutputPath, $"{options.OutputPrefix}.sam");
        Console.WriteLine($"Writing alignments to {samPath}");
        await File.WriteAllLinesAsync(samPath, samLines, cancellationToken);

        var summaryPath = Path.Combine(options.OutputPath, $"{options.OutputPrefix}.summary.txt");
        var mappingPercent = readCount > 0 ? (mappedCount * 100.0 / readCount) : 0;
        await File.WriteAllTextAsync(summaryPath,
             $"Alignment Summary\nInputReads\t{readCount}\nMappedReads\t{mappedCount}\n" +
             $"UnmappedReads\t{unmappedCount}\nMappingPercent\t{mappingPercent:F1}\n",
            cancellationToken);

        LogSummary(mappedCount, unmappedCount, readCount, options.OutputPath, options.OutputPrefix);
        Console.WriteLine($"Source: BAM ({options.BamPath})");
        return 0;
     }

    private static void LogSummary(int mapped, int unmapped, int total, string outputPath, string outputPrefix)
     {
        Console.WriteLine();
        Console.WriteLine($"Aligned reads: {mapped:N0}");
        Console.WriteLine($"Unaligned reads: {unmapped:N0}");
        Console.WriteLine($"Mapping rate: {(total > 0 ? (mapped * 100.0 / total) : 0):F1}%");
        Console.WriteLine($"SAM output: {Path.Combine(outputPath, $"{outputPrefix}.sam")}");
        Console.WriteLine($"Summary: {Path.Combine(outputPath, $"{outputPrefix}.summary.txt")}");
     }

    private static void ProcessRead(
        Sequence read, Sequence reference, FmIndexSeeder seeder,
        int minAlignmentScore, ConcurrentBag<SamLineData> results,
        ref int mappedCount, ref int unmappedCount)
     {
        var windows = seeder.FindCandidateWindows(read);
        AlignmentResult? bestResult = null;
        int bestWindowStart = -1;

        foreach (var window in windows)
         {
            var windowLen = window.End - window.Start;
            if (windowLen <= 0) continue;
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
        var sequences = IsGzip(referencePath) ? reader.ReadGz(referencePath) : reader.Read(referencePath);
        await foreach (var sequence in sequences)
         {
            if (string.IsNullOrWhiteSpace(referenceIdContains) ||
                sequence.Id.Contains(referenceIdContains, StringComparison.OrdinalIgnoreCase))
                return sequence;
         }
        throw new InvalidOperationException(
            string.IsNullOrWhiteSpace(referenceIdContains)
                 ? "No reference sequences were found in the FASTA file."
                 : $"No reference ID containing '{referenceIdContains}' was found in the FASTA file.");
     }

    private static bool IsGzip(string path) => path.EndsWith(".gz", StringComparison.OrdinalIgnoreCase);
    internal static string NormalizeSequenceId(string sequenceId) =>
        sequenceId.Split(' ', '\t', StringSplitOptions.RemoveEmptyEntries)[0];

    // SAM output (FASTQ path uses this; BAM path writes directly)
    private static async Task WriteSamOutput(
        AlignmentOptions options, string referenceName,
        Sequence reference, IEnumerable<SamLineData> alignments)
     {
        var samPath = Path.Combine(options.OutputPath, $"{options.OutputPrefix}.sam");
        Console.WriteLine($"Writing alignments to {samPath}");
        var stream = File.Create(samPath);
        await using var writer = new StreamWriter(stream);
        await writer.WriteLineAsync("@HD\tVN:1.6\tSO:unsorted");
        await writer.WriteLineAsync($"@SQ\tSN:{referenceName}\tLN:{reference.Length}");
        await writer.WriteLineAsync("@PG\tID:preator-align\tPN:preator\tVN:1.0");
        await writer.WriteLineAsync("@RG\tID:preator\tPL:Illumina\tPU:preator-align-run");
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
            var flag = (short)(AlignmentSection.AlignmentFlag.FirstSegmentInTemplate | AlignmentSection.AlignmentFlag.SegmentUnmapped);
            return $"{record.QueryName}\t{flag}\t*\t0\t0\t*\t*\t0\t{ToSamSequence(record.Sequence)}\t{ToSamSequence(record.Quality)}\tNM:i:0";
         }
        var result = record.AlignmentResult!;
        var cigarOps = CigarBuilder.BuildCigarOps(result);
        var cigar = string.Concat(cigarOps.Select(o => $"{o.Item1}{o.Item2}"));
        var pos = result.ReferenceStartPosition + 1;
        var mapq = result.Score >= 50 ? (byte)60 : result.Score >= 30 ? (byte)40 : (byte)25;
        var mappedFlag = (short)(AlignmentSection.AlignmentFlag.FirstSegmentInTemplate);
        var editDistance = CalculateEditDistance(result);
        return $"{record.QueryName}\t{mappedFlag}\t*\t{pos}\t{mapq}\t{cigar}\t*\t0\t"
             + $"{ToSamSequence(record.Sequence)}\t{ToSamSequence(record.Quality)}\t"
             + $"NM:i:{editDistance}";
     }

    private static int CalculateEditDistance(AlignmentResult result)
     {
        var distance = 0;
        var len = Math.Min(result.AlignedReference.Length, result.AlignedRead.Length);
        for (var i = 0; i < len; i++)
         {
            if (result.AlignedReference[i] == '-' || result.AlignedRead[i] == '-')
                distance++;
            else if (result.AlignedReference[i] != result.AlignedRead[i])
                distance++;
         }
        distance += result.LeftSoftClip + result.RightSoftClip;
        return distance;
     }

    private static int CalculateEditDistanceFromCigar((uint, CigarOp)[] cigar)
     {
        if (cigar == null) return 0;
        var distance = 0;
        foreach (var (count, op) in cigar)
         {
            if (op == CigarOp.Insertion || op == CigarOp.Deletion)
                distance += (int)count;
         }
        return distance;
     }

    private static char CigarOpChar(CigarOp op) => op switch
     {
        CigarOp.Match => 'M',
        CigarOp.Insertion => 'I',
        CigarOp.Deletion => 'D',
        CigarOp.SoftClip => 'S',
         _ => 'M'
     };

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
        AlignmentResult? AlignmentResult,
        int WindowStart,
        bool IsUnmapped);
}
