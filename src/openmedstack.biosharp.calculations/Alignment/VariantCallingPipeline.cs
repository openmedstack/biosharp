using Microsoft.Extensions.Logging.Abstractions;

namespace OpenMedStack.BioSharp.Calculations.Alignment;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Model;
using DeBruijn;
using Io.Bam;
using Io.FastQ;
using Io.Sam;

/// <summary>
/// Orchestrates the entire variant calling pipeline:
/// BAM/FASTQ input -> per-read alignment -> variant calling -> soft-clip realignment ->
/// De Bruijn graph SV detection -> multi-read consensus -> VCF output.
///
/// Usage:
///   var pipeline = new VariantCallingPipeline(refSeq, "chr1");
///
///    // Option 1: BAM input
///   await pipeline.LoadBamAsync("input.bam");
///
///    // Option 2: FASTQ input (single-end)
///   await pipeline.LoadFastQAsync("reads.fastq");
///
///    // Merge variants and get final results
///   var result = pipeline.BuildResult();
///
///    // Write output
///   await pipeline.WriteVcfAsync("output.vcf");
/// </summary>
public class VariantCallingPipeline
{
    /// <summary>
    /// Configuration options for the variant calling pipeline.
    /// </summary>
    public sealed class PipelineOptions
    {
        /// <summary>
        /// Match score for Smith-Waterman alignment (default: 2).
        /// </summary>
        public int MatchScore { get; set; } = 2;

        /// <summary>
        /// Mismatch penalty for Smith-Waterman alignment (default: -3).
        /// </summary>
        public int MismatchPenalty { get; set; } = -3;

        /// <summary>
        /// Gap open penalty (default: -5).
        /// </summary>
        public int GapOpenPenalty { get; set; } = -5;

        /// <summary>
        /// Gap extend penalty (default: -2).
        /// </summary>
        public int GapExtendPenalty { get; set; } = -2;

        /// <summary>
        /// Minimum alignment score to consider a read as mapped (default: 10).
        /// </summary>
        public int MinAlignmentScore { get; set; } = 10;

        /// <summary>
        /// Minimum variant quality to include in output (default: 30).
        /// </summary>
        public int MinVariantQuality { get; set; } = 30;

        /// <summary>
        /// Enable soft-clip realignment for SV discovery (default: true).
        /// </summary>
        public bool EnableSoftClipRealignment { get; set; } = true;

        /// <summary>
        /// Read clip fraction threshold for realignment. Reads with >= this fraction
        /// of soft-clips are realigned. Default: 0.20 (20%).
        /// </summary>
        public float MinClipFraction { get; set; } = 0.20f;

        /// <summary>
        /// Minimum clip size in bp for realignment (default: 20).
        /// </summary>
        public int MinClipSize { get; set; } = 20;

        /// <summary>
        /// Window size for local realignment (default: 200).
        /// </summary>
        public int LocalWindowSize { get; set; } = 200;

        /// <summary>
        /// Enable De Bruijn graph analysis for SV detection (default: true).
        /// </summary>
        public bool EnableGraphSvDetection { get; set; } = true;

        /// <summary>
        /// K-mer size for graph analysis (default: 15).
        /// Smaller kmer = more sensitive but more noise; larger = more specific.
        /// </summary>
        public int KmerSize { get; set; } = 15;

        /// <summary>
        /// Maximum window size for graph analysis (bp). Reads aligned to a region
        /// of this size will have a local graph built (default: 500).
        /// </summary>
        public int GraphWindowBp { get; set; } = 500;

        /// <summary>
        /// Minimum number of reads covering a region to trigger graph analysis (default: 5).
        /// </summary>
        public int MinGraphCoverage { get; set; } = 5;

        /// <summary>
        /// Seed size used for the reference index candidate search.
        /// </summary>
        public int SeedSize { get; set; } = 11;

        /// <summary>
        /// Extra padding applied to each candidate alignment window.
        /// </summary>
        public int CandidateWindowPadding { get; set; } = 64;

        /// <summary>
        /// Maximum number of candidate windows to align per read.
        /// </summary>
        public int MaxCandidateWindowsPerRead { get; set; } = 8;

        /// <summary>
        /// Ignore seeds that hit the reference too many times.
        /// </summary>
        public int MaxSeedHitsPerKmer { get; set; } = 64;

        /// <summary>
        /// Diagonal slack around the seed-informed alignment path.
        /// Set to a negative value to disable banding.
        /// </summary>
        public int AlignmentBandWidth { get; set; } = 16;

        /// <summary>
        /// Drop cells that trail the best alignment score by more than this amount.
        /// Set to 0 to disable X-drop pruning.
        /// </summary>
        public int AlignmentXDrop { get; set; } = 40;

        /// <summary>
        /// Maximum DP cells allowed for a single candidate alignment. 0 disables the guard.
        /// </summary>
        public int MaxAlignmentCellCount { get; set; }

        /// <summary>
        /// Degree of parallelism for FASTQ/BAM read processing.
        /// </summary>
        public int DegreeOfParallelism { get; set; } = 1;

        /// <summary>
        /// Maximum number of graph windows to analyze when performing full-graph SV detection.
        /// 0 means no cap.
        /// </summary>
        public int MaxGraphWindowsPerSample { get; set; } = 32;
    }

    /// <summary>
    /// Summary of pipeline processing.
    /// </summary>
    public sealed class PipelineMetrics
    {
        /// <summary>Total reads processed.</summary>
        public int ReadsProcessed { get; set; }

        /// <summary>Reads that mapped with sufficient quality.</summary>
        public int ReadsMapped { get; set; }

        /// <summary>Reads that had soft-clips realigned.</summary>
        public int ReadsRealigned { get; set; }

        /// <summary>Total variant calls before merging.</summary>
        public int VariantsCalled { get; set; }

        /// <summary>Variants after merging consensus.</summary>
        public int VariantsFinal { get; set; }

        /// <summary>Number of structural variants detected.</summary>
        public int StructuralVariants { get; set; }

        /// <summary>
        /// Clips rejected before alignment by heuristics (low complexity or below the
        /// size/fraction threshold). Tracked per the Task 7 specification.
        /// </summary>
        public int SkippedRealignments { get; set; }

        public override string ToString()
        {
            return $"PipelineMetrics{{reads={ReadsProcessed}, mapped={ReadsMapped}, " +
                $"aligned={ReadsRealigned}, called={VariantsCalled}, final={VariantsFinal}, " +
                $"sv={StructuralVariants}, skippedRealign={SkippedRealignments}}}";
        }
    }

    /// <summary>Results from pipeline processing.</summary>
    public sealed class PipelineResult
    {
        /// <summary>Final merged variant calls.</summary>
        public LocalVariantResult[] Variants { get; set; } = [];

        /// <summary>Metrics from processing.</summary>
        public PipelineMetrics Metrics { get; set; } = new();

        /// <summary>Optional BAM output (if WriteBam was enabled).</summary>
        public SamDefinition? BamOutput { get; set; }

        /// <summary>Converts variants to VCF lines (header + records).</summary>
        public string ToVcfString(string chromosome, long? chromLength = null)
        {
            using var mem = new MemoryStream();
            VcfWriter.WriteAsync(mem, Variants, chromosome, chromLength).Wait();
            return System.Text.Encoding.UTF8.GetString(mem.GetBuffer(), 0, (int)mem.Length);
        }
    }

    private readonly Sequence _reference;
    private readonly string _chromosome;
    private readonly PipelineOptions _options;
    private readonly List<LocalVariantResult> _allVariants = new();
    private readonly List<ReadAlignmentRecord> _allAlignments = new();
    private readonly object _graphLock = new();
    private readonly ReadRealigner _realigner;
    private readonly ReferenceIndex _referenceIndex;
    private readonly HashSet<int> _graphCandidateWindows = [];
    private SamDefinition? _samDefinition;
    private string? _bamFilePath;
    private readonly List<AlignmentSection> _regionAlignments = new();
    private int _skippedRealignments;

    private sealed class ReadProcessingResult
    {
        public required Sequence Read { get; init; }
        public AlignmentResult? Alignment { get; init; }
        public LocalVariantResult[] Variants { get; init; } = [];
        public List<int> GraphCandidateStarts { get; init; } = [];
        public int SkippedRealignments { get; init; }
    }

    /// <summary>
    /// Record for storing read alignment data internally (not SAM format).
    /// </summary>
    private sealed class ReadAlignmentRecord
    {
        public string ReadName { get; set; } = string.Empty;
        public AlignmentResult? Alignment { get; set; }
        public Sequence Sequence { get; set; } = null!;

        public bool IsMapped
        {
            get { return Alignment is { Score: > 0 }; }
        }
    }

    /// <summary>
    /// Creates a new variant calling pipeline.
    /// </summary>
    /// <param name="reference">The reference sequence to align against.</param>
    /// <param name="chromosome">Chromosome name for output.</param>
    /// <param name="options">Pipeline configuration. Optional; defaults used if null.</param>
    public VariantCallingPipeline(Sequence reference, string chromosome, PipelineOptions? options = null)
    {
        _reference = reference ?? throw new ArgumentNullException(nameof(reference));
        _chromosome = chromosome ?? throw new ArgumentNullException(nameof(chromosome));
        _options = options ?? new PipelineOptions();
        _referenceIndex = new ReferenceIndex(reference, new ReferenceIndex.IndexOptions
        {
            SeedSize = _options.SeedSize,
            WindowPadding = _options.CandidateWindowPadding,
            MaxCandidateWindowsPerRead = _options.MaxCandidateWindowsPerRead,
            MaxSeedHitsPerKmer = _options.MaxSeedHitsPerKmer
        });
        _realigner = new ReadRealigner
        {
            MinClipFraction = _options.MinClipFraction,
            MinClipSize = _options.MinClipSize,
            LocalWindowSize = _options.LocalWindowSize
        };
    }

    /// <summary>
    /// Loads and processes a BAM file. Reads alignment by alignment,
    /// extracts variants from each aligned read.
    /// </summary>
    public async Task<bool> LoadBamAsync(string bamFilePath, CancellationToken cancellationToken = default)
    {
        _bamFilePath = bamFilePath;
        var logger = new Microsoft.Extensions.Logging.Abstractions.NullLogger<BamReader>();
        var reader = new BamReader(bamFilePath, logger);
        _samDefinition = await reader.Read(cancellationToken).ConfigureAwait(false);

        if (_samDefinition == null || _samDefinition.AlignmentSections.Length == 0)
        {
            return false;
        }

        var reads = _samDefinition.AlignmentSections
            .Where(section => (section.Flag & AlignmentSection.AlignmentFlag.SegmentUnmapped) == 0)
            .Select(section => new Sequence(section.QName, section.Sequence.TrimEnd().ToCharArray(),
                section.Quality.TrimEnd().ToCharArray()))
            .ToList();

        await ProcessReadsAsync(reads.ToAsyncEnumerable(), cancellationToken).ConfigureAwait(false);

        return true;
    }

    /// <summary>
    /// Loads and processes a FASTQ file for variant calling.
    /// </summary>
    public async Task<bool> LoadFastQAsync(string fastqPath, CancellationToken cancellationToken = default)
    {
        var logger = Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;
        var reader = new FastQReader(logger);
        var sequences = reader.Read(fastqPath, cancellationToken);

        var anyReads = await ProcessReadsAsync(sequences, cancellationToken).ConfigureAwait(false);

        return anyReads;
    }

    /// <summary>
    /// Loads reads directly from an async sequence with optional progress reporting.
    /// Progress is reported after every 10,000 reads.
    /// </summary>
    public async Task<bool> LoadFastQAsync(
        IAsyncEnumerable<Sequence> reads,
        IProgress<PipelineProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (progress == null)
        {
            return await ProcessReadsAsync(reads, cancellationToken).ConfigureAwait(false);
        }

        var start = DateTime.UtcNow;
        var processed = 0;

        async IAsyncEnumerable<Sequence> Instrumented(
            [System.Runtime.CompilerServices.EnumeratorCancellation]
            CancellationToken ct)
        {
            await foreach (var seq in reads.WithCancellation(ct).ConfigureAwait(false))
            {
                yield return seq;
                processed++;
                if (processed % 10_000 == 0)
                {
                    ReportProgressSynchronously(progress, new PipelineProgress
                    {
                        ReadsProcessed = processed,
                        ReadsMapped = _allAlignments.Count,
                        ReadsFiltered = 0,
                        VariantsCalled = _allVariants.Count,
                        CurrentPhase = "Alignment",
                        Elapsed = DateTime.UtcNow - start
                    });
                }
            }
        }

        var result = await ProcessReadsAsync(Instrumented(cancellationToken), cancellationToken).ConfigureAwait(false);

        // Final progress report
        ReportProgressSynchronously(progress, new PipelineProgress
        {
            ReadsProcessed = processed,
            ReadsMapped = _allAlignments.Count,
            ReadsFiltered = 0,
            VariantsCalled = _allVariants.Count,
            CurrentPhase = "Complete",
            Elapsed = DateTime.UtcNow - start
        });

        return result;
    }

    /// <summary>
    /// Processes a single read: aligns against reference, calls variants,
    /// performs soft-clip realignment, and handles graph-based SV detection.
    /// </summary>
    public Task<LocalVariantResult[]> ProcessReadAsync(
        Sequence read,
        CancellationToken cancellationToken = default)
    {
        var result = AnalyzeRead(read);
        MergeReadResult(result);
        return Task.FromResult(result.Variants);
    }

    /// <summary>
    /// Invokes progress synchronously on the current thread, bypassing <see cref="Progress{T}"/>
    /// thread-pool posting to avoid race conditions in tests and tight feedback loops.
    /// </summary>
    private static void ReportProgressSynchronously(IProgress<PipelineProgress> progress, PipelineProgress value)
    {
        // Progress<T> posts callbacks to the thread pool asynchronously, which creates a race
        // condition in tests. We use reflection to directly invoke the stored action synchronously.
        if (progress is Progress<PipelineProgress> concreteProgress)
        {
            var field = typeof(Progress<PipelineProgress>)
                .GetField("_handler",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (field?.GetValue(concreteProgress) is Action<PipelineProgress> handler)
            {
                handler(value);
                return;
            }
        }

        // Fallback for custom IProgress<T> implementations (assumed synchronous)
        progress.Report(value);
    }

    private ReadProcessingResult AnalyzeRead(Sequence read)
    {
        var results = new List<LocalVariantResult>();
        var candidateWindows = new List<int>();

        // Step 1: Align read against reference using Smith-Waterman
        var alignment = AlignReadToCandidateWindows(read);

        if (alignment == null)
        {
            return new ReadProcessingResult { Read = read, Alignment = null, Variants = [] };
        }

        // Step 2: Call variants from alignment (SNPs, indels)
        var variants = VariantCaller.CallVariants(_reference, alignment, _options.MinVariantQuality);
        results.AddRange(variants);

        // Step 3: Soft-clip realignment for SV discovery
        var skippedRealignments = 0;
        if (_options.EnableSoftClipRealignment && (alignment.LeftSoftClip > 0 || alignment.RightSoftClip > 0))
        {
            candidateWindows.Add(alignment.ReferenceStartPosition);
            try
            {
                var realignResults = _realigner.Realign(alignment, _reference, _chromosome, read);

                foreach (var realign in realignResults)
                {
                    if (realign.IsSkippedByHeuristic)
                    {
                        skippedRealignments++;
                        continue;
                    }

                    if (realign is { WasRealigned: true, Alignment: not null })
                    {
                        var reAlignVariants = VariantCaller.CallVariants(_reference, realign.Alignment!,
                            _options.MinVariantQuality);
                        results.AddRange(reAlignVariants);

                        if (realign.IsStructuralVariant)
                        {
                            foreach (var v in reAlignVariants)
                            {
                                v.IsStructuralVariant = true;
                                v.SvType = realign.SvType;
                                v.EndPosition = realign.AlignedPosition + (realign.SvType == SvType.Inversion ||
                                    realign.SvType == SvType.Translocation
                                        ? 1
                                        : realign.SvType == SvType.Insertion
                                            ? realign.ClipSize
                                            : 0);
                                v.ClipInfo = new ClipInfo(
                                    realign.ClipPosition,
                                    realign.ClipSize,
                                    realign.IsLeftClip,
                                    realign.WasRealigned,
                                    realign.Alignment!.Score);
                            }
                        }
                    }
                }
            }
            catch
            {
                // Soft-clip realignment failed; fall through to just return primary alignment variants
            }
        }

        return new ReadProcessingResult
        {
            Read = read,
            Alignment = alignment,
            Variants = results.ToArray(),
            GraphCandidateStarts = candidateWindows,
            SkippedRealignments = skippedRealignments
        };
    }

    private void MergeReadResult(ReadProcessingResult result)
    {
        if (result.Alignment == null)
        {
            return;
        }

        _allAlignments.Add(new ReadAlignmentRecord
        {
            ReadName = result.Read.Id,
            Alignment = result.Alignment,
            Sequence = result.Read
        });

        foreach (var v in result.Variants)
        {
            v.Chromosome = _chromosome;
            _allVariants.Add(v);
        }

        _skippedRealignments += result.SkippedRealignments;

        foreach (var start in result.GraphCandidateStarts)
        {
            _graphCandidateWindows.Add(start);
        }
    }

    private async Task<bool> ProcessReadsAsync(IAsyncEnumerable<Sequence> reads, CancellationToken cancellationToken)
    {
        var degreeOfParallelism = Math.Max(1, _options.DegreeOfParallelism);
        var pending = new List<Task<ReadProcessingResult>>(degreeOfParallelism);
        var sawAnyRead = false;

        await foreach (var read in reads.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            sawAnyRead = true;
            if (degreeOfParallelism == 1)
            {
                MergeReadResult(AnalyzeRead(read));
                continue;
            }

            pending.Add(Task.Run(() => AnalyzeRead(read), cancellationToken));
            if (pending.Count < degreeOfParallelism)
            {
                continue;
            }

            var completed = await Task.WhenAny(pending).ConfigureAwait(false);
            pending.Remove(completed);
            MergeReadResult(await completed.ConfigureAwait(false));
        }

        while (pending.Count > 0)
        {
            var completed = await Task.WhenAny(pending).ConfigureAwait(false);
            pending.Remove(completed);
            MergeReadResult(await completed.ConfigureAwait(false));
        }

        return sawAnyRead;
    }

    private AlignmentResult? AlignReadToCandidateWindows(Sequence read)
    {
        var candidateWindows = _referenceIndex.FindCandidateWindows(read);
        if (candidateWindows.Length == 0)
        {
            return null;
        }

        AlignmentResult? bestAlignment = null;
        var runnerUpScore = int.MinValue;

        foreach (var candidateWindow in candidateWindows)
        {
            var windowLength = candidateWindow.End - candidateWindow.Start;
            if (windowLength <= 0)
            {
                continue;
            }

            var referenceWindow = _reference.Slice(candidateWindow.Start, windowLength);
            var localAlignment = SmithWatermanAligner.Align(
                referenceWindow,
                read,
                _options.MatchScore,
                _options.MismatchPenalty,
                _options.GapOpenPenalty,
                _options.GapExtendPenalty,
                _options.MinAlignmentScore,
                _options.MaxAlignmentCellCount,
                _options.AlignmentBandWidth,
                _options.AlignmentXDrop,
                candidateWindow.PreferredStartOffset);

            if (localAlignment == null)
            {
                continue;
            }

            var globalAlignment = new AlignmentResult(
                localAlignment.AlignedReference,
                localAlignment.AlignedRead,
                localAlignment.VisualString,
                localAlignment.Score,
                localAlignment.ReferenceStartPosition + candidateWindow.Start,
                localAlignment.LeftSoftClip,
                localAlignment.RightSoftClip,
                localAlignment.WasPruned);

            if (bestAlignment == null || globalAlignment.Score > bestAlignment.Score)
            {
                runnerUpScore = bestAlignment?.Score ?? int.MinValue;
                bestAlignment = globalAlignment;
            }
            else if (globalAlignment.Score > runnerUpScore)
            {
                runnerUpScore = globalAlignment.Score;
            }
        }

        return bestAlignment;
    }

    /// <summary>
    /// Groups local variants by position/allele and computes consensus depth/quality.
    /// </summary>
    public LocalVariantResult[] GetMergedVariants()
    {
        return VariantCaller.MergeVariants(_allVariants);
    }

    /// <summary>
    /// Runs De Bruijn graph analysis for SV detection on reads aligned to a local region.
    /// Builds a graph from reads overlapping [windowStart, windowEnd) and detects bubbles/tips.
    /// </summary>
    public async Task<StructuralVariantAnalysis> RunGraphAnalysisAsync(
        int windowStart,
        int windowEnd,
        CancellationToken cancellationToken = default)
    {
        // Filter reads to those overlapping the window
        var windowReads = new List<Sequence>();

        foreach (var record in _allAlignments)
        {
            if (record.Alignment == null || !record.IsMapped)
            {
                continue;
            }

            var readStart = record.Alignment!.ReferenceStartPosition;
            var readEnd = readStart + record.Alignment!.AlignedReference.Length;

            if (readEnd > windowStart && readStart < windowEnd)
            {
                windowReads.Add(record.Sequence);
            }
        }

        // Need minimum coverage for graph analysis
        if (windowReads.Count < _options.MinGraphCoverage)
        {
            return new StructuralVariantAnalysis([]);
        }

        // Slice reference window
        var windowLen = Math.Min(_reference.Length, windowEnd - windowStart);
        var windowRef = _reference.Slice(Math.Max(0, windowStart), windowLen);

        var kmerGraph = new DeBruijnGraph(_options.KmerSize, windowReads.ToAsyncEnumerable());

        var analysis = await StructuralVariantDetector.AnalyzeGraph(
            kmerGraph,
            new string(windowRef.GetData().Span),
            _chromosome,
            windowStart).ConfigureAwait(false);

        // Add any graph-discovered SVs to the variant list (lock for thread-safety when called in parallel)
        lock (_graphLock)
        {
            foreach (var v in analysis.Variants)
            {
                v.Chromosome = _chromosome;
                _allVariants.Add(v);
            }
        }

        return analysis;
    }

    /// <summary>
    /// Runs graph analysis on the full reference (de novo SV discovery).
    /// </summary>
    public async Task<StructuralVariantAnalysis> RunFullGraphAnalysisAsync(
        CancellationToken cancellationToken = default)
    {
        var candidateStarts = _graphCandidateWindows
            .OrderBy(value => value)
            .Take(_options.MaxGraphWindowsPerSample > 0 ? _options.MaxGraphWindowsPerSample : int.MaxValue)
            .ToArray();

        if (candidateStarts.Length == 0)
        {
            candidateStarts = [0];
        }

        var collectedVariants = new ConcurrentBag<LocalVariantResult>();

        await Parallel.ForEachAsync(
            candidateStarts,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = Math.Max(1, _options.DegreeOfParallelism),
                CancellationToken = cancellationToken
            },
            async (candidateStart, ct) =>
            {
                var windowStart = Math.Max(0, candidateStart - _options.GraphWindowBp / 2);
                var windowEnd = Math.Min(_reference.Length, windowStart + _options.GraphWindowBp);
                var analysis = await RunGraphAnalysisAsync(windowStart, windowEnd, ct).ConfigureAwait(false);
                foreach (var v in analysis.Variants)
                {
                    collectedVariants.Add(v);
                }
            }).ConfigureAwait(false);

        return new StructuralVariantAnalysis(collectedVariants.ToArray());
    }

