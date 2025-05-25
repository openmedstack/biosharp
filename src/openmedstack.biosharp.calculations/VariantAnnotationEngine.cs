using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using OpenMedStack.BioSharp.Model;
using OpenMedStack.BioSharp.Model.Vcf;
using OpenMedStack.BioSharp.Io.FastA;
using OpenMedStack.BioSharp.Io.Vcf;

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
/// </list>
/// 
/// Usage:
/// <code>
/// var ctx = AnnotationContext.FromCdsBoundaries(100, 1000, 1500);
/// var engine = new VariantAnnotationEngine(ctx);
/// await engine.LoadTranscriptsAsync("transcripts.fasta");
/// await foreach (var ann in engine.AnnotateVcfAsync("variants.vcf", "NM_001", 5.0f))
/// {
///     Console.WriteLine($"{ann.HgvsCoding} -> {ann.Consequence}");
/// }
/// </code>
/// </summary>
public class VariantAnnotationEngine : IDisposable
{
    private readonly Dictionary<string, Sequence> _transcripts = new();
    private readonly FastAReader _fastaReader = new();
    private readonly VcfFileReader _vcfReader;
    private bool _disposed;
    private readonly AnnotationContext? _annotationContext;

    /// <summary>
    /// Constructs a <see cref="VariantAnnotationEngine"/> with an optional annotation context.
    /// Passing a context enables non-coding consequence classification (Upstream, Downstream, SpliceSite, Intronic, etc.).
    /// </summary>
    /// <param name="annotationContext">Context defining CDS boundaries, gene regions, and introns. Null for coding-only annotation.</param>
    public VariantAnnotationEngine(AnnotationContext? annotationContext = null)
    {
        _vcfReader = new VcfFileReader(new VcfMetaReader());
        _annotationContext = annotationContext;
    }

    /// <summary>
    /// Loads transcript sequences from a FASTA file. Each sequence is stored by its FASTA header ID,
    /// making it available for variant annotation via <see cref="AnnotateVcfAsync"/> or <see cref="AnnotateVariantAsync"/>.
    /// </summary>
    /// <param name="fastaPath">Absolute or relative path to a FASTA file containing transcript sequences.</param>
    /// <param name="ct">Cancellation token to stop loading early.</param>
    /// <exception cref="FileNotFoundException">Thrown when the FASTA file does not exist at the given path.</exception>
    public async Task LoadTranscriptsAsync(string fastaPath, CancellationToken ct = default)
    {
        if (!File.Exists(fastaPath)) throw new FileNotFoundException("FASTA file not found.", fastaPath);

        _transcripts.Clear();
        await foreach (var sequence in _fastaReader.Read(fastaPath, ct)) _transcripts[sequence.Id] = sequence;
    }

    /// <summary>
    /// Annotates all variants in a VCF file, yielding <see cref="VariantAnnotation"/> results
    /// as they are produced via <see cref="IAsyncEnumerable{T}"/>.
    /// 
    /// Filtering is applied:
    /// <list type="bullet">
    ///   <item>Quality: Only variants with Phred error probabilities &gt;= minQuality are included.</item>
    ///   <item>Transcript: If transcriptId is provided, only that transcript is annotated. Otherwise, all loaded transcripts.</item>
    /// </list>
    /// </summary>
    /// <param name="vcfPath">Path to the VCF file to annotate.</param>
    /// <param name="transcriptId">Optional specific transcript ID to annotate against. Null annotates all.</param>
    /// <param name="minQuality">Minimum Phred-scaled quality threshold. Variants with error probabilities below this are skipped.</param>
    /// <param name="ct">Cancellation token for early termination.</param>
    /// <returns>An async enumerable of variant annotations.</returns>
    /// <exception cref="FileNotFoundException">Thrown when the VCF file does not exist.</exception>
    public async IAsyncEnumerable<VariantAnnotation> AnnotateVcfAsync(
        string vcfPath,
        string? transcriptId = null,
        float minQuality = 5.0f,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var reader = await _vcfReader.Read(vcfPath, ct);

        await foreach (var variant in reader)
        {
            // R1.2: Filter by minQuality using ErrorProbabilities (phred-scaled).
            var failsQuality = false;
            if (variant.ErrorProbabilities.Length > 0)
                if (variant.ErrorProbabilities.Any(q => q < minQuality))
                    failsQuality = true;

            if (failsQuality) continue;

            // R1.2: Filter by optional transcriptId.
            if (transcriptId != null && !_transcripts.ContainsKey(transcriptId)) continue;

            var targetTranscripts = transcriptId != null
                ? [_transcripts[transcriptId]]
                : _transcripts.Values.ToArray();

            foreach (var annotation in targetTranscripts.SelectMany(t =>
                AnnotateSingleVariantAgainstTranscript(variant, t)))
                yield return annotation;
        }
    }

    /// <summary>
    /// Annotates a single VCF variant against all loaded transcripts.
    /// Returns null if no annotations were produced (e.g., all variants were skipped or ref-mismatched without transcript match).
    /// </summary>
    /// <param name="variant">The VCF variant to annotate.</param>
    /// <returns>An array of <see cref="VariantAnnotation"/> for each transcript, or null if none.</returns>
    public VariantAnnotation[]? AnnotateVariantAsync(VcfVariant variant)
    {
        var results = _transcripts.Values
            .SelectMany(transcript => AnnotateSingleVariantAgainstTranscript(variant, transcript))
            .ToArray();

        return results.Length > 0 ? results : null;
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
            if (altAllele == "*" || altAllele == variant.Reference || altAllele.StartsWith("<")) continue;

            var refAllele = variant.Reference;
            var cPos = variant.Position;

            var codonChange = BuildCodonChange(variant, altAllele, transcript);

            // If codonChange is null, check if it's a ref-mismatch case.
            // A mismatch means VCF ref doesn't agree with transcript, so annotate as Uncertain.
            if (codonChange == null)
            {
                if (RefMismatch(transcript, variant, altAllele))
                {
                    yield return new VariantAnnotation
                    {
                        AffectedGene = transcript.Id,
                        Consequence = VariantConsequence.Uncertain,
                        HgvsCoding = "c." + variant.Position + "?",
                        HgvsProtein = "p.?",
                        AffectedAminoAcid = null,
                        ResultingAminoAcid = null,
                        CodonChange = variant.Reference + ">" + altAllele,
                        FrameshiftOffset = null
                    };
                    continue;
                }

                continue;
            }

            var ann = codonChange.Annotate(transcript.Id, transcript, cPos, refAllele[0],
                altAllele[0], _annotationContext);
            if (ann != null) yield return ann;
        }
    }

    /// <summary>
    /// Checks whether the VCF reference base at the variant position matches the
    /// corresponding base in the transcript. Returns true only when positions
    /// are within the transcript and the transcript base differs from REF.
    /// </summary>
    private static bool RefMismatch(Sequence transcript, VcfVariant variant, string altAllele)
    {
        var cPos = variant.Position;
        if (cPos < 1 || cPos > transcript.Length)
            return false; // Can't check ref if position is out of bounds

        var data = transcript.GetData();
        var refBase = char.ToUpper(variant.Reference[0]);
        var transcriptBase = char.ToUpper(data.Span[cPos - 1]);
        return transcriptBase != refBase;
    }

    /// <summary>
    /// Maps VCF allele type (substitution, deletion, insertion) to the corresponding
    /// <see cref="CodonChange"/> factory method. Returns null if the position falls
    /// beyond the transcript boundaries.
    /// </summary>
    private static CodonChange? BuildCodonChange(VcfVariant variant, string altAllele, Sequence transcript)
    {
        var refBase = variant.Reference;
        var cPos = variant.Position;

        if (altAllele.Length == refBase.Length) // Substitution
        {
            var startOfCodon = (cPos - 1) / 3 * 3 + 1;
            var endOfCodon = startOfCodon + 2;
            if (endOfCodon > transcript.Length) return null;

            var refCodon = GetSequenceSubstring(transcript, startOfCodon, endOfCodon);
            return VariantAnnotator.Substitution(refCodon, cPos, refBase[0], altAllele[0]);
        }

        if (altAllele.Length < refBase.Length) // Deletion
        {
            var startOfCodon = (cPos - 1) / 3 * 3 + 1;
            var endOfCodon = startOfCodon + 2;
            if (endOfCodon > transcript.Length) return null;

            var refCodon = GetSequenceSubstring(transcript, startOfCodon, endOfCodon);
            return VariantAnnotator.MultiDeletion(refCodon, cPos, refBase.Length);
        }
        else // Insertion
        {
            var startOfCodon = (cPos - 1) / 3 * 3 + 1;
            var endOfCodon = startOfCodon + 2;
            if (endOfCodon > transcript.Length) return null;

            var refCodon = GetSequenceSubstring(transcript, startOfCodon, endOfCodon);
            return VariantAnnotator.Insertion(refCodon, cPos, altAllele[0]);
        }
    }

    /// <summary>
    /// Extracts a substring from a <see cref="Sequence"/> by 1-based inclusive start/end.
    /// </summary>
    private static string GetSequenceSubstring(Sequence seq, int start, int end)
    {
        var data = seq.GetData();
        var chars = new char[end - start + 1];
        for (var i = 0; i < chars.Length; i++) chars[i] = data.Span[start + i - 1];
        return new string(chars);
    }

    /// <summary>
    /// Releases resources held by the engine. Does not close the underlying readers
    /// as they are typically shared across multiple engine instances.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;

        _disposed = true;
    }
}
