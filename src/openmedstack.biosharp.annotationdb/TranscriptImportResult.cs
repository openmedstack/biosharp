namespace OpenMedStack.BioSharp.AnnotationDb;

public sealed record TranscriptImportResult(
    string SourceName,
    string SourceId,
    int TranscriptCount);
