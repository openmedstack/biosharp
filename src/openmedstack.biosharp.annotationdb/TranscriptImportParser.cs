using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using OpenMedStack.BioSharp.Io;
using OpenMedStack.BioSharp.Io.FastA;
using OpenMedStack.BioSharp.Io.Gff;
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
            // Use transcript_id (the per-transcript unique key), NOT gene_id (which is shared
            // across all transcripts of the same gene and does not match FASTA sequence IDs).
            // Strip the Ensembl version suffix so keys match the (also version-stripped) FASTA
            // dictionary regardless of whether the source is Ensembl (unversioned GTF) or
            // GENCODE (versioned GTF, e.g. ENST00000832824.1).
            var rawTranscriptId = GetAttribute(record, "transcript_id");
            if (string.IsNullOrWhiteSpace(rawTranscriptId))
            {
                continue;
            }

            var transcriptId = StripEnsemblVersion(rawTranscriptId.Trim());
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

        // Parse the entire GFF3 into accumulators first (same logic as before).
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

        // Determine whether the FASTA contains chromosome sequences (NC_*) or transcript sequences (NM_*).
        var useGenomicExtraction = await IsGenomicFasta(request.SequencePath, cancellationToken).ConfigureAwait(false);

        if (useGenomicExtraction)
        {
            // Stream the chromosomal FASTA one chromosome at a time and extract exon sequences.
            await foreach (var transcript in BuildFromGenomicFasta(transcripts, request.SequencePath, cancellationToken)
                               .ConfigureAwait(false))
            {
                yield return transcript;
            }
        }
        else
        {
            // Original path: transcript FASTA keyed by NM_*/NR_* accessions.
            var sequences = await ReadSequences(request.SequencePath, cancellationToken).ConfigureAwait(false);
            foreach (var transcript in BuildStoredTranscripts(transcripts, sequences))
            {
                yield return transcript;
            }
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

        // Support both plain-text and gzip-compressed FASTA/FNA files.
        var reads = sequencePath.EndsWith(".gz", StringComparison.OrdinalIgnoreCase)
            ? reader.ReadGz(sequencePath, cancellationToken: cancellationToken)
            : reader.Read(sequencePath, cancellationToken);

        await foreach (var sequence in reads.ConfigureAwait(false))
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

        // Take the first whitespace-separated token (handles "ENST…  description text  …").
        var firstSpaceToken = trimmed.Split(' ', '\t', StringSplitOptions.RemoveEmptyEntries)[0];

        // GENCODE FASTA headers use pipe-separated fields with no spaces:
        //   ENST00000641515.2|ENSG00000272710.1|OTTHUMG…|…
        // Take only the part before the first "|" so the key matches the GTF transcript_id.
        var firstToken = firstSpaceToken.Split('|', StringSplitOptions.RemoveEmptyEntries)[0];

        var normalized = NormalizeRefSeqIdentifier(firstToken);

        // Strip Ensembl-/GENCODE-style version suffix (.N) from ENS* identifiers so that
        // FASTA keys (always versioned, e.g. ENST00000622028.1) match the GTF transcript_id
        // which may be unversioned (Ensembl) or versioned (GENCODE – stripped on both sides).
        return StripEnsemblVersion(normalized);
    }

    /// <summary>
    /// Removes the trailing version suffix (.<digits>) from Ensembl/GENCODE identifiers
    /// that begin with "ENS" (e.g. ENST00000622028.1 → ENST00000622028).
    /// Non-Ensembl IDs are returned unchanged.
    /// </summary>
    private static string StripEnsemblVersion(string id)
    {
        if (id.Length > 3
            && id.StartsWith("ENS", StringComparison.OrdinalIgnoreCase))
        {
            var dotIdx = id.LastIndexOf('.');
            if (dotIdx > 0 && dotIdx < id.Length - 1)
            {
                var suffix = id.AsSpan(dotIdx + 1);
                var allDigits = true;
                foreach (var ch in suffix)
                {
                    if (!char.IsAsciiDigit(ch))
                    {
                        allDigits = false;
                        break;
                    }
                }

                if (allDigits)
                {
                    return id[..dotIdx];
                }
            }
        }

        return id;
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

    /// <summary>
    /// Peeks at the first sequence ID in a FASTA file.
    /// Returns true when the FASTA contains chromosomal/genomic sequences (NC_*, NT_*, NW_* accessions)
    /// rather than per-transcript RNA sequences (NM_*, NR_*, XM_*, …).
    /// </summary>
    private static async Task<bool> IsGenomicFasta(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        var reader = new FastAReader(NullLogger<FastAReader>.Instance);
        var reads = path.EndsWith(".gz", StringComparison.OrdinalIgnoreCase)
            ? reader.ReadGz(path, cancellationToken: cancellationToken)
            : reader.Read(path, cancellationToken);

        await foreach (var seq in reads.ConfigureAwait(false))
        {
            var firstId = NormalizeSequenceId(seq.Id);
            return firstId.StartsWith("NC_", StringComparison.OrdinalIgnoreCase)
                   || firstId.StartsWith("NT_", StringComparison.OrdinalIgnoreCase)
                   || firstId.StartsWith("NW_", StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    /// <summary>
    /// Streams a chromosomal (genomic) FASTA file and, for each chromosome, extracts the
    /// transcript sequence by splicing together exon sub-sequences (handling reverse-strand
    /// transcripts with a reverse-complement).
    /// Memory footprint: one chromosome sequence at a time (~250 MB for the largest human chromosome).
    /// </summary>
    private static async IAsyncEnumerable<StoredTranscript> BuildFromGenomicFasta(
        IReadOnlyDictionary<string, TranscriptImportAccumulator> accumulators,
        string fastaPath,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // Index accumulators by chromosome so we only need one chromosome in memory at a time.
        var byChromosome = accumulators
            .Where(kvp => !string.IsNullOrEmpty(kvp.Value.Chromosome))
            .GroupBy(kvp => kvp.Value.Chromosome, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        if (byChromosome.Count == 0)
        {
            yield break;
        }

        var fastaReader = new FastAReader(NullLogger<FastAReader>.Instance);
        var reads = fastaPath.EndsWith(".gz", StringComparison.OrdinalIgnoreCase)
            ? fastaReader.ReadGz(fastaPath, cancellationToken: cancellationToken)
            : fastaReader.Read(fastaPath, cancellationToken);

        await foreach (var chromSeq in reads.ConfigureAwait(false))
        {
            // The FASTA header is e.g. "NC_000001.11 Homo sapiens chromosome 1, GRCh38.p14 Primary Assembly".
            // NormalizeSequenceId strips everything after the first whitespace → "NC_000001.11".
            var chromKey = NormalizeSequenceId(chromSeq.Id);
            if (!byChromosome.TryGetValue(chromKey, out var transcriptList))
            {
                continue;
            }

            var chromData = chromSeq.GetData(); // ReadOnlyMemory<char>

            foreach (var (transcriptId, accumulator) in transcriptList)
            {
                var orderedExons = accumulator.Exons.OrderBy(e => e.Start).ToList();

                string sequence;
                if (orderedExons.Count == 0)
                {
                    // No exon records — use the gene span as a fallback.
                    if (accumulator.GeneStart <= 0 || accumulator.GeneEnd <= 0)
                    {
                        continue;
                    }

                    var gStart = Math.Max(0, accumulator.GeneStart - 1);
                    var gEnd = Math.Min(chromData.Length, accumulator.GeneEnd);
                    if (gStart >= gEnd)
                    {
                        continue;
                    }

                    var span = chromData.Span.Slice(gStart, gEnd - gStart);
                    sequence = accumulator.Strand == '-' ? ReverseComplement(span) : new string(span);
                }
                else
                {
                    var sb = new StringBuilder();
                    foreach (var (exonStart, exonEnd) in orderedExons)
                    {
                        var s = Math.Max(0, exonStart - 1); // GFF3 uses 1-based coordinates
                        var e = Math.Min(chromData.Length, exonEnd);
                        if (s < e)
                        {
                            sb.Append(chromData.Span.Slice(s, e - s));
                        }
                    }

                    var raw = sb.ToString();
                    sequence = accumulator.Strand == '-' ? ReverseComplement(raw.AsSpan()) : raw;
                }

                if (sequence.Length == 0)
                {
                    continue;
                }

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
                    ? accumulator.CdsSegments.Min(seg => seg.Start)
                    : accumulator.GeneStart;
                var cdsEnd = accumulator.CdsSegments.Count > 0
                    ? accumulator.CdsSegments.Max(seg => seg.End)
                    : accumulator.GeneEnd;

                if (cdsStart <= 0 || cdsEnd <= 0)
                {
                    continue;
                }

                var context = new AnnotationContext
                {
                    CdsStart = cdsStart,
                    CdsEnd = cdsEnd,
                    TranscriptLength = sequence.Length,
                    GeneBoundaries = (accumulator.GeneStart > 0 ? accumulator.GeneStart : cdsStart,
                        accumulator.GeneEnd > 0 ? accumulator.GeneEnd : cdsEnd),
                    ExonBoundaries = orderedExons,
                    Introns = orderedIntrons
                };

                yield return new StoredTranscript(
                    TranscriptId: transcriptId,
                    GeneId: EmptyToNull(accumulator.GeneId),
                    GeneName: EmptyToNull(accumulator.GeneName),
                    Chromosome: string.IsNullOrWhiteSpace(accumulator.Chromosome)
                        ? string.Empty
                        : accumulator.Chromosome,
                    Strand: accumulator.Strand,
                    IsCanonical: accumulator.IsCanonical,
                    Sequence: sequence,
                    Context: context,
                    Exons: orderedExons,
                    Introns: orderedIntrons);
            }
        }
    }

    /// <summary>Computes the reverse complement of a DNA sequence span.</summary>
    private static string ReverseComplement(ReadOnlySpan<char> sequence)
    {
        if (sequence.IsEmpty)
        {
            return string.Empty;
        }

        var complement = new char[sequence.Length];
        for (var i = 0; i < sequence.Length; i++)
        {
            var b = char.ToUpperInvariant(sequence[sequence.Length - 1 - i]);
            complement[i] = b switch
            {
                'A' => 'T',
                'T' => 'A',
                'C' => 'G',
                'G' => 'C',
                _ => b
            };
        }

        return new string(complement);
    }
}
