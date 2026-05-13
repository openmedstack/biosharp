using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using OpenMedStack.BioSharp.Io;
using OpenMedStack.BioSharp.Io.FastA;
using OpenMedStack.BioSharp.Model;

namespace OpenMedStack.BioSharp.AnnotationDb;

internal static class TranscriptImportParser
{
    public static async IAsyncEnumerable<StoredTranscript> ImportEnsemblStyle(
        TranscriptImportRequest request,
        string sourceName,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        _ = sourceName;

        var sequences = await ReadSequences(request.SequencePath, cancellationToken).ConfigureAwait(false);
        var transcripts = new Dictionary<string, TranscriptImportAccumulator>(StringComparer.Ordinal);

        await foreach (var record in GffReader.Read(request.AnnotationPath, cancellationToken).ConfigureAwait(false))
        {
            var transcriptId = GetAttribute(record, "transcript_id");
            if (string.IsNullOrWhiteSpace(transcriptId))
            {
                continue;
            }

            var accumulator = GetOrCreateAccumulator(transcripts, transcriptId);
            accumulator.Chromosome = record.Seqname;
            accumulator.Strand = record.Strand;

            switch (record.Feature.ToLowerInvariant())
            {
                case "transcript":
                    accumulator.GeneId = FirstNonEmpty(accumulator.GeneId, GetAttribute(record, "gene_id"));
                    accumulator.GeneName = FirstNonEmpty(accumulator.GeneName, GetAttribute(record, "gene_name"));
                    accumulator.GeneStart = record.Start;
                    accumulator.GeneEnd = record.End;
                    accumulator.IsCanonical |= IsEnsemblCanonical(record);
                    break;

                case "exon":
                    accumulator.Exons.Add((record.Start, record.End));
                    accumulator.GeneId = FirstNonEmpty(accumulator.GeneId, GetAttribute(record, "gene_id"));
                    accumulator.GeneName = FirstNonEmpty(accumulator.GeneName, GetAttribute(record, "gene_name"));
                    break;

                case "cds":
                    accumulator.CdsSegments.Add((record.Start, record.End));
                    accumulator.GeneId = FirstNonEmpty(accumulator.GeneId, GetAttribute(record, "gene_id"));
                    accumulator.GeneName = FirstNonEmpty(accumulator.GeneName, GetAttribute(record, "gene_name"));
                    break;
            }
        }

        foreach (var transcript in BuildStoredTranscripts(transcripts, sequences))
        {
            yield return transcript;
        }
    }

    public static async IAsyncEnumerable<StoredTranscript> ImportRefSeq(
        TranscriptImportRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var sequences = await ReadSequences(request.SequencePath, cancellationToken).ConfigureAwait(false);
        var transcripts = new Dictionary<string, TranscriptImportAccumulator>(StringComparer.Ordinal);
        var genes = new Dictionary<string, (string? GeneId, string? GeneName)>(StringComparer.Ordinal);

        await foreach (var record in GffReader.Read(request.AnnotationPath, cancellationToken).ConfigureAwait(false))
        {
            var feature = record.Feature.ToLowerInvariant();
            switch (feature)
            {
                case "gene":
                {
                    var geneKey = GetAttribute(record, "ID");
                    if (!string.IsNullOrWhiteSpace(geneKey))
                    {
                        genes[geneKey] = (
                            GeneId: ExtractGeneId(record),
                            GeneName: FirstNonEmpty(GetAttribute(record, "gene"), GetAttribute(record, "Name")));
                    }

                    break;
                }

                case "mrna":
                case "transcript":
                case "ncrna":
                case "rrna":
                case "trna":
                {
                    var transcriptId = NormalizeRefSeqIdentifier(GetAttribute(record, "ID"));
                    if (string.IsNullOrWhiteSpace(transcriptId))
                    {
                        continue;
                    }

                    var accumulator = GetOrCreateAccumulator(transcripts, transcriptId);
                    accumulator.Chromosome = record.Seqname;
                    accumulator.Strand = record.Strand;
                    accumulator.GeneStart = record.Start;
                    accumulator.GeneEnd = record.End;
                    accumulator.GeneId = FirstNonEmpty(accumulator.GeneId, ExtractGeneId(record));
                    accumulator.GeneName = FirstNonEmpty(
                        accumulator.GeneName,
                        GetAttribute(record, "gene"),
                        GetAttribute(record, "Name"));

                    var parentGene = GetAttribute(record, "Parent");
                    if (!string.IsNullOrWhiteSpace(parentGene) && genes.TryGetValue(parentGene, out var geneMetadata))
                    {
                        accumulator.GeneId = FirstNonEmpty(accumulator.GeneId, geneMetadata.GeneId);
                        accumulator.GeneName = FirstNonEmpty(accumulator.GeneName, geneMetadata.GeneName);
                    }

                    break;
                }

                case "exon":
                case "cds":
                {
                    var parentTranscriptId = NormalizeRefSeqIdentifier(GetAttribute(record, "Parent"));
                    if (string.IsNullOrWhiteSpace(parentTranscriptId))
                    {
                        continue;
                    }

                    var accumulator = GetOrCreateAccumulator(transcripts, parentTranscriptId);
                    accumulator.Chromosome = record.Seqname;
                    accumulator.Strand = record.Strand;

                    if (feature == "exon")
                    {
                        accumulator.Exons.Add((record.Start, record.End));
                    }
                    else
                    {
                        accumulator.CdsSegments.Add((record.Start, record.End));
                    }

                    break;
                }
            }
        }

        foreach (var transcript in BuildStoredTranscripts(transcripts, sequences))
        {
            yield return transcript;
        }
    }

