namespace OpenMedStack.BioSharp.Calculations.Alignment;

using System;
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

        public override string ToString()
        {
            return $"PipelineMetrics{{reads={ReadsProcessed}, mapped={ReadsMapped}, " +
                $"aligned={ReadsRealigned}, called={VariantsCalled}, final={VariantsFinal}, sv={StructuralVariants}}}";
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
            return System.Text.Encoding.UTF8.GetString(mem.ToArray());
        }
    }

    private readonly Sequence _reference;
    private readonly string _chromosome;
    private readonly PipelineOptions _options;
    private readonly List<LocalVariantResult> _allVariants = new();
    private readonly List<ReadAlignmentRecord> _allAlignments = new();
    private readonly ReadRealigner _realigner;
    private SamDefinition? _samDefinition;
    private string? _bamFilePath;
    private readonly List<AlignmentSection> _regionAlignments = new();

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

        if (_samDefinition == null || _samDefinition.AlignmentSections.Length == 0) return false;

        foreach (var section in _samDefinition.AlignmentSections)
        {
            // Skip unmapped reads
            if ((section.Flag & AlignmentSection.AlignmentFlag.SegmentUnmapped) != 0) continue;

            var header = SequenceHeader.Empty;
            var readData = section.Sequence.TrimEnd().ToCharArray();
            var readQual = section.Quality.TrimEnd().ToCharArray();
            var readSeq = new Sequence(section.QName, readData, readQual);
            await ProcessReadAsync(readSeq, cancellationToken).ConfigureAwait(false);
        }

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
        var seqList = new List<Sequence>();

        await foreach (var seq in sequences.WithCancellation(cancellationToken).ConfigureAwait(false)) seqList.Add(seq);

        if (seqList.Count == 0) return false;

        foreach (var seq in seqList) await ProcessReadAsync(seq, cancellationToken).ConfigureAwait(false);

        return true;
    }

    /// <summary>
    /// Processes a single read: aligns against reference, calls variants,
    /// performs soft-clip realignment, and handles graph-based SV detection.
    /// </summary>
    public async Task<LocalVariantResult[]> ProcessReadAsync(
        Sequence read,
        CancellationToken cancellationToken = default)
    {
        var results = new List<LocalVariantResult>();

        // Step 1: Align read against reference using Smith-Waterman
        var alignment = SmithWatermanAligner.Align(
            _reference,
            read,
            _options.MatchScore,
            _options.MismatchPenalty,
            _options.GapOpenPenalty,
            _options.GapExtendPenalty,
            _options.MinAlignmentScore);

        if (alignment == null) return [];

        // Store the alignment record for BAM SAM output
        _allAlignments.Add(new ReadAlignmentRecord
        {
            ReadName = read.Id,
            Alignment = alignment,
            Sequence = read
        });

        // Step 2: Call variants from alignment (SNPs, indels)
        var variants = VariantCaller.CallVariants(_reference, alignment!, _options.MinVariantQuality);
        results.AddRange(variants);

        // Step 3: Soft-clip realignment for SV discovery
        if (_options.EnableSoftClipRealignment && (alignment!.LeftSoftClip > 0 || alignment!.RightSoftClip > 0))
            try
            {
                var readSeqStr = read.GetData()!.Span.ToString();
                var readQualityStr = read.GetQuality()!.Span.ToString();
                var realignResults = _realigner.Realign(alignment!, _reference, readSeqStr, readQualityStr);

                foreach (var realign in realignResults)
                    if (realign is { WasRealigned: true, Alignment: not null })
                    {
                        var reAlignVariants = VariantCaller.CallVariants(_reference, realign.Alignment!,
                            _options.MinVariantQuality);
                        results.AddRange(reAlignVariants);

                        if (realign.IsStructuralVariant)
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
            catch
            {
                // Soft-clip realignment failed; fall through to just return primary alignment variants
            }

        // Step 4: Store variant calls with chromosome assigned
        foreach (var v in results)
        {
            v.Chromosome = _chromosome;
            _allVariants.Add(v);
        }

        return results.ToArray();
    }

    /// <summary>
    /// Groups local variants by position/allele and computes consensus depth/quality.
    /// </summary>
    public LocalVariantResult[] GetMergedVariants()
    {
        return VariantCaller.MergeVariants(_allVariants.ToArray());
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
            if (record.Alignment == null || !record.IsMapped) continue;

            var readStart = record.Alignment!.ReferenceStartPosition;
            var readEnd = readStart + record.Alignment!.AlignedReference.Length;

            if (readEnd > windowStart && readStart < windowEnd) windowReads.Add(record.Sequence);
        }

        // Need minimum coverage for graph analysis
        if (windowReads.Count < _options.MinGraphCoverage) return new StructuralVariantAnalysis([]);

        // Slice reference window
        var windowLen = Math.Min(_reference.Length, windowEnd - windowStart);
        var windowRef = _reference.Slice(Math.Max(0, windowStart), windowLen);

        var kmerGraph = new DeBruijnGraph(_options.KmerSize, windowReads.ToAsyncEnumerable());

        var analysis = await StructuralVariantDetector.AnalyzeGraph(
            kmerGraph,
            new string(windowRef.GetData()!.Span),
            _chromosome,
            windowStart).ConfigureAwait(false);

        // Add any graph-discovered SVs to the variant list
        foreach (var v in analysis.Variants)
        {
            v.Chromosome = _chromosome;
            _allVariants.Add(v);
        }

        return analysis;
    }

    /// <summary>
    /// Runs graph analysis on the full reference (de novo SV discovery).
    /// </summary>
    public async Task<StructuralVariantAnalysis> RunFullGraphAnalysisAsync(
        CancellationToken cancellationToken = default)
    {
        var allReads = _allAlignments
            .Where(r => r.IsMapped)
            .Select(r => r.Sequence)
            .ToList();

        if (allReads.Count < _options.MinGraphCoverage) return new StructuralVariantAnalysis([]);

        var kmerGraph = new DeBruijnGraph(_options.KmerSize, allReads.ToAsyncEnumerable());

        var analysis = await StructuralVariantDetector.AnalyzeGraph(
            kmerGraph,
            new string(_reference.GetData()!.Span),
            _chromosome,
            0).ConfigureAwait(false);

        foreach (var v in analysis.Variants)
        {
            v.Chromosome = _chromosome;
            _allVariants.Add(v);
        }

        return analysis;
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
            StructuralVariants = svCount
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
        if (_samDefinition == null) return;

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
            var logger = new Microsoft.Extensions.Logging.Abstractions.NullLogger<BamReader>();
            var reader = new BamReader(bamFilePath, logger);
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
            if (v.IsStructuralVariant)
            {
                counts["SV"]++;
                if (v.SvType.HasValue)
                {
                    var key = ((SvType)v.SvType).ToString();
                    if (key != null && counts.ContainsKey(key)) counts[key]++;
                }
            }
            else
            {
                // Classify local variants
                if (string.IsNullOrEmpty(v.Reference) || string.IsNullOrEmpty(v.Alternate)) continue;

                if (v.Reference.Length > v.Alternate.Length)
                    counts["Deletion"]++;
                else if (v.Alternate.Length > v.Reference.Length)
                    counts["Insertion"]++;
                else
                    counts["SNP"]++;
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
