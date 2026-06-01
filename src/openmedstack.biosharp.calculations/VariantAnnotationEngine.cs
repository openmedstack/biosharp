using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using OpenMedStack.BioSharp.Model;
using OpenMedStack.BioSharp.Model.Vcf;
using OpenMedStack.BioSharp.Io.FastA;
using OpenMedStack.BioSharp.Io.Vcf;
using OpenMedStack.BioSharp.Io.Gff;

namespace OpenMedStack.BioSharp.Calculations;

/// <summary>
/// High-level API to annotate VCF file variants against transcript sequences.
///
/// Features:
/// <list type="bullet">
///   <item>Annotates VCF files with consequence types (Missense, Nonsense, Frameshift, etc.)</item>
///   <item>Filters variants by quality score (Phred-scaled) and transcript ID</item>
///   <p>Supports ref-allele mismatch detection, classifying mismatches as <see cref="VariantConsequence.Uncertain"/></item>
///   <item>Generates HGVS coding and protein annotations (c.XY&gt;Z, p.Xz)</item>
///   <item>Calculates FrameshiftOffset when applicable (amino acids between mutation and new stop)</item>
///   <item>Loads per-transcript coordinate structure from GTF/GFF3 files (ANN-1)</item>
///   <item>Predicts splice site disruption and cryptic site activation via PWM (ANN-4)</item>
/// </list>
///
/// Usage:
/// <code>
/// var ctx = AnnotationContext.FromCdsBoundaries(100, 1000, 1500);
/// var engine = new VariantAnnotationEngine(ctx);
/// await engine.LoadTranscriptsAsync("transcripts.fasta");
/// await foreach (var ann in engine.AnnotateVcf("variants.vcf", "NM_001", 5.0f))
/// {
///     Console.WriteLine($"{ann.HgvsCoding} -> {ann.Consequence}");
/// }
/// </code>
/// </summary>
public class VariantAnnotationEngine : IDisposable
{
    private readonly Dictionary<string, Sequence> _transcripts = new();
    private readonly Dictionary<string, AnnotationContext> _transcriptContexts = new();
    private readonly FastAReader _fastaReader = new(NullLogger<FastAReader>.Instance);
    private readonly VcfFileReader _vcfReader;
    private bool _disposed;
    private readonly AnnotationContext? _annotationContext;

    // ANN-4 splice site prediction thresholds
    private readonly double _spliceSiteThreshold;
    private readonly double _crypticSpliceThreshold;

    /// <summary>
    /// The canonical transcript ID identified during <see cref="LoadTranscriptsFromGtf"/>.
    /// Null if no canonical transcript was found or GTF has not been loaded.
    /// </summary>
    public string? CanonicalTranscriptId { get; private set; }

    /// <summary>
    /// Per-transcript <see cref="AnnotationContext"/> objects populated by
    /// <see cref="LoadTranscriptsFromGtf"/>. Read-only view.
    /// </summary>
    public IReadOnlyDictionary<string, AnnotationContext> TranscriptContexts
    {
        get { return _transcriptContexts; }
    }

    /// <summary>
    /// Constructs a <see cref="VariantAnnotationEngine"/> with an optional annotation context.
    /// Passing a context enables non-coding consequence classification (Upstream, Downstream, SpliceSite, Intronic, etc.).
    /// </summary>
    /// <param name="annotationContext">Context defining CDS boundaries, gene regions, and introns. Null for coding-only annotation.</param>
    /// <param name="spliceSiteThreshold">PWM score delta (bits) required to call a splice site disruption. Default 3.0.</param>
    /// <param name="crypticSpliceThreshold">Minimum alt PWM score (bits) required to call cryptic splice activation. Default 5.0.</param>
    public VariantAnnotationEngine(
        AnnotationContext? annotationContext = null,
        double spliceSiteThreshold = 3.0,
        double crypticSpliceThreshold = 5.0)
    {
        _vcfReader = new VcfFileReader();
        _annotationContext = annotationContext;
        _spliceSiteThreshold = spliceSiteThreshold;
        _crypticSpliceThreshold = crypticSpliceThreshold;
    }

