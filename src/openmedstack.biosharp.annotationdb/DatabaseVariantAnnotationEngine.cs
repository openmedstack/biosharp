using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OpenMedStack.BioSharp.Calculations;
using OpenMedStack.BioSharp.Io.Vcf;
using OpenMedStack.BioSharp.Model;
using OpenMedStack.BioSharp.Model.Vcf;

namespace OpenMedStack.BioSharp.AnnotationDb;

public sealed class DatabaseVariantAnnotationEngine
{
    private readonly VcfFileReader _vcfReader = new();

    public DatabaseVariantAnnotationEngine(TranscriptAnnotationDatabase database)
    {
        Database = database;
    }

    public TranscriptAnnotationDatabase Database { get; }

    public Task<VariantAnnotation[]> AnnotateVariant(
        VcfVariant variant,
        string? transcriptId = null,
        float minQuality = 5.0f,
        CancellationToken cancellationToken = default)
    {
        return AnnotateVariantCore(variant, transcriptId, minQuality, cancellationToken);
    }

    public async IAsyncEnumerable<VariantAnnotation> AnnotateVcf(
        string vcfPath,
        string? transcriptId = null,
        float minQuality = 5.0f,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var reader = await _vcfReader.Read(vcfPath, cancellationToken).ConfigureAwait(false);
        await foreach (var variant in reader.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            var annotations = await AnnotateVariantCore(
                variant,
                transcriptId,
                minQuality,
                cancellationToken).ConfigureAwait(false);

            foreach (var annotation in annotations)
            {
                yield return annotation;
            }
        }
    }

    private async Task<VariantAnnotation[]> AnnotateVariantCore(
        VcfVariant variant,
        string? transcriptId,
        float minQuality,
        CancellationToken cancellationToken)
    {
        if (FailsQualityThreshold(variant, minQuality))
        {
            return [];
        }

        var transcripts = await Database.FindTranscriptsForVariant(
            variant.Chromosome,
            variant.Position,
            transcriptId,
            cancellationToken).ConfigureAwait(false);

        if (transcripts.Count == 0)
        {
            return [];
        }

        var annotations = new List<VariantAnnotation>();
        foreach (var transcript in transcripts)
        {
            using var engine = new VariantAnnotationEngine(transcript.Context);
            engine.LoadTranscript(ToSequence(transcript));

            var transcriptAnnotations = engine.AnnotateVariant(variant);
            if (transcriptAnnotations != null)
            {
                annotations.AddRange(transcriptAnnotations);
            }
        }

        return annotations.ToArray();
    }

    private static bool FailsQualityThreshold(VcfVariant variant, float minQuality)
    {
        return variant.ErrorProbabilities is { Length: > 0 } && variant.ErrorProbabilities.Any(q => q < minQuality);
    }

    private static Sequence ToSequence(StoredTranscript transcript)
    {
        var quality = new string('I', transcript.Sequence.Length);
        return new Sequence(transcript.TranscriptId, transcript.Sequence.AsMemory(), quality.AsMemory());
    }
}
