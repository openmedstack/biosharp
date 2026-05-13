using System.Collections.Generic;
using System.Threading;

namespace OpenMedStack.BioSharp.AnnotationDb;

public interface ITranscriptDatabaseImporter
{
    string SourceName { get; }

    IAsyncEnumerable<StoredTranscript> Import(
        TranscriptImportRequest request,
        CancellationToken cancellationToken = default);
}