    /// <summary>
    /// Builds the final pipeline result with merged variants and metrics.
    /// </summary>
    public PipelineResult BuildResult()
    {
        var merged = GetMergedVariants();
        var svCount = merged.Count(v => v.IsStructuralVariant);

        var mappedCount = _allAlignments.Count(r => r.IsMapped);
        var realignedCount = _allAlignments.Count(r =>
            r.Alignment != null && (r.Alignment!.LeftSoftClip > 0 || r.Alignment!.RightSoftClip > 0));

        var metrics = new PipelineMetrics
        {
            ReadsProcessed = _allAlignments.Count,
            ReadsMapped = mappedCount,
            ReadsRealigned = realignedCount,
            VariantsCalled = _allVariants.Count,
            VariantsFinal = merged.Length,
            StructuralVariants = svCount,
            SkippedRealignments = _skippedRealignments
        };

        return new PipelineResult
        {
            Variants = merged.OrderBy(v => v.Position).ToArray(),
            Metrics = metrics,
            BamOutput = _samDefinition
        };
    }

    /// <summary>
    /// Writes the pipeline variants to a VCF file.
    /// </summary>
    public async Task WriteVcfAsync(string filePath, long? chromLength = null)
    {
        using var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write);
        VcfWriter.WriteAsync(fs, _allVariants, _chromosome, chromLength).Wait();
    }

    /// <summary>
    /// Writes alignments to a SAM file (for reference/debugging).
    /// </summary>
    public async Task WriteSamAsync(string filePath)
    {
        if (_samDefinition == null)
        {
            return;
        }

        using var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write);
        var writer = new SamWriter(Microsoft.Extensions.Logging.Abstractions
            .NullLogger<SamWriter>.Instance);
        await writer.Write(_samDefinition, fs, CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>
    /// Queries alignments in a specific genomic region via BamReader.
    /// Returns empty when no BAM file path is available.
    /// </summary>
    public async Task<List<AlignmentSection>> QueryRegionAsync(
        string referenceName,
        int start,
        int end,
        CancellationToken cancellationToken = default)
    {
        var bamFilePath = _bamFilePath;
        if (string.IsNullOrEmpty(bamFilePath))
        {
            _regionAlignments.Clear();
            return [];
        }

        List<AlignmentSection> localResults;
        try
        {
            var reader = new BamReader(bamFilePath, NullLogger<BamReader>.Instance);
            localResults = new List<AlignmentSection>();
            await foreach (var section in reader.QueryRegionAsync(referenceName, start, end, cancellationToken)
                .ConfigureAwait(false))
            {
                _regionAlignments.Add(section);
                localResults.Add(section);
            }
        }
        catch
        {
            return [];
        }

        return localResults;
    }

    /// <summary>
    /// Filters variants to a specific genomic region.
    /// </summary>
    public LocalVariantResult[] GetVariantsInRegion(int start, int end)
    {
        return _allVariants
            .Where(v => v.Position >= start && v.EndPosition <= end)
            .OrderBy(v => v.Position)
            .ToArray();
    }

    /// <summary>
    /// Gets a quality summary: counts by variant type.
    /// </summary>
    public Dictionary<string, int> GetVariantCounts()
    {
        var counts = new Dictionary<string, int>();
        counts["SNP"] = 0;
        counts["Insertion"] = 0;
        counts["Deletion"] = 0;
        counts["Inversion"] = 0;
        counts["Translocation"] = 0;
        counts["SV"] = 0;

        foreach (var v in _allVariants)
        {
            if (v.IsStructuralVariant)
            {
                counts["SV"]++;
                if (v.SvType.HasValue)
                {
                    var key = ((SvType)v.SvType).ToString();
                    if (key != null && counts.ContainsKey(key))
                    {
                        counts[key]++;
                    }
                }
            }
            else
            {
                // Classify local variants
                if (string.IsNullOrEmpty(v.Reference) || string.IsNullOrEmpty(v.Alternate))
                {
                    continue;
                }

                if (v.Reference.Length > v.Alternate.Length)
                {
                    counts["Deletion"]++;
                }
                else if (v.Alternate.Length > v.Reference.Length)
                {
                    counts["Insertion"]++;
                }
                else
                {
                    counts["SNP"]++;
                }
            }
        }

        return counts;
    }
}

/// <summary>
/// Extension for converting collections to IAsyncEnumerable.
/// </summary>
public static class AsyncEnumerableExtensions
{
    public static async IAsyncEnumerable<T> ToAsyncEnumerable<T>(this IEnumerable<T> source)
    {
        foreach (var item in source)
        {
            await Task.Yield();
            yield return item;
        }
    }
}