    private static async Task<Dictionary<string, string>> ReadSequences(
        string sequencePath,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(sequencePath))
        {
            throw new FileNotFoundException("FASTA file not found.", sequencePath);
        }

        var reader = new FastAReader(NullLogger<FastAReader>.Instance);
        var sequences = new Dictionary<string, string>(StringComparer.Ordinal);
        await foreach (var sequence in reader.Read(sequencePath, cancellationToken).ConfigureAwait(false))
        {
            var key = NormalizeSequenceId(sequence.Id);
            sequences[key] = new string(sequence.GetData().Span);
        }

        return sequences;
    }

    private static IEnumerable<StoredTranscript> BuildStoredTranscripts(
        IReadOnlyDictionary<string, TranscriptImportAccumulator> accumulators,
        IReadOnlyDictionary<string, string> sequences)
    {
        foreach (var (transcriptId, accumulator) in accumulators.OrderBy(kvp => kvp.Key, StringComparer.Ordinal))
        {
            if (!sequences.TryGetValue(transcriptId, out var sequence))
            {
                continue;
            }

            var orderedExons = accumulator.Exons
                .OrderBy(exon => exon.Start)
                .ToList();

            var orderedIntrons = new List<(int Start, int End)>();
            for (var index = 0; index < orderedExons.Count - 1; index++)
            {
                var intronStart = orderedExons[index].End + 1;
                var intronEnd = orderedExons[index + 1].Start - 1;
                if (intronStart <= intronEnd)
                {
                    orderedIntrons.Add((intronStart, intronEnd));
                }
            }

            var cdsStart = accumulator.CdsSegments.Count > 0
                ? accumulator.CdsSegments.Min(segment => segment.Start)
                : accumulator.GeneStart;
            var cdsEnd = accumulator.CdsSegments.Count > 0
                ? accumulator.CdsSegments.Max(segment => segment.End)
                : accumulator.GeneEnd;

            if (cdsStart <= 0 || cdsEnd <= 0)
            {
                continue;
            }

            var transcriptLength = sequence.Length;
            var context = new AnnotationContext
            {
                CdsStart = cdsStart,
                CdsEnd = cdsEnd,
                TranscriptLength = transcriptLength,
                GeneBoundaries = (accumulator.GeneStart > 0 ? accumulator.GeneStart : cdsStart,
                    accumulator.GeneEnd > 0 ? accumulator.GeneEnd : cdsEnd),
                ExonBoundaries = orderedExons,
                Introns = orderedIntrons
            };

            yield return new StoredTranscript(
                TranscriptId: transcriptId,
                GeneId: EmptyToNull(accumulator.GeneId),
                GeneName: EmptyToNull(accumulator.GeneName),
                Chromosome: string.IsNullOrWhiteSpace(accumulator.Chromosome) ? string.Empty : accumulator.Chromosome,
                Strand: accumulator.Strand,
                IsCanonical: accumulator.IsCanonical,
                Sequence: sequence,
                Context: context,
                Exons: orderedExons,
                Introns: orderedIntrons);
        }
    }

    private static TranscriptImportAccumulator GetOrCreateAccumulator(
        IDictionary<string, TranscriptImportAccumulator> transcripts,
        string transcriptId)
    {
        if (transcripts.TryGetValue(transcriptId, out var accumulator))
        {
            return accumulator;
        }

        accumulator = new TranscriptImportAccumulator();
        transcripts[transcriptId] = accumulator;
        return accumulator;
    }

    private static string NormalizeSequenceId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var trimmed = value.Trim();
        var firstToken = trimmed.Split(' ', '\t', StringSplitOptions.RemoveEmptyEntries)[0];
        return NormalizeRefSeqIdentifier(firstToken);
    }

    private static string NormalizeRefSeqIdentifier(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var trimmed = value.Trim();
        return trimmed.StartsWith("rna-", StringComparison.OrdinalIgnoreCase)
            ? trimmed[4..]
            : trimmed;
    }

    private static string? ExtractGeneId(GffRecord record)
    {
        if (record.Attributes.TryGetValue("Dbxref", out var dbxref))
        {
            foreach (var entry in dbxref.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (entry.StartsWith("GeneID:", StringComparison.OrdinalIgnoreCase))
                {
                    return entry["GeneID:".Length..];
                }
            }
        }

        var parent = GetAttribute(record, "Parent");
        if (!string.IsNullOrWhiteSpace(parent) && parent.StartsWith("gene-", StringComparison.OrdinalIgnoreCase))
        {
            return parent[5..];
        }

        var id = GetAttribute(record, "ID");
        if (!string.IsNullOrWhiteSpace(id) && id.StartsWith("gene-", StringComparison.OrdinalIgnoreCase))
        {
            return id[5..];
        }

        return EmptyToNull(GetAttribute(record, "gene_id"));
    }

    private static string GetAttribute(GffRecord record, string key)
    {
        return record.Attributes.TryGetValue(key, out var value) ? value : string.Empty;
    }

    private static bool IsEnsemblCanonical(GffRecord record)
    {
        return record.Attributes.TryGetValue("tag", out var tag)
               && tag.Contains("Ensembl_canonical", StringComparison.OrdinalIgnoreCase);
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }

    private static string? EmptyToNull(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private sealed class TranscriptImportAccumulator
    {
        public string? GeneId { get; set; }

        public string? GeneName { get; set; }

        public string Chromosome { get; set; } = string.Empty;

        public char Strand { get; set; } = '.';

        public int GeneStart { get; set; }

        public int GeneEnd { get; set; }

        public bool IsCanonical { get; set; }

        public List<(int Start, int End)> Exons { get; } = [];

        public List<(int Start, int End)> CdsSegments { get; } = [];
    }
}
