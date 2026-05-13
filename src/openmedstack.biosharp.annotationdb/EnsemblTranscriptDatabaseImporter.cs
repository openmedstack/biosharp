using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;

namespace OpenMedStack.BioSharp.AnnotationDb;

public sealed class EnsemblTranscriptDatabaseImporter : ITranscriptDatabaseImporter
{
    public string SourceName => "Ensembl";

    public async IAsyncEnumerable<StoredTranscript> Import(
        TranscriptImportRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var transcript in TranscriptImportParser.ImportEnsemblStyle(
                           request,
                           SourceName,
                           cancellationToken))
        {
            yield return transcript;
        }
    }
}
