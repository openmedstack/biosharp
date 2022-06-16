namespace OpenMedStack.BioSharp.Io.Sam;

using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

public class SamWriter
{
    private readonly ILogger _logger;

    public SamWriter(ILogger logger)
    {
        _logger = logger;
    }

    public async Task Write(SamDefinition definition, Stream stream, CancellationToken cancellationToken)
    {
        _logger.LogTrace("Writing SAM content");
        var writer = new StreamWriter(stream, Encoding.UTF8);
        await using var _ = writer.ConfigureAwait(false);
        await writer.WriteLineAsync(definition.Hd.ToString().AsMemory(), cancellationToken).ConfigureAwait(false);
        await writer.WriteLineAsync(definition.Pg.ToString().AsMemory(), cancellationToken).ConfigureAwait(false);
        await writer.WriteLineAsync(definition.Rg.ToString().AsMemory(), cancellationToken).ConfigureAwait(false);
        foreach (var referenceSequence in definition.Sq)
        {
            await writer.WriteLineAsync(referenceSequence.ToString().AsMemory(), cancellationToken).ConfigureAwait(false);
        }

        foreach (var alignmentSection in definition.AlignmentSections)
        {
            await writer.WriteLineAsync(alignmentSection.ToString().AsMemory(), cancellationToken).ConfigureAwait(false);
        }
    }
}