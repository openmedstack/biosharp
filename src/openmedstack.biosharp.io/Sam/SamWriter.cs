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
        await using var writer = new StreamWriter(stream, Encoding.UTF8);
        await writer.WriteLineAsync(definition.Hd.ToString().AsMemory(), cancellationToken);
        await writer.WriteLineAsync(definition.Pg.ToString().AsMemory(), cancellationToken);
        await writer.WriteLineAsync(definition.Rg.ToString().AsMemory(), cancellationToken);
        foreach (var referenceSequence in definition.Sq)
        {
            await writer.WriteLineAsync(referenceSequence.ToString().AsMemory(), cancellationToken);
        }

        foreach (var alignmentSection in definition.AlignmentSections)
        {
            await writer.WriteLineAsync(alignmentSection.ToString().AsMemory(), cancellationToken);
        }
    }
}