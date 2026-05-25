using Microsoft.Extensions.Logging.Abstractions;
using OpenMedStack.BioSharp.Model.Alignment;

namespace OpenMedStack.BioSharp.Calculations.Alignment;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
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
        /// Minimum number of read observations supporting the alternate allele for a merged
        /// variant to be accepted into the final output. Default 1 preserves the historical
        /// per-read BioSharp behavior. Freebayes defaults to 2.
        /// </summary>
        public int MinAlternateObservationCount { get; set; } = 1;

        /// <summary>
        /// Minimum fraction of covering reads that must support the alternate allele for a
        /// merged variant to be accepted into the final output. Default 0 preserves the
        /// historical BioSharp behavior. Freebayes defaults to 0.20.
        /// </summary>
        public double MinAlternateFraction { get; set; }

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
        /// Maximum degree of parallelism used when evaluating candidate alignment windows for a single read.
        /// Set to 1 to keep candidate-window evaluation serial.
        /// </summary>
        public int CandidateAlignmentDegreeOfParallelism { get; set; } = 1;

        /// <summary>
        /// Minimum number of candidate windows required before intra-read candidate alignment parallelism is enabled.
        /// </summary>
        public int ParallelCandidateWindowThreshold { get; set; } = 6;

        /// <summary>
        /// Minimum candidate window length required before intra-read candidate alignment parallelism is enabled.
        /// </summary>
        public int ParallelCandidateMinWindowSize { get; set; } = 256;

        /// <summary>
        /// Degree of parallelism for FASTQ/BAM read processing.
        /// </summary>
        public int DegreeOfParallelism { get; set; } = 10;

        /// <summary>
        /// Maximum number of reads buffered between the input reader and analysis workers.
        /// Set to 0 to derive a bounded capacity from <see cref="DegreeOfParallelism"/>.
        /// </summary>
        public int ReadBufferCapacity { get; set; }

        /// <summary>
        /// Try insert-size constrained candidate windows for the mate of an already mapped paired-end read.
        /// </summary>
        public bool EnablePairedEndInsertConstrainedAlignment { get; set; } = true;

        /// <summary>
        /// Expected paired-end template size used to seed mate candidate windows.
        /// </summary>
        public int ExpectedInsertSize { get; set; } = 350;

        /// <summary>
        /// Padding around the expected mate position when building insert-constrained windows.
        /// </summary>
        public int InsertSizeTolerance { get; set; } = 500;

        /// <summary>
        /// For BAM inputs, use existing BAM coordinates and CIGAR instead of re-aligning each read.
        /// </summary>
        public bool UseBamAlignmentCoordinates { get; set; } = true;

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
            return
                $"PipelineMetrics{{reads={ReadsProcessed}, mapped={ReadsMapped}, aligned={ReadsRealigned}, called={VariantsCalled}, final={VariantsFinal}, sv={StructuralVariants}, skippedRealign={SkippedRealignments}}}";
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
            VcfWriter.Write(mem, Variants, chromosome, chromLength).Wait();
            return System.Text.Encoding.UTF8.GetString(mem.GetBuffer(), 0, (int)mem.Length);
        }
    }

    /// <summary>
    /// Stage timings captured by the most recent BAM load.
    /// </summary>
    public sealed class BamLoadProfile
    {
        public int RecordsRead { get; set; }
        public int RecordsAccepted { get; set; }
        public double BamReadMilliseconds { get; set; }
        public double VariantCallingMilliseconds { get; set; }
    }

    private readonly Sequence _reference;
    private readonly string _chromosome;
    private readonly PipelineOptions _options;
    private readonly ReferenceAlignmentContext _referenceContext;
    private readonly List<LocalVariantResult> _allVariants = [];
    private readonly List<ReadAlignmentRecord> _allAlignments = [];
    private readonly Lock _graphLock = new();
    private readonly ReadRealigner _realigner;
    private ReferenceIndex? _referenceIndex;
    private readonly HashSet<int> _graphCandidateWindows = [];

    /// <summary>
    /// Optional custom seeder used instead of the built-in
    /// <see cref="ReferenceIndex"/>.  Assign an
    /// <see cref="BurrowsWheeler.FmIndexSeeder"/> (or any
    /// <see cref="IReferenceSeeder"/> implementation) before processing reads
    /// to use FM-index–based seeding with BWA-MEM–style MEM seeds.
    ///
    /// When <c>null</c> (the default) the pipeline falls back to the
    /// hash-map k-mer index built by <see cref="EnsureReferenceIndex"/>.
    /// </summary>
    public IReferenceSeeder? Seeder { get; set; }
    private SamDefinition? _samDefinition;
    private string? _bamFilePath;
    private readonly List<AlignmentSection> _regionAlignments = [];
    private int _skippedRealignments;
    private int[]? _coverageByPosition;

    public BamLoadProfile LastBamLoadProfile { get; } = new();

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
        public Sequence? Sequence { get; set; }

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
        : this(ReferenceAlignmentContext.GetShared(reference), chromosome, options)
    {
    }

    public VariantCallingPipeline(
        ReferenceAlignmentContext referenceContext,
        string chromosome,
        PipelineOptions? options = null)
    {
        _referenceContext = referenceContext ?? throw new ArgumentNullException(nameof(referenceContext));
        _reference = referenceContext.Reference;
        _chromosome = chromosome ?? throw new ArgumentNullException(nameof(chromosome));
        _options = options ?? new PipelineOptions();
        _realigner = new ReadRealigner
        {
            MinClipFraction = _options.MinClipFraction,
            MinClipSize = _options.MinClipSize,
            LocalWindowSize = _options.LocalWindowSize
        };
    }

    public ReferenceAlignmentContext ReferenceContext => _referenceContext;

    public ReferenceIndex EnsureReferenceIndex()
    {
        return _referenceIndex ??= _referenceContext.GetOrCreateIndex(CreateReferenceIndexOptions());
    }

    public ReferenceIndex LoadReferenceIndex(string path)
    {
        _referenceIndex = _referenceContext.LoadIndex(path, CreateReferenceIndexOptions());
        return _referenceIndex;
    }

    public void SaveReferenceIndex(string path)
    {
        EnsureReferenceIndex().Save(path);
    }

    /// <summary>
    /// Loads and processes a BAM file. Reads alignment by alignment,
    /// extracts variants from each aligned read.
    /// </summary>
    public async Task<bool> LoadBam(string bamFilePath, CancellationToken cancellationToken = default)
    {
        _bamFilePath = bamFilePath;
        var logger = new NullLogger<BamReader>();
        var reader = new BamReader(bamFilePath, logger);

        LastBamLoadProfile.RecordsRead = 0;
        LastBamLoadProfile.RecordsAccepted = 0;
        LastBamLoadProfile.BamReadMilliseconds = 0;
        LastBamLoadProfile.VariantCallingMilliseconds = 0;

        if (_options.UseBamAlignmentCoordinates)
        {
            _samDefinition = null;
            var sawAnyAlignment = false;
            var readStopwatch = System.Diagnostics.Stopwatch.StartNew();
            await foreach (var section in reader.ReadAlignmentSections(cancellationToken).ConfigureAwait(false))
            {
                LastBamLoadProfile.RecordsRead++;
                if (!IsCallableBamAlignment(section))
                {
                    continue;
                }

                sawAnyAlignment = true;
                LastBamLoadProfile.RecordsAccepted++;
                readStopwatch.Stop();
                var callStopwatch = System.Diagnostics.Stopwatch.StartNew();
                MergeReadResult(AnalyzeBamAlignment(section));
                callStopwatch.Stop();
                LastBamLoadProfile.VariantCallingMilliseconds += callStopwatch.Elapsed.TotalMilliseconds;
                readStopwatch.Start();
            }

            readStopwatch.Stop();
            LastBamLoadProfile.BamReadMilliseconds = readStopwatch.Elapsed.TotalMilliseconds;
            return sawAnyAlignment;
        }

        _samDefinition = null;
        return await ProcessReads(StreamCallableBamReads(reader, cancellationToken), cancellationToken)
            .ConfigureAwait(false);
    }

    private static async IAsyncEnumerable<Sequence> StreamCallableBamReads(
        BamReader reader,
        [System.Runtime.CompilerServices.EnumeratorCancellation]
        CancellationToken cancellationToken)
    {
        await foreach (var section in reader.ReadAlignmentSections(cancellationToken).ConfigureAwait(false))
        {
            if (!IsCallableBamAlignment(section))
            {
                continue;
            }

            yield return new Sequence(
                section.QName,
                section.Sequence.TrimEnd().AsMemory(),
                section.Quality.TrimEnd().AsMemory());
        }
    }

    /// <summary>
    /// Loads and processes a FASTQ file for variant calling.
    /// </summary>
    public async Task<bool> LoadFastQ(string fastqPath, CancellationToken cancellationToken = default)
    {
        var logger = NullLogger.Instance;
        var reader = new FastQReader(logger);
        var sequences = reader.Read(fastqPath, cancellationToken);

        var anyReads = await ProcessReads(sequences, cancellationToken).ConfigureAwait(false);

        return anyReads;
    }

    /// <summary>
    /// Loads reads directly from an async sequence with optional progress reporting.
    /// Progress is reported after every 10,000 reads.
    /// </summary>
    public async Task<bool> LoadFastQ(
        IAsyncEnumerable<Sequence> reads,
        IProgress<PipelineProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (progress == null)
        {
            return await ProcessReads(reads, cancellationToken).ConfigureAwait(false);
        }

        var start = DateTime.UtcNow;
        var processed = 0;

        var result = await ProcessReads(Instrumented(cancellationToken), cancellationToken).ConfigureAwait(false);

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
    }

    /// <summary>
    /// Loads matched paired-end reads and uses the alignment of the first mapped mate to constrain
    /// the initial candidate windows for the second mate.
    /// </summary>
    public async Task<bool> LoadPairedFastQ(
        IAsyncEnumerable<(Sequence R1, Sequence R2)> readPairs,
        CancellationToken cancellationToken = default)
    {
        return await ProcessReadPairs(readPairs, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Processes a single read: aligns against reference, calls variants,
    /// performs soft-clip realignment, and handles graph-based SV detection.
    /// </summary>
    public LocalVariantResult[] ProcessRead(
        Sequence read,
        CancellationToken cancellationToken = default)
    {
        var result = AnalyzeRead(read);
        MergeReadResult(result);
        return result.Variants;
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

    private ReadProcessingResult AnalyzeRead(
        Sequence read,
        ReferenceIndex.CandidateWindow[]? preferredCandidateWindows = null)
    {
        var results = new List<LocalVariantResult>();
        var candidateWindows = new List<int>();

        // Step 1: Align read against reference using Smith-Waterman
        var alignment = AlignReadToCandidateWindows(read, preferredCandidateWindows);

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
                                v.EndPosition = realign.AlignedPosition +
                                    (realign.SvType is SvType.Inversion or SvType.Translocation
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
            Sequence = _options.EnableGraphSvDetection ? result.Read : null
        });
        _coverageByPosition = null;

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

    private async Task<bool> ProcessReads(IAsyncEnumerable<Sequence> reads, CancellationToken cancellationToken)
    {
        var degreeOfParallelism = Math.Max(1, _options.DegreeOfParallelism);
        if (degreeOfParallelism == 1)
        {
            var sawAnySequentialRead = false;
            await foreach (var read in reads.WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                sawAnySequentialRead = true;
                MergeReadResult(AnalyzeRead(read));
            }

            return sawAnySequentialRead;
        }

        var bufferCapacity = _options.ReadBufferCapacity > 0
            ? _options.ReadBufferCapacity
            : Math.Max(4, degreeOfParallelism * 4);
        var inputChannel = Channel.CreateBounded<Sequence>(new BoundedChannelOptions(bufferCapacity)
        {
            AllowSynchronousContinuations = false,
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = false,
            SingleWriter = true
        });
        var resultChannel = Channel.CreateBounded<ReadProcessingResult>(new BoundedChannelOptions(bufferCapacity)
        {
            AllowSynchronousContinuations = false,
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });
        var sawAnyRead = false;

        var producer = Task.Run(async () =>
        {
            try
            {
                await foreach (var read in reads.WithCancellation(cancellationToken).ConfigureAwait(false))
                {
                    sawAnyRead = true;
                    await inputChannel.Writer.WriteAsync(read, cancellationToken).ConfigureAwait(false);
                }

                inputChannel.Writer.TryComplete();
            }
            catch (Exception exception)
            {
                inputChannel.Writer.TryComplete(exception);
                throw;
            }
        }, cancellationToken);

        var workers = Enumerable.Range(0, degreeOfParallelism)
            .Select(_ => Task.Run(async () =>
            {
                await foreach (var read in inputChannel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
                {
                    var result = AnalyzeRead(read);
                    await resultChannel.Writer.WriteAsync(result, cancellationToken).ConfigureAwait(false);
                }
            }, cancellationToken))
            .ToArray();

        var completeResults = Task.Run(async () =>
        {
            try
            {
                await Task.WhenAll(workers).ConfigureAwait(false);
                resultChannel.Writer.TryComplete();
            }
            catch (Exception exception)
            {
                resultChannel.Writer.TryComplete(exception);
                throw;
            }
        }, CancellationToken.None);

        try
        {
            await foreach (var result in resultChannel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                MergeReadResult(result);
            }
        }
        finally
        {
            await producer.ConfigureAwait(false);
            await completeResults.ConfigureAwait(false);
        }

        return sawAnyRead;
    }

    private async Task<bool> ProcessReadPairs(
        IAsyncEnumerable<(Sequence R1, Sequence R2)> readPairs,
        CancellationToken cancellationToken)
    {
        var degreeOfParallelism = Math.Max(1, _options.DegreeOfParallelism);
        if (degreeOfParallelism == 1)
        {
            var sawAnySequentialPair = false;
            await foreach (var readPair in readPairs.WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                sawAnySequentialPair = true;
                foreach (var result in AnalyzeReadPair(readPair.R1, readPair.R2))
                {
                    MergeReadResult(result);
                }
            }

            return sawAnySequentialPair;
        }

        var bufferCapacity = _options.ReadBufferCapacity > 0
            ? _options.ReadBufferCapacity
            : Math.Max(4, degreeOfParallelism * 4);
        var inputChannel = Channel.CreateBounded<(Sequence R1, Sequence R2)>(new BoundedChannelOptions(bufferCapacity)
        {
            AllowSynchronousContinuations = false,
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = false,
            SingleWriter = true
        });
        var resultChannel = Channel.CreateBounded<ReadProcessingResult[]>(new BoundedChannelOptions(bufferCapacity)
        {
            AllowSynchronousContinuations = false,
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });
        var sawAnyPair = false;

        var producer = Task.Run(async () =>
        {
            try
            {
                await foreach (var readPair in readPairs.WithCancellation(cancellationToken).ConfigureAwait(false))
                {
                    sawAnyPair = true;
                    await inputChannel.Writer.WriteAsync(readPair, cancellationToken).ConfigureAwait(false);
                }

                inputChannel.Writer.TryComplete();
            }
            catch (Exception exception)
            {
                inputChannel.Writer.TryComplete(exception);
                throw;
            }
        }, cancellationToken);

        var workers = Enumerable.Range(0, degreeOfParallelism)
            .Select(_ => Task.Run(async () =>
            {
                await foreach (var readPair in inputChannel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
                {
                    var results = AnalyzeReadPair(readPair.R1, readPair.R2);
                    await resultChannel.Writer.WriteAsync(results, cancellationToken).ConfigureAwait(false);
                }
            }, cancellationToken))
            .ToArray();

        var completeResults = Task.Run(async () =>
        {
            try
            {
                await Task.WhenAll(workers).ConfigureAwait(false);
                resultChannel.Writer.TryComplete();
            }
            catch (Exception exception)
            {
                resultChannel.Writer.TryComplete(exception);
                throw;
            }
        }, CancellationToken.None);

        try
        {
            await foreach (var results in resultChannel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                foreach (var result in results)
                {
                    MergeReadResult(result);
                }
            }
        }
        finally
        {
            await producer.ConfigureAwait(false);
            await completeResults.ConfigureAwait(false);
        }

        return sawAnyPair;
    }

    private ReadProcessingResult[] AnalyzeReadPair(Sequence firstRead, Sequence secondRead)
    {
        var firstResult = AnalyzeRead(firstRead);
        var secondPreferredWindows = _options.EnablePairedEndInsertConstrainedAlignment && firstResult.Alignment != null
            ? BuildMateCandidateWindows(firstResult.Alignment, secondRead.Length)
            : null;
        var secondResult = AnalyzeRead(secondRead, secondPreferredWindows);

        if (secondResult.Alignment == null && firstResult.Alignment == null)
        {
            return [firstResult, secondResult];
        }

        if (!_options.EnablePairedEndInsertConstrainedAlignment || secondResult.Alignment != null)
        {
            return [firstResult, secondResult];
        }

        return [firstResult, AnalyzeRead(secondRead)];
    }

    private ReadProcessingResult AnalyzeBamAlignment(AlignmentSection section)
    {
        var read = new Sequence(
            section.QName,
            section.Sequence.TrimEnd().AsMemory(),
            section.Quality.TrimEnd().AsMemory());
        var alignment = BuildAlignmentFromBamSection(section);
        if (alignment == null)
        {
            return new ReadProcessingResult { Read = read, Alignment = null, Variants = [] };
        }

        var variants = VariantCaller.CallVariants(_reference, alignment, _options.MinVariantQuality);
        return new ReadProcessingResult
        {
            Read = read,
            Alignment = alignment,
            Variants = variants,
            GraphCandidateStarts = _options.EnableGraphSvDetection ? [alignment.ReferenceStartPosition] : []
        };
    }

    private AlignmentResult? BuildAlignmentFromBamSection(AlignmentSection section)
    {
        var referencePosition = Math.Max(0, section.Position - 1);
        var readPosition = 0;
        var leftSoftClip = 0;
        var rightSoftClip = 0;
        var referenceSpan = _reference.GetData().Span;
        var referenceBuilder = new System.Text.StringBuilder(section.Sequence.Length + 16);
        var readBuilder = new System.Text.StringBuilder(section.Sequence.Length + 16);
        var visualBuilder = new System.Text.StringBuilder(section.Sequence.Length + 16);
        var score = 0;

        foreach (var (countValue, op) in section.Cigar)
        {
            var count = checked((int)countValue);
            switch (op)
            {
                case CigarOp.Match:
                case CigarOp.Equal:
                case CigarOp.Difference:
                    for (var offset = 0; offset < count; offset++)
                    {
                        if (referencePosition >= referenceSpan.Length || readPosition >= section.Sequence.Length)
                        {
                            return null;
                        }

                        var referenceBase = DnaEncoding.Normalize(referenceSpan[referencePosition++]);
                        var readBase = DnaEncoding.Normalize(section.Sequence[readPosition++]);
                        referenceBuilder.Append(referenceBase);
                        readBuilder.Append(readBase);
                        if (DnaEncoding.AreEqual(referenceBase, readBase))
                        {
                            visualBuilder.Append('|');
                            score += _options.MatchScore;
                        }
                        else
                        {
                            visualBuilder.Append('X');
                            score += _options.MismatchPenalty;
                        }
                    }

                    break;
                case CigarOp.Insertion:
                    for (var offset = 0; offset < count; offset++)
                    {
                        if (readPosition >= section.Sequence.Length)
                        {
                            return null;
                        }

                        referenceBuilder.Append('-');
                        readBuilder.Append(DnaEncoding.Normalize(section.Sequence[readPosition++]));
                        visualBuilder.Append(' ');
                        score += offset == 0 ? _options.GapOpenPenalty : _options.GapExtendPenalty;
                    }

                    break;
                case CigarOp.Deletion:
                    for (var offset = 0; offset < count; offset++)
                    {
                        if (referencePosition >= referenceSpan.Length)
                        {
                            return null;
                        }

                        referenceBuilder.Append(DnaEncoding.Normalize(referenceSpan[referencePosition++]));
                        readBuilder.Append('-');
                        visualBuilder.Append(' ');
                        score += offset == 0 ? _options.GapOpenPenalty : _options.GapExtendPenalty;
                    }

                    break;
                case CigarOp.SoftClip:
                    if (readPosition == 0)
                    {
                        leftSoftClip += count;
                    }
                    else
                    {
                        rightSoftClip += count;
                    }

                    readPosition += count;
                    break;
                case CigarOp.Skip:
                    referencePosition += count;
                    break;
                case CigarOp.HardClip:
                case CigarOp.Padding:
                    break;
            }
        }

        if (referenceBuilder.Length == 0 || score < _options.MinAlignmentScore)
        {
            return null;
        }

        return new AlignmentResult(
            referenceBuilder.ToString(),
            readBuilder.ToString(),
            visualBuilder.ToString(),
            score,
            Math.Max(0, section.Position - 1),
            leftSoftClip,
            rightSoftClip);
    }

    private static bool IsCallableBamAlignment(AlignmentSection section)
    {
        const AlignmentSection.AlignmentFlag excluded =
            AlignmentSection.AlignmentFlag.SegmentUnmapped |
            AlignmentSection.AlignmentFlag.SecondaryAlignment |
            AlignmentSection.AlignmentFlag.SupplementaryAlignment;

        return (section.Flag & excluded) == 0 && section.Sequence.Length > 0;
    }

    private AlignmentResult? AlignReadToCandidateWindows(
        Sequence read,
        ReferenceIndex.CandidateWindow[]? preferredCandidateWindows = null)
    {
        var candidateWindows = preferredCandidateWindows is { Length: > 0 }
            ? preferredCandidateWindows
            : (Seeder ?? EnsureReferenceIndex()).FindCandidateWindows(read);
        if (candidateWindows.Length == 0)
        {
            return null;
        }

        var candidateParallelism = Math.Max(1, _options.CandidateAlignmentDegreeOfParallelism);
        if (candidateParallelism > 1 &&
            candidateWindows.Length >= _options.ParallelCandidateWindowThreshold &&
            candidateWindows.Any(window => window.End - window.Start >= _options.ParallelCandidateMinWindowSize))
        {
            return AlignReadToCandidateWindowsInParallel(read, candidateWindows, candidateParallelism);
        }

        AlignmentResult? bestAlignment = null;
        var runnerUpScore = int.MinValue;

        foreach (var candidateWindow in candidateWindows)
        {
            var globalAlignment = AlignReadToCandidateWindow(read, candidateWindow);
            if (globalAlignment == null)
            {
                continue;
            }

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

    private ReferenceIndex.CandidateWindow[] BuildMateCandidateWindows(AlignmentResult anchorAlignment, int mateLength)
    {
        var expectedInsertSize = Math.Max(mateLength, _options.ExpectedInsertSize);
        var tolerance = Math.Max(_options.CandidateWindowPadding, _options.InsertSizeTolerance);
        var anchorStart = anchorAlignment.ReferenceStartPosition;
        var anchorReferenceLength = Math.Max(1, DnaEncoding.CountNonGap(anchorAlignment.AlignedReference.AsSpan()));
        var downstreamStart = anchorStart + expectedInsertSize - mateLength;
        var upstreamStart = anchorStart + anchorReferenceLength - expectedInsertSize;

        return BuildCandidateWindowsFromEstimatedStarts([downstreamStart, upstreamStart], mateLength, tolerance);
    }

    private ReferenceIndex.CandidateWindow[] BuildCandidateWindowsFromEstimatedStarts(
        int[] estimatedStarts,
        int readLength,
        int tolerance)
    {
        var windows = new List<ReferenceIndex.CandidateWindow>(estimatedStarts.Length);
        foreach (var estimatedStart in estimatedStarts)
        {
            var start = Math.Max(0, estimatedStart - tolerance);
            var end = Math.Min(_reference.Length, estimatedStart + readLength + tolerance);
            if (end <= start)
            {
                continue;
            }

            var preferredOffset = Math.Clamp(estimatedStart - start, 0, end - start);
            var merged = false;
            for (var windowIndex = 0; windowIndex < windows.Count; windowIndex++)
            {
                var existing = windows[windowIndex];
                if (start > existing.End || end < existing.Start)
                {
                    continue;
                }

                windows[windowIndex] = new ReferenceIndex.CandidateWindow(
                    Math.Min(existing.Start, start),
                    Math.Max(existing.End, end),
                    existing.SeedHits + 1,
                    existing.PreferredStartOffset);
                merged = true;
                break;
            }

            if (!merged)
            {
                windows.Add(new ReferenceIndex.CandidateWindow(start, end, 1, preferredOffset));
            }
        }

        return windows.ToArray();
    }

    private ReferenceIndex.IndexOptions CreateReferenceIndexOptions()
    {
        return new ReferenceIndex.IndexOptions
        {
            SeedSize = _options.SeedSize,
            WindowPadding = _options.CandidateWindowPadding,
            MaxCandidateWindowsPerRead = _options.MaxCandidateWindowsPerRead,
            MaxSeedHitsPerKmer = _options.MaxSeedHitsPerKmer
        };
    }

    private LocalVariantResult[] ApplyReadAcceptanceFilters(LocalVariantResult[] mergedVariants)
    {
        var minAlternateObservationCount = Math.Max(1, _options.MinAlternateObservationCount);
        var minAlternateFraction = Math.Clamp(_options.MinAlternateFraction, 0d, 1d);

        if (minAlternateObservationCount <= 1 && minAlternateFraction <= 0d)
        {
            return mergedVariants;
        }

        var acceptedVariants = new List<LocalVariantResult>(mergedVariants.Length);
        foreach (var variant in mergedVariants)
        {
            var altCoverage = Math.Max(0, variant.Depth);
            if (altCoverage < minAlternateObservationCount)
            {
                continue;
            }

            var totalCoverage = EstimateTotalCoverage(variant.Position);
            if (totalCoverage <= 0)
            {
                continue;
            }

            var alternateFraction = (double)altCoverage / totalCoverage;
            if (alternateFraction < minAlternateFraction)
            {
                continue;
            }

            variant.Genotype ??= GenotypeCaller.Call(
                Math.Max(0, totalCoverage - altCoverage),
                altCoverage);
            acceptedVariants.Add(variant);
        }

        return acceptedVariants.ToArray();
    }

    private int EstimateTotalCoverage(int position)
    {
        var coverageByPosition = _coverageByPosition ??= BuildCoverageByPosition();
        return position >= 0 && position < coverageByPosition.Length ? coverageByPosition[position] : 0;
    }

    private int[] BuildCoverageByPosition()
    {
        var coverageByPosition = new int[_reference.Length + 1];
        foreach (var record in _allAlignments)
        {
            if (!record.IsMapped || record.Alignment == null)
            {
                continue;
            }

            var referencePosition = record.Alignment.ReferenceStartPosition + 1;
            foreach (var referenceBase in record.Alignment.AlignedReference)
            {
                if (referenceBase == '-')
                {
                    continue;
                }

                if (referencePosition >= 0 && referencePosition < coverageByPosition.Length)
                {
                    coverageByPosition[referencePosition]++;
                }

                referencePosition++;
            }
        }

        return coverageByPosition;
    }

    private AlignmentResult? AlignReadToCandidateWindowsInParallel(
        Sequence read,
        ReferenceIndex.CandidateWindow[] candidateWindows,
        int candidateParallelism)
    {
        AlignmentResult? bestAlignment = null;
        var runnerUpScore = int.MinValue;
        var bestAlignmentLock = new Lock();

        Parallel.ForEach(
            candidateWindows,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = Math.Min(candidateParallelism, candidateWindows.Length)
            },
            () => (Best: (AlignmentResult?)null, RunnerUpScore: int.MinValue),
            (candidateWindow, _, localState) =>
            {
                var alignment = AlignReadToCandidateWindow(read, candidateWindow);
                if (alignment == null)
                {
                    return localState;
                }

                if (localState.Best == null || alignment.Score > localState.Best.Score)
                {
                    localState.RunnerUpScore = localState.Best?.Score ?? localState.RunnerUpScore;
                    localState.Best = alignment;
                }
                else if (alignment.Score > localState.RunnerUpScore)
                {
                    localState.RunnerUpScore = alignment.Score;
                }

                return localState;
            },
            localState =>
            {
                if (localState.Best == null)
                {
                    return;
                }

                lock (bestAlignmentLock)
                {
                    if (bestAlignment == null || localState.Best.Score > bestAlignment.Score)
                    {
                        runnerUpScore = Math.Max(runnerUpScore, bestAlignment?.Score ?? int.MinValue);
                        runnerUpScore = Math.Max(runnerUpScore, localState.RunnerUpScore);
                        bestAlignment = localState.Best;
                    }
                    else
                    {
                        runnerUpScore = Math.Max(runnerUpScore, localState.Best.Score);
                        runnerUpScore = Math.Max(runnerUpScore, localState.RunnerUpScore);
                    }
                }
            });

        return bestAlignment;
    }

    private AlignmentResult? AlignReadToCandidateWindow(
        Sequence read,
        ReferenceIndex.CandidateWindow candidateWindow)
    {
        var windowLength = candidateWindow.End - candidateWindow.Start;
        if (windowLength <= 0)
        {
            return null;
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
            return null;
        }

        return new AlignmentResult(
            localAlignment.AlignedReference,
            localAlignment.AlignedRead,
            localAlignment.VisualString,
            localAlignment.Score,
            localAlignment.ReferenceStartPosition + candidateWindow.Start,
            localAlignment.LeftSoftClip,
            localAlignment.RightSoftClip,
            localAlignment.WasPruned);
    }

    /// <summary>
    /// Groups local variants by position/allele and computes consensus depth/quality.
    /// </summary>
    public LocalVariantResult[] GetMergedVariants()
    {
        var merged = VariantCaller.MergeVariants(_allVariants);
        return ApplyReadAcceptanceFilters(merged);
    }

    /// <summary>
    /// Runs De Bruijn graph analysis for SV detection on reads aligned to a local region.
    /// Builds a graph from reads overlapping [windowStart, windowEnd) and detects bubbles/tips.
    /// </summary>
    public async Task<StructuralVariantAnalysis> RunGraphAnalysis(
        int windowStart,
        int windowEnd,
        CancellationToken cancellationToken = default)
    {
        // Filter reads to those overlapping the window
        var windowReads = new List<Sequence>();

        foreach (var record in _allAlignments)
        {
            if (record.Alignment == null || !record.IsMapped || record.Sequence == null)
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
    public async Task<StructuralVariantAnalysis> RunFullGraphAnalysis(
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
                var analysis = await RunGraphAnalysis(windowStart, windowEnd, ct).ConfigureAwait(false);
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
    public async Task WriteVcf(string filePath, long? chromLength = null)
    {
        await using var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write);
        VcfWriter.Write(fs, _allVariants, _chromosome, chromLength).Wait();
    }

    /// <summary>
    /// Writes alignments to a SAM file (for reference/debugging).
    /// </summary>
    public async Task WriteSam(string filePath)
    {
        if (_samDefinition == null)
        {
            return;
        }

        await using var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write);
        var writer = new SamWriter(NullLogger<SamWriter>.Instance);
        await writer.Write(_samDefinition, fs, CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>
    /// Queries alignments in a specific genomic region via BamReader.
    /// Returns empty when no BAM file path is available.
    /// </summary>
    public async Task<List<AlignmentSection>> QueryRegion(
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
            localResults = [];
            await foreach (var section in reader.QueryRegion(referenceName, start, end, cancellationToken)
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
        var counts = new Dictionary<string, int>
        {
            ["SNP"] = 0,
            ["Insertion"] = 0,
            ["Deletion"] = 0,
            ["Inversion"] = 0,
            ["Translocation"] = 0,
            ["SV"] = 0
        };

        foreach (var v in _allVariants)
        {
            if (v.IsStructuralVariant)
            {
                counts["SV"]++;
                var key = v.SvType?.ToString();
                if (key != null && counts.ContainsKey(key))
                {
                    counts[key]++;
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
