using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using OpenMedStack.BioSharp.Model;

namespace OpenMedStack.BioSharp.Io.Bed;

/// <summary>
/// Reads BED (3-to-12 column) and BEDGraph (4-column) interval files,
/// yielding <see cref="BedInterval"/> records.
/// Coordinates are zero-based half-open [start, end) per the BED specification.
/// Comment lines starting with '#' and track/browser header lines are skipped.
/// </summary>
public class BedReader
{
    /// <summary>
    /// Reads BED or BEDGraph intervals from a file path.
    /// The format is auto-detected from the column count of the first non-header data line.
    /// </summary>
    public async IAsyncEnumerable<BedInterval> Read(
        string path,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await using var file = File.Open(
            path,
            new FileStreamOptions
            {
                Access = FileAccess.Read,
                Mode = FileMode.Open,
                Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
                Share = FileShare.Read
            });

        await foreach (var interval in Read(file, cancellationToken).ConfigureAwait(false))
        {
            yield return interval;
        }
    }

    /// <summary>
    /// Reads BED or BEDGraph intervals from a stream.
    /// </summary>
    public static async IAsyncEnumerable<BedInterval> Read(
        Stream stream,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var reader = new StreamReader(stream, leaveOpen: true);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line == null)
            {
                yield break;
            }

            // Skip blank lines, comment lines, and track/browser headers
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            if (line.StartsWith('#'))
            {
                continue;
            }

            if (line.StartsWith("track", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (line.StartsWith("browser", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var interval = ParseLine(line);
            if (interval != null)
            {
                yield return interval;
            }
        }
    }

    private static BedInterval? ParseLine(string line)
    {
        var span = line.AsSpan();
        Span<Range> ranges = stackalloc Range[13];
        var count = span.Split(ranges, '\t');

        if (count < 3)
        {
            return null;
        }

        var chrom = new string(span[ranges[0]]);
        if (!int.TryParse(span[ranges[1]], out var start))
        {
            return null;
        }

        if (!int.TryParse(span[ranges[2]], out var end))
        {
            return null;
        }

        // BEDGraph: exactly 4 columns where column 4 is a float/score value
        if (count == 4 && float.TryParse(span[ranges[3]], out var bedGraphValue))
        {
            return new BedInterval
            {
                Chrom = chrom,
                Start = start,
                End = end,
                Value = bedGraphValue,
                IsBedGraph = true
            };
        }

        // Standard BED: column 4 onward are optional named fields
        var name = count > 3 ? new string(span[ranges[3]]) : null;
        float? score = null;
        if (count > 4 && float.TryParse(span[ranges[4]], out var s))
        {
            score = s;
        }

        char? strand = null;
        if (count > 5)
        {
            var strandSpan = span[ranges[5]];
            if (strandSpan.Length == 1 && (strandSpan[0] == '+' || strandSpan[0] == '-' || strandSpan[0] == '.'))
            {
                strand = strandSpan[0];
            }
        }

        int? thickStart = null, thickEnd = null;
        if (count > 6 && int.TryParse(span[ranges[6]], out var ts))
        {
            thickStart = ts;
        }

        if (count > 7 && int.TryParse(span[ranges[7]], out var te))
        {
            thickEnd = te;
        }

        var itemRgb = count > 8 ? new string(span[ranges[8]]) : null;

        int? blockCount = null;
        string? blockSizes = null, blockStarts = null;
        if (count > 9 && int.TryParse(span[ranges[9]], out var bc))
        {
            blockCount = bc;
            blockSizes = count > 10 ? new string(span[ranges[10]]) : null;
            blockStarts = count > 11 ? new string(span[ranges[11]]) : null;
        }

        return new BedInterval
        {
            Chrom = chrom,
            Start = start,
            End = end,
            Name = name,
            Score = score,
            Strand = strand,
            ThickStart = thickStart,
            ThickEnd = thickEnd,
            ItemRgb = itemRgb,
            BlockCount = blockCount,
            BlockSizes = blockSizes,
            BlockStarts = blockStarts
        };
    }
}
