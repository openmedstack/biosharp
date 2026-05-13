using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;

namespace OpenMedStack.BioSharp.AnnotationDb;

public sealed class RefSeqTranscriptDatabaseImporter : ITranscriptDatabaseImporter
{
    public string SourceName => "RefSeq";

    public async IAsyncEnumerable<StoredTranscript> Import(
        TranscriptImportRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var transcript in TranscriptImportParser.ImportRefSeq(
                           request,
                           cancellationToken))
        {
            yield return transcript;
        }
    }
}