    /// <summary>
    /// Loads transcript sequences from a FASTA file. Each sequence is stored by its FASTA header ID,
    /// making it available for variant annotation via <see cref="AnnotateVcf"/> or <see cref="AnnotateVariant"/>.
    /// </summary>
    /// <param name="fastaPath">Absolute or relative path to a FASTA file containing transcript sequences.</param>
    /// <param name="ct">Cancellation token to stop loading early.</param>
    /// <exception cref="FileNotFoundException">Thrown when the FASTA file does not exist at the given path.</exception>
    public async Task LoadTranscripts(string fastaPath, CancellationToken ct = default)
    {
        if (!File.Exists(fastaPath))
        {
            throw new FileNotFoundException("FASTA file not found.", fastaPath);
        }

        _transcripts.Clear();
        await foreach (var sequence in _fastaReader.Read(fastaPath, ct))
        {
            _transcripts[sequence.Id] = sequence;
        }
    }

    /// <summary>
    /// Adds a single pre-built <see cref="Sequence"/> directly to the transcript store.
    /// Useful for unit tests and programmatic loading without a FASTA file on disk.
    /// </summary>
    public void LoadTranscript(Sequence sequence) => _transcripts[sequence.Id] = sequence;

    /// <summary>
    /// Loads per-transcript <see cref="AnnotationContext"/> objects from a GTF or GFF3 file.
    ///
    /// For each transcript_id found in the file the method:
    /// <list type="bullet">
    ///   <item>Collects all CDS features to determine CDS start and end boundaries.</item>
    ///   <item>Collects all exon features and derives intron positions as the gaps between consecutive exons.</item>
    ///   <item>Captures gene/transcript boundaries from the enclosing feature.</item>
    ///   <item>Records the canonical transcript (tagged with "Ensembl_canonical" in GTF).</item>
    /// </list>
    ///
    /// Loaded contexts are stored in <see cref="TranscriptContexts"/> and used automatically
    /// by <see cref="AnnotateVariant"/> and <see cref="AnnotateVariantFromContexts"/>.
    /// </summary>
    /// <param name="gtfPath">Path to a GFF3 or GTF annotation file.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task LoadTranscriptsFromGtf(string gtfPath, CancellationToken ct = default)
    {
        if (!File.Exists(gtfPath))
        {
            throw new FileNotFoundException("GTF/GFF file not found.", gtfPath);
        }

        _transcriptContexts.Clear();
        CanonicalTranscriptId = null;

        var txCds = new Dictionary<string, List<(int Start, int End)>>();
        var txExons = new Dictionary<string, List<(int Start, int End)>>();
        var txBounds = new Dictionary<string, (int Start, int End)>();
        var txCanonical = new HashSet<string>();

        await foreach (var record in GffReader.Read(gtfPath, ct).ConfigureAwait(false))
        {
            var feature = record.Feature.ToLowerInvariant();
            var txId = GetAttribute(record, "transcript_id");
            if (string.IsNullOrEmpty(txId))
            {
                continue;
            }

            switch (feature)
            {
                case "transcript":
                    txBounds[txId] = (record.Start, record.End);
                    if (IsEnsemblCanonical(record))
                    {
                        txCanonical.Add(txId);
                    }

                    break;

                case "exon":
                    if (!txExons.TryGetValue(txId, out var exons))
                    {
                        txExons[txId] = exons = [];
                    }

                    exons.Add((record.Start, record.End));
                    break;

                case "cds":
                    if (!txCds.TryGetValue(txId, out var cdsList))
                    {
                        txCds[txId] = cdsList = [];
                    }

                    cdsList.Add((record.Start, record.End));
                    break;
            }
        }

        // Build an AnnotationContext per transcript
        var allTxIds = new HashSet<string>(txCds.Keys);
        allTxIds.UnionWith(txExons.Keys);
        allTxIds.UnionWith(txBounds.Keys);

        foreach (var txId in allTxIds)
        {
            txCds.TryGetValue(txId, out var cdsList2);
            txExons.TryGetValue(txId, out var exonList);
            txBounds.TryGetValue(txId, out var bounds);

            var cdsStart = cdsList2?.Count > 0 ? cdsList2.Min(c => c.Start) : bounds.Start;
            var cdsEnd = cdsList2?.Count > 0 ? cdsList2.Max(c => c.End) : bounds.End;
            var geneStart = bounds == default ? cdsStart : bounds.Start;
            var geneEnd = bounds == default ? cdsEnd : bounds.End;

            List<(int, int)>? introns = null;
            List<(int, int)>? exonBoundaries = null;

            if (exonList is { Count: > 0 })
            {
                var sorted = exonList.OrderBy(e => e.Start).ToList();
                exonBoundaries = sorted;
                if (sorted.Count > 1)
                {
                    introns = new List<(int, int)>(sorted.Count - 1);
                    for (var i = 0; i < sorted.Count - 1; i++)
                    {
                        introns.Add((sorted[i].End + 1, sorted[i + 1].Start - 1));
                    }
                }
            }

            _transcriptContexts[txId] = new AnnotationContext
            {
                CdsStart = cdsStart,
                CdsEnd = cdsEnd,
                TranscriptLength = geneEnd,
                GeneBoundaries = (geneStart, geneEnd),
                Introns = introns,
                ExonBoundaries = exonBoundaries
            };
        }

        CanonicalTranscriptId = txCanonical.FirstOrDefault();
    }

