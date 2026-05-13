using System;
using System.Collections.Generic;

namespace OpenMedStack.BioSharp.AnnotationDb;

public sealed class TranscriptSourceEntity
{
    public string SourceId { get; set; } = string.Empty;

    public string SourceName { get; set; } = string.Empty;

    public string Assembly { get; set; } = string.Empty;

    public string SourceVersion { get; set; } = string.Empty;

    public string AnnotationPath { get; set; } = string.Empty;

    public string SequencePath { get; set; } = string.Empty;

    public DateTimeOffset ImportedAtUtc { get; set; }

    public List<TranscriptEntity> Transcripts { get; set; } = [];
}
