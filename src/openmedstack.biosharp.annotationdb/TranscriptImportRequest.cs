namespace OpenMedStack.BioSharp.AnnotationDb;

public sealed record TranscriptImportRequest(
    string AnnotationPath,
    string SequencePath,
    string Assembly,
    string SourceVersion);
