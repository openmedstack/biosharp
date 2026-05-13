using System.Collections.Generic;

namespace OpenMedStack.BioSharp.AnnotationDb;

public sealed class TranscriptEntity
{
    public string TranscriptId { get; set; } = string.Empty;

    public string SourceId { get; set; } = string.Empty;

    public string? GeneId { get; set; }

    public string? GeneName { get; set; }

    public string Chromosome { get; set; } = string.Empty;

    public string Strand { get; set; } = string.Empty;

    public int GeneStart { get; set; }

    public int GeneEnd { get; set; }

    public int CdsStart { get; set; }

    public int CdsEnd { get; set; }

    public int TranscriptLength { get; set; }

    public bool IsCanonical { get; set; }

    public string Sequence { get; set; } = string.Empty;

    public TranscriptSourceEntity? Source { get; set; }

    public List<TranscriptExonEntity> Exons { get; set; } = [];

    public List<TranscriptIntronEntity> Introns { get; set; } = [];
}
