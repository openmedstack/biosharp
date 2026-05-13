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
            await writer.WriteLineAsync(referenceSequence.ToString().AsMemory(), cancellationToken)
                .ConfigureAwait(false);
        }

        foreach (var alignmentSection in definition.AlignmentSections)
        {
            var cigarString = CigarString(alignmentSection);
            await writer.WriteLineAsync(BuildSamLine(alignmentSection, cigarString).AsMemory(), cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Converts SAM/CIGAR operations to a CIGAR string (e.g. "100M").
    /// Used when writing SAM/BAM alignment records.
    /// </summary>
    private static string CigarString(AlignmentSection section)
    {
        if (section.Cigar == null || section.Cigar.Length == 0)
        {
            return "*";
        }

        var sb = new StringBuilder();
        var samOps = new[] { 'M', 'I', 'D', 'N', 'S', 'H', 'P', '=', 'X' }; // 0-8
        foreach (var (count, op) in section.Cigar)
        {
            var idx = (int)op;
            var samChar = idx >= 0 && idx < samOps.Length ? samOps[idx] : 'M';
            sb.Append(count).Append(samChar);
        }

        return sb.ToString();
    }

    /// <summary>
    /// Builds a single SAM text line for an alignment record.
    /// </summary>
    private static string BuildSamLine(AlignmentSection section, string cigarString)
    {
        var flag = (short)section.Flag;

        var rnext = section.ReferenceIdOfNextSegment;
        var pnex = section.NextPosition;
        var tlen = section.TemplateLength;

        var tags = new StringBuilder();
        foreach (var tag in section.Tags)
        {
            if (tags.Length > 0)
            {
                tags.Append('\t');
            }

            tags.Append(tag);
        }

        // SAM format: QNAME FLAG RNAME POS CIGAR RNEXT PNEXT TLEN SEQ QUAL [TAG...]
        return string.Join("\t",
            section.QName,
            flag,
            section.ReadName,
            section.Position,
            cigarString,
            rnext,
            pnex,
            tlen,
            section.Sequence,
            section.Quality,
            tags);
    }
}