    /// <summary>
    /// Annotates all variants in a VCF file, yielding <see cref="VariantAnnotation"/> results
    /// as they are produced via <see cref="IAsyncEnumerable{T}"/>.
    /// </summary>
    public async IAsyncEnumerable<VariantAnnotation> AnnotateVcf(
        string vcfPath,
        string? transcriptId = null,
        float minQuality = 5.0f,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var reader = await _vcfReader.Read(vcfPath, ct);
        var targetTranscripts = GetTargetTranscripts(transcriptId);

        await foreach (var variant in reader.WithCancellation(ct).ConfigureAwait(false))
        {
            if (ShouldSkipVariant(variant, targetTranscripts, minQuality))
            {
                continue;
            }

            foreach (var annotation in targetTranscripts.SelectMany(t =>
                AnnotateSingleVariantAgainstTranscript(variant, t)))
            {
                yield return annotation;
            }
        }
    }

    /// <summary>
    /// Annotates a VCF file in bounded batches. Each batch can be processed in parallel while keeping
    /// total in-flight variants capped by <paramref name="batchSize"/>.
    /// </summary>
    public async IAsyncEnumerable<VariantAnnotation[]> AnnotateVcfInBatches(
        string vcfPath,
        string? transcriptId = null,
        float minQuality = 5.0f,
        int batchSize = 256,
        int degreeOfParallelism = 1,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (batchSize < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(batchSize));
        }

