using OpenMedStack.BioSharp.Model.Alignment;

namespace OpenMedStack.Preator;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.CommandLine;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using System.IO.Compression;
using Microsoft.Extensions.Logging.Abstractions;
using OpenMedStack.BioSharp.Io.Bgzf;
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
        OutputPrefix: parseResult.GetValue(PreatorCommandOptions.OutputPrefixOption) ?? "aligned",
        IndexPath: parseResult.GetValue(PreatorCommandOptions.PreloadIndexOption));

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

        FmIndexSeeder seeder;
        if (!string.IsNullOrWhiteSpace(options.IndexPath) && File.Exists(options.IndexPath))
        {
            Console.WriteLine($"Loading pre-built FM-index from {options.IndexPath}");
            seeder = FmIndexSeeder.Load(reference, options.IndexPath, seederOptions);
        }
        else
        {
            seeder = new FmIndexSeeder(reference, seederOptions);
        }

        Console.WriteLine($"FM-index seeder built ({seeder.ReferenceLength:N0} bp)");

        var mappedCount = 0;
        var unmappedCount = 0;
        var alignments = new ConcurrentBag<SamLineData>();

        var reader = new FastQReader(NullLogger.Instance);
        var enumerable = reader.Read(options.FastqPath!, cancellationToken);
        if (options.MaxReads.HasValue)
        {
            enumerable = enumerable.Take(options.MaxReads.Value);
        }
        var reads = await enumerable.ToListAsync(cancellationToken).ConfigureAwait(false);

        Parallel.ForEach(
            reads,
            new ParallelOptions { MaxDegreeOfParallelism = options.MaxCores },
            read =>
            {
                if (ProcessRead(read, reference, seeder, options.MinAlignmentScore, alignments))
                {
                    Interlocked.Increment(ref mappedCount);
                }
                else
                {
                    Interlocked.Increment(ref unmappedCount);
                }
            });

        await WriteBamOutput(options, contigName, reference, alignments, cancellationToken);
        await WriteSummaryFile(options.OutputPath, options.OutputPrefix, mappedCount, unmappedCount, reads.Count, cancellationToken);
        LogSummary(mappedCount, unmappedCount, reads.Count, options.OutputPath, options.OutputPrefix);
        return 0;
    }

    // BAM path: reads BAM alignment sections and writes BAM output directly
    private static async Task<int> RunFromBam(
        AlignmentOptions options,
        Sequence reference,
        string contigName,
        CancellationToken cancellationToken)
    {
        var reader = new BamReader(options.BamPath!, NullLogger<BamReader>.Instance);
        var readCount = 0;
        var mappedCount = 0;
        var unmappedCount = 0;
        var sections = new List<AlignmentSection>();

        await foreach (var section in reader.ReadAlignmentSections(cancellationToken).ConfigureAwait(false))
        {
            readCount++;
            sections.Add(section);
            if ((section.Flag & AlignmentSection.AlignmentFlag.SegmentUnmapped) != 0)
            {
                unmappedCount++;
            }
            else
            {
                mappedCount++;
            }

            if (options.MaxReads.HasValue && readCount >= options.MaxReads.Value)
            {
                break;
            }
        }

        var bamPath = Path.Combine(options.OutputPath, $"{options.OutputPrefix}.bam");
        Console.WriteLine($"Writing alignments to {bamPath}");
        var hd = FileMetadata.Parse("@HD\tVN:1.6\tSO:unsorted");
        var pg = OpenMedStack.BioSharp.Io.Sam.Program.Parse("@PG\tID:preator-align\tPN:preator\tVN:1.0");
        var rg = ReadGroup.Parse("@RG\tID:preator\tPL:Illumina\tPU:preator-align-run");
        var sq = ReferenceSequence.Parse($"@SQ\tSN:{contigName}\tLN:{reference.Length}");
        var definition = new SamDefinition(hd, [sq], rg, pg, sections);
        await using var fileStream = File.Create(bamPath);
        await using var bgzfStream = new BgzfStream(fileStream, CompressionLevel.Fastest);
        var bamWriter = new BamWriter(bgzfStream, NullLogger<BamWriter>.Instance);
        await bamWriter.Write(definition, cancellationToken).ConfigureAwait(false);

        await WriteSummaryFile(options.OutputPath, options.OutputPrefix, mappedCount, unmappedCount, readCount, cancellationToken);
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
        Console.WriteLine($"BAM output: {Path.Combine(outputPath, $"{outputPrefix}.bam")}");
        Console.WriteLine($"Summary: {Path.Combine(outputPath, $"{outputPrefix}.summary.txt")}");
    }

    private static Task WriteSummaryFile(
        string outputPath,
        string outputPrefix,
        int mapped,
        int unmapped,
        int total,
        CancellationToken cancellationToken)
    {
        var summaryPath = Path.Combine(outputPath, $"{outputPrefix}.summary.txt");
        var mappingPercent = total > 0 ? (mapped * 100.0 / total) : 0;
        return File.WriteAllTextAsync(
            summaryPath,
            $"Alignment Summary\nInputReads\t{total}\nMappedReads\t{mapped}\n" +
            $"UnmappedReads\t{unmapped}\nMappingPercent\t{mappingPercent:F1}\n",
            cancellationToken);
    }

    /// <summary>
    /// Aligns a collection of reads against the reference in-memory using FM-index seeding +
    /// Smith-Waterman, returning <see cref="AlignmentSection"/> records for downstream use
    /// (e.g. duplicate marking and variant calling) without writing any intermediate files.
    /// </summary>
    /// <summary>
    /// Aligns a stream of reads against the reference in-memory using FM-index seeding +
    /// Smith-Waterman, returning <see cref="AlignmentSection"/> records for downstream use
    /// (e.g. duplicate marking and variant calling) without writing any intermediate files.
    /// Reads are streamed through a bounded <see cref="Channel{T}"/> into parallel alignment
    /// workers so input reading and alignment proceed concurrently — no full materialisation
    /// of the read stream before alignment begins.
    /// </summary>
    /// <param name="reads">Lazily-evaluated read stream (e.g. from adapter trimming).</param>
    /// <param name="reference">Reference sequence used for alignment.</param>
    /// <param name="seeder">Pre-built FM-index seeder to reuse across all workers.</param>
    /// <param name="contigName">Contig/chromosome name written into each SAM record.</param>
    /// <param name="minAlignmentScore">Minimum Smith-Waterman score to accept an alignment.</param>
    /// <param name="maxCores">Degree of parallelism for alignment workers.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    internal static async Task<IReadOnlyList<AlignmentSection>> AlignReadsInMemory(
        IAsyncEnumerable<Sequence> reads,
        Sequence reference,
        FmIndexSeeder seeder,
        string contigName,
        int minAlignmentScore,
        int maxCores,
        CancellationToken cancellationToken = default)
    {
        var parallelism = Math.Max(1, maxCores);
        var alignments = new ConcurrentBag<SamLineData>();

        if (parallelism == 1)
        {
            // Single-threaded: stream reads directly without Channel overhead
            await foreach (var read in reads.WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                ProcessRead(read, reference, seeder, minAlignmentScore, alignments);
            }
        }
        else
        {
            // Multi-threaded: producer streams reads into a bounded channel; worker tasks
            // consume from the channel in parallel so I/O and alignment overlap.
            var channelOptions = new BoundedChannelOptions(parallelism * 4)
            {
                SingleWriter = true,
                SingleReader = false,
                AllowSynchronousContinuations = false,
                FullMode = BoundedChannelFullMode.Wait,
            };
            var channel = Channel.CreateBounded<Sequence>(channelOptions);

            var producer = Task.Run(async () =>
            {
                try
                {
                    await foreach (var read in reads.WithCancellation(cancellationToken)
                                       .ConfigureAwait(false))
                    {
                        await channel.Writer.WriteAsync(read, cancellationToken).ConfigureAwait(false);
                    }

                    channel.Writer.TryComplete();
                }
                catch (Exception ex)
                {
                    channel.Writer.TryComplete(ex);
                    throw;
                }
            }, cancellationToken);

            var workers = Enumerable.Range(0, parallelism)
                .Select(_ => Task.Run(async () =>
                {
                    await foreach (var read in channel.Reader.ReadAllAsync(cancellationToken)
                                       .ConfigureAwait(false))
                    {
                        ProcessRead(read, reference, seeder, minAlignmentScore, alignments);
                    }
                }, cancellationToken))
                .ToList();

            await Task.WhenAll(workers.Prepend(producer)).ConfigureAwait(false);
        }

        return alignments
            .Select(r => AlignmentSection.Parse(BuildSamLine(r, contigName)))
            .OfType<AlignmentSection>()
            .ToList();
    }

    internal static bool ProcessRead(
        Sequence read,
        Sequence reference,
        FmIndexSeeder seeder,
        int minAlignmentScore,
        ConcurrentBag<SamLineData> results)
    {
        var windows = seeder.FindCandidateWindows(read);
        AlignmentResult? bestResult = null;
        int bestWindowStart = -1;

        foreach (var window in windows)
        {
            var windowLen = window.End - window.Start;
            if (windowLen <= 0)
            {
                continue;
            }

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
            var seq = new string(read.GetData().Span);
            var qual = new string(read.GetQuality().Span);
            results.Add(new SamLineData(read.Id, seq, qual, bestResult, bestWindowStart, false));
            return true;
        }

        var uSeq = new string(read.GetData().Span);
        var uQual = new string(read.GetQuality().Span);
        results.Add(new SamLineData(read.Id, uSeq, uQual, null, -1, true));
        return false;
    }

    private static async Task<Sequence> LoadReference(string referencePath, string? referenceIdContains)
    {
        var reader = new FastAReader(NullLogger<FastAReader>.Instance);
        var sequences = IsGzip(referencePath) ? reader.ReadGz(referencePath) : reader.Read(referencePath);
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

    private static bool IsGzip(string path) => path.EndsWith(".gz", StringComparison.OrdinalIgnoreCase);

    internal static string NormalizeSequenceId(string sequenceId) =>
        sequenceId.Split(' ', '\t', StringSplitOptions.RemoveEmptyEntries)[0];

    // BAM output (FASTQ path uses this)
    private static async Task WriteBamOutput(
        AlignmentOptions options,
        string referenceName,
        Sequence reference,
        IEnumerable<SamLineData> alignments,
        CancellationToken cancellationToken)
    {
        var bamPath = Path.Combine(options.OutputPath, $"{options.OutputPrefix}.bam");
        Console.WriteLine($"Writing alignments to {bamPath}");
        var hd = FileMetadata.Parse("@HD\tVN:1.6\tSO:unsorted");
        var pg = OpenMedStack.BioSharp.Io.Sam.Program.Parse("@PG\tID:preator-align\tPN:preator\tVN:1.0");
        var rg = ReadGroup.Parse("@RG\tID:preator\tPL:Illumina\tPU:preator-align-run");
        var sq = ReferenceSequence.Parse($"@SQ\tSN:{referenceName}\tLN:{reference.Length}");
        var sections = alignments
            .Select(r => AlignmentSection.Parse(BuildSamLine(r, referenceName)))
            .ToList();
        var definition = new SamDefinition(hd, [sq], rg, pg, sections);
        await using var fileStream = File.Create(bamPath);
        await using var bgzfStream = new BgzfStream(fileStream, CompressionLevel.Fastest);
        var bamWriter = new BamWriter(bgzfStream, NullLogger<BamWriter>.Instance);
        await bamWriter.Write(definition, cancellationToken).ConfigureAwait(false);
    }

    internal static string BuildSamLine(SamLineData record, string contigName = "*")
    {
        if (record.IsUnmapped)
        {
            var flag = (short)(AlignmentSection.AlignmentFlag.FirstSegmentInTemplate |
                AlignmentSection.AlignmentFlag.SegmentUnmapped);
            return
                $"{record.QueryName}\t{flag}\t*\t0\t0\t*\t*\t0\t0\t{ToSamSequence(record.Sequence)}\t{ToSamSequence(record.Quality)}\tNM:i:0";
        }

        var result = record.AlignmentResult!;
        var cigarOps = CigarBuilder.BuildCigarOps(result);
        var cigar = string.Concat(cigarOps.Select(o => $"{o.Item1}{o.Item2}"));
        // WindowStart is the absolute reference offset of the candidate window; add the
        // aligner's window-relative start position to get the true reference coordinate.
        var pos = record.WindowStart + result.ReferenceStartPosition + 1;
        var mapq = result.Score >= 50 ? (byte)60 : result.Score >= 30 ? (byte)40 : (byte)25;
        var mappedFlag = (short)(AlignmentSection.AlignmentFlag.FirstSegmentInTemplate);
        var editDistance = CalculateEditDistance(result);
        return $"{record.QueryName}\t{mappedFlag}\t{contigName}\t{pos}\t{mapq}\t{cigar}\t*\t0\t0\t"
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
            {
                distance++;
            }
            else if (result.AlignedReference[i] != result.AlignedRead[i])
            {
                distance++;
            }
        }

        distance += result.LeftSoftClip + result.RightSoftClip;
        return distance;
    }

    private static int CalculateEditDistanceFromCigar((uint, CigarOp)[] cigar)
    {
        if (cigar == null)
        {
            return 0;
        }

        var distance = 0;
        foreach (var (count, op) in cigar)
        {
            if (op == CigarOp.Insertion || op == CigarOp.Deletion)
            {
                distance += (int)count;
            }
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
