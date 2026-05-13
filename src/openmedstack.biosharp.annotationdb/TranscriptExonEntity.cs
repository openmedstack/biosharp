namespace OpenMedStack.BioSharp.AnnotationDb;

public sealed class TranscriptExonEntity
{
    public string TranscriptId { get; set; } = string.Empty;

    public int ExonIndex { get; set; }

    public int ExonStart { get; set; }

    public int ExonEnd { get; set; }

    public TranscriptEntity? Transcript { get; set; }
}
