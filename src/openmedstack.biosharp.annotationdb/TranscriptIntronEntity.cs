namespace OpenMedStack.BioSharp.AnnotationDb;

public sealed class TranscriptIntronEntity
{
    public string TranscriptId { get; set; } = string.Empty;

    public int IntronIndex { get; set; }

    public int IntronStart { get; set; }

    public int IntronEnd { get; set; }

    public TranscriptEntity? Transcript { get; set; }
}