        if (degreeOfParallelism < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(degreeOfParallelism));
        }

        var reader = await _vcfReader.Read(vcfPath, ct).ConfigureAwait(false);
        var targetTranscripts = GetTargetTranscripts(transcriptId);
        var batch = new List<VcfVariant>(batchSize);

        await foreach (var variant in reader.WithCancellation(ct).ConfigureAwait(false))
        {
            if (ShouldSkipVariant(variant, targetTranscripts, minQuality))
            {
                continue;
            }

            batch.Add(variant);
            if (batch.Count < batchSize)
            {
                continue;
            }

            yield return AnnotateVariantBatch(batch, targetTranscripts, degreeOfParallelism);
            batch.Clear();
        }

        if (batch.Count > 0)
        {
            yield return AnnotateVariantBatch(batch, targetTranscripts, degreeOfParallelism);
        }
    }

    /// <summary>
    /// Annotates a single VCF variant against all loaded transcripts.
    /// Returns null if no annotations were produced (e.g., all variants were skipped or ref-mismatched without transcript match).
    /// </summary>
    /// <param name="variant">The VCF variant to annotate.</param>
    /// <returns>An array of <see cref="VariantAnnotation"/> for each transcript, or null if none.</returns>
    public VariantAnnotation[]? AnnotateVariant(VcfVariant variant)
    {
        var results = _transcripts.Values
            .SelectMany(transcript => AnnotateSingleVariantAgainstTranscript(variant, transcript))
            .ToArray();

        return results.Length > 0 ? results : null;
    }

    /// <summary>
    /// Annotates a single VCF variant using only the GTF-loaded <see cref="TranscriptContexts"/>,
    /// without requiring loaded FASTA sequences. Only non-coding consequences (SpliceSite,
    /// Intronic, Upstream, Downstream, Intergenic, etc.) are returned.
    /// Splice site variants are upgraded via the PWM predictor when transcript sequence is available.
    /// </summary>
    public IEnumerable<VariantAnnotation> AnnotateVariantFromContexts(VcfVariant variant)
    {
        foreach (var (txId, ctx) in _transcriptContexts)
        {
            var consequence = ctx.ClassifyPosition(variant.Position);
            if (!consequence.HasValue)
            {
                continue; // coding region — skip for context-only path
            }

            var finalConsequence = consequence.Value;

            if (finalConsequence == VariantConsequence.SpliceSite && _transcripts.TryGetValue(txId, out var seq))
            {
                finalConsequence = UpgradeSpliceSiteConsequence(seq, variant.Position,
                    variant.Reference.Length > 0 ? variant.Reference[0] : ' ',
                    variant.Alternate.Length > 0 ? variant.Alternate[0] : ' ',
                    ctx);
            }
            else if (finalConsequence == VariantConsequence.Intronic && _transcripts.TryGetValue(txId, out seq))
            {
                finalConsequence = CheckCrypticSplice(seq, variant.Position,
                    variant.Alternate.Length > 0 ? variant.Alternate[0] : ' ');
            }

            yield return new VariantAnnotation
            {
                AffectedGene = txId,
                Consequence = finalConsequence,
                HgvsCoding = BuildNonCodingHgvs(variant.Position, finalConsequence),
                HgvsNotation = $"{txId}:{BuildNonCodingHgvs(variant.Position, finalConsequence)}",
                HgvsProtein = "p.?",
                AffectedAminoAcid = null,
                ResultingAminoAcid = null,
                CodonChange = null,
                FrameshiftOffset = null
            };
        }
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private AnnotationContext? GetContextForTranscript(string transcriptId)
    {
        return _transcriptContexts.TryGetValue(transcriptId, out var ctx) ? ctx : _annotationContext;
    }

    private Sequence[] GetTargetTranscripts(string? transcriptId)
    {
        if (transcriptId == null)
        {
            return _transcripts.Values.ToArray();
        }

        return _transcripts.TryGetValue(transcriptId, out var transcript) ? [transcript] : [];
    }

    private static bool FailsQualityThreshold(VcfVariant variant, float minQuality)
    {
        return variant.ErrorProbabilities.Length > 0 && variant.ErrorProbabilities.Any(q => q < minQuality);
    }

    private static bool ShouldSkipVariant(VcfVariant variant, Sequence[] targetTranscripts, float minQuality)
    {
        return targetTranscripts.Length == 0 || FailsQualityThreshold(variant, minQuality);
    }

    private VariantAnnotation[] AnnotateVariantBatch(
        IReadOnlyList<VcfVariant> variants,
        Sequence[] targetTranscripts,
        int degreeOfParallelism)
    {
        if (variants.Count == 0 || targetTranscripts.Length == 0)
        {
            return [];
        }

        if (degreeOfParallelism == 1 || variants.Count == 1)
        {
            return variants
                .SelectMany(variant => targetTranscripts.SelectMany(t =>
                    AnnotateSingleVariantAgainstTranscript(variant, t)))
                .ToArray();
        }

        var annotationsByVariant = new VariantAnnotation[variants.Count][];
        Parallel.For(
            0,
            variants.Count,
            new ParallelOptions { MaxDegreeOfParallelism = degreeOfParallelism },
            index =>
            {
                annotationsByVariant[index] = targetTranscripts
                    .SelectMany(transcript => AnnotateSingleVariantAgainstTranscript(variants[index], transcript))
                    .ToArray();
            });

        return annotationsByVariant.SelectMany(annotations => annotations).ToArray();
    }

    /// <summary>
    /// Annotates a single VCF variant against a specific transcript.
    /// Handles multi-allele variants by producing one annotation per alternate allele.
    /// Skips ALT alleles that are "*", equal to REF, or symbolic (starting with "<").
    /// </summary>
    private IEnumerable<VariantAnnotation> AnnotateSingleVariantAgainstTranscript(
        VcfVariant variant,
        Sequence transcript)
    {
        var altAlleles = variant.Alternate.Split(',', StringSplitOptions.RemoveEmptyEntries);
        foreach (var altAllele in altAlleles)
        {
            if (altAllele == "*" || altAllele == variant.Reference || altAllele.StartsWith('<'))
            {
                continue;
            }

            var refAllele = variant.Reference;
            var cPos = variant.Position;
            var context = GetContextForTranscript(transcript.Id);

            // Check for non-coding context classification first
            var nonCoding = context?.ClassifyPosition(cPos);
            if (nonCoding.HasValue)
            {
                var consequence = nonCoding.Value;

                // Upgrade SpliceSite using PWM (ANN-4)
                if (consequence == VariantConsequence.SpliceSite)
                {
                    consequence = UpgradeSpliceSiteConsequence(transcript, cPos,
                        refAllele.Length > 0 ? refAllele[0] : ' ',
                        altAllele.Length > 0 ? altAllele[0] : ' ',
                        context!);
                }
                else if (consequence == VariantConsequence.Intronic)
                {
                    consequence = CheckCrypticSplice(transcript, cPos,
                        altAllele.Length > 0 ? altAllele[0] : ' ');
                }

                yield return new VariantAnnotation
                {
                    AffectedGene = transcript.Id,
                    Consequence = consequence,
                    HgvsCoding = BuildNonCodingHgvs(cPos, consequence),
                    HgvsNotation = $"{transcript.Id}:{BuildNonCodingHgvs(cPos, consequence)}",
                    HgvsProtein = "p.?",
                    AffectedAminoAcid = null,
                    ResultingAminoAcid = null,
                    CodonChange = $"{refAllele}>{altAllele}",
                    FrameshiftOffset = null
                };
                continue;
            }

            var codonChange = BuildCodonChange(variant, altAllele, transcript);

            if (codonChange == null)
            {
                if (RefMismatch(transcript, variant))
                {
                    yield return new VariantAnnotation
                    {
                        AffectedGene = transcript.Id,
                        Consequence = VariantConsequence.Uncertain,
                        HgvsCoding = $"c.{variant.Position}?",
                        HgvsNotation = $"{transcript.Id}:{BuildNonCodingHgvs(cPos, VariantConsequence.Uncertain)}",
                        HgvsProtein = "p.?",
                        AffectedAminoAcid = null,
                        ResultingAminoAcid = null,
                        CodonChange = $"{variant.Reference}>{altAllele}",
                        FrameshiftOffset = null
                    };
                }

                continue;
            }

            var ann = codonChange.Annotate(transcript.Id, transcript, cPos, refAllele[0],
                altAllele[0], context);
            if (ann != null)
            {
                yield return ann;
            }
        }
    }

    // ── Splice site PWM integration ───────────────────────────────────────────

    private VariantConsequence UpgradeSpliceSiteConsequence(
        Sequence transcript,
        int variantPos,
        char refBase,
        char altBase,
        AnnotationContext context)
    {
        var data = transcript.GetData();
        var donorBoundary = FindNearestDonorBoundary(variantPos, context);
        if (donorBoundary.HasValue)
        {
            return SpliceSitePredictor.UpgradeDonorConsequence(
                data.Span, variantPos, altBase,
                donorBoundary.Value, _spliceSiteThreshold, VariantConsequence.SpliceSite);
        }

        return VariantConsequence.SpliceSite;
    }

    private VariantConsequence CheckCrypticSplice(Sequence transcript, int variantPos, char altBase)
    {
        var data = transcript.GetData();
        return SpliceSitePredictor.CheckCrypticDonor(data.Span, variantPos, altBase, _crypticSpliceThreshold);
    }

    private static int? FindNearestDonorBoundary(int variantPos, AnnotationContext context)
    {
        const int spliceWindow = 6; // broad window to capture +1..+6

        if (context.ExonBoundaries != null)
        {
            foreach (var (_, exonEnd) in context.ExonBoundaries)
            {
                if (variantPos >= exonEnd - 3 && variantPos <= exonEnd + spliceWindow)
                {
                    return exonEnd;
                }
            }
        }

        if (variantPos >= context.CdsStart - 3 && variantPos < context.CdsStart)
        {
            return context.CdsStart - 1;
        }

        if (variantPos > context.CdsEnd && variantPos <= context.CdsEnd + spliceWindow)
        {
            return context.CdsEnd;
        }

        return null;
    }

    // ── Coding annotation helpers ─────────────────────────────────────────────

    /// <summary>
    /// Checks whether the VCF reference base at the variant position matches the
    /// corresponding base in the transcript.
    /// </summary>
    private static bool RefMismatch(Sequence transcript, VcfVariant variant)
    {
        var cPos = variant.Position;
        if (cPos < 1 || cPos > transcript.Length)
        {
            return false;
        }

        var data = transcript.GetData();
        var refBase = char.ToUpper(variant.Reference[0]);
        var transcriptBase = char.ToUpper(data.Span[cPos - 1]);
        return transcriptBase != refBase;
    }

    private static CodonChange? BuildCodonChange(VcfVariant variant, string altAllele, Sequence transcript)
    {
        var refBase = variant.Reference;
        var cPos = variant.Position;

        if (altAllele.Length == refBase.Length)
        {
            var startOfCodon = (cPos - 1) / 3 * 3 + 1;
            var endOfCodon = startOfCodon + 2;
            if (endOfCodon > transcript.Length)
            {
                return null;
            }

            var refCodon = GetSequenceSubstring(transcript, startOfCodon, endOfCodon);
            return VariantAnnotator.Substitution(refCodon, cPos, refBase[0], altAllele[0]);
        }

        if (altAllele.Length < refBase.Length)
        {
            var startOfCodon = (cPos - 1) / 3 * 3 + 1;
            var endOfCodon = startOfCodon + 2;
            if (endOfCodon > transcript.Length)
            {
                return null;
            }

            var refCodon = GetSequenceSubstring(transcript, startOfCodon, endOfCodon);
            return VariantAnnotator.MultiDeletion(refCodon, cPos, refBase.Length);
        }
        else
        {
            var startOfCodon = (cPos - 1) / 3 * 3 + 1;
            var endOfCodon = startOfCodon + 2;
            if (endOfCodon > transcript.Length)
            {
                return null;
            }

            var refCodon = GetSequenceSubstring(transcript, startOfCodon, endOfCodon);
            return VariantAnnotator.Insertion(refCodon, cPos, altAllele[0]);
        }
    }

    private static string GetSequenceSubstring(Sequence seq, int start, int end)
    {
        var data = seq.GetData();
        var chars = new char[end - start + 1];
        for (var i = 0; i < chars.Length; i++)
        {
            chars[i] = data.Span[start + i - 1];
        }

        return new string(chars);
    }

    private static string BuildNonCodingHgvs(int pos, VariantConsequence consequence) =>
        consequence switch
        {
            // Upstream variants: negative position offset from coding start (c.-N)
            VariantConsequence.Upstream => $"c.-{pos}?",
            // Downstream variants: position after stop codon (c.*N)
            VariantConsequence.Downstream => $"c.*{pos}?",
            // Intergenic non-coding (n. prefix)
            VariantConsequence.Intergenic => $"n.{pos}?",
            // All other non-coding consequences (intronic, splice-site, etc.) use
            // the standard HGVS uncertain-position notation: c.N?
            _ => $"c.{pos}?"
        };

    // ── GTF parsing helpers ───────────────────────────────────────────────────

    private static string GetAttribute(GffRecord record, string key)
    {
        record.Attributes.TryGetValue(key, out var val);
        return val ?? string.Empty;
    }

    private static bool IsEnsemblCanonical(GffRecord record)
    {
        if (record.Attributes.TryGetValue("tag", out var tag))
        {
            return tag.Contains("Ensembl_canonical", StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    /// <summary>
    /// Releases resources held by the engine.
    /// </summary>
    public void Dispose()
    {
        GC.SuppressFinalize(this);
        if (_disposed)
        {
            return;
        }

        _disposed = true;
    }
}
