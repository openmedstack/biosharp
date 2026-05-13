using System.Collections.Generic;
using OpenMedStack.BioSharp.Model;

namespace OpenMedStack.BioSharp.AnnotationDb;

public sealed record StoredTranscript(
    string TranscriptId,
    string? GeneId,
    string? GeneName,
    string Chromosome,
    char Strand,
    bool IsCanonical,
    string Sequence,
    AnnotationContext Context,
    IReadOnlyList<(int Start, int End)> Exons,
    IReadOnlyList<(int Start, int End)> Introns);
