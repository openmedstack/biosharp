namespace OpenMedStack.BioSharp.Io;

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Model;

/// <summary>
/// Reads GFF3 and GTF annotation files, yielding <see cref="GffRecord"/> instances.
/// Lines starting with '#' are treated as comments and skipped, except the '##gff-version' pragma
/// which is used to detect GFF3 format.
/// </summary>
public class GffReader
{
    /// <summary>
    /// Reads GFF records from a file path.
    /// The dialect (GFF3 vs GTF) is auto-detected from the ##gff-version pragma or
    /// from the attribute quoting style.
    /// </summary>
    public async IAsyncEnumerable<GffRecord> ReadAsync(
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

        await foreach (var record in ReadAsync(file, cancellationToken).ConfigureAwait(false))
        {
            yield return record;
        }
    }

    /// <summary>
    /// Reads GFF records from a stream.
    /// </summary>
    public async IAsyncEnumerable<GffRecord> ReadAsync(
        Stream stream,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var reader = new StreamReader(stream, leaveOpen: true);
        var dialect = GffDialect.Gff3; // default
        var dialectDetected = false;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line == null)
            {
                yield break;
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            if (line.StartsWith("##"))
            {
                // Check for GFF version pragma
                if (line.StartsWith("##gff-version", StringComparison.OrdinalIgnoreCase))
                {
                    dialect = GffDialect.Gff3;
                    dialectDetected = true;
                }
                continue;
            }

            if (line.StartsWith('#'))
            {
                continue;
            }

            var record = ParseLine(line, ref dialect, ref dialectDetected);
            if (record != null)
            {
                yield return record;
            }
        }
    }

    private static GffRecord? ParseLine(string line, ref GffDialect dialect, ref bool dialectDetected)
    {
        var span = line.AsSpan();
        Span<Range> ranges = stackalloc Range[9];
        var count = span.Split(ranges, '\t');

        if (count < 8)
        {
            return null;
        }

        var seqname = new string(span[ranges[0]]);
        var source = new string(span[ranges[1]]);
        var feature = new string(span[ranges[2]]);

        if (!int.TryParse(span[ranges[3]], out var start))
        {
            return null;
        }

        if (!int.TryParse(span[ranges[4]], out var end))
        {
            return null;
        }

        var scoreSpan = span[ranges[5]];
        float? score = scoreSpan.Length == 1 && scoreSpan[0] == '.' ? null :
            float.TryParse(scoreSpan, out var s) ? s : null;

        var strandChar = span[ranges[6]].Length > 0 ? span[ranges[6]][0] : '.';

        var phaseSpan = span[ranges[7]];
        int? phase = phaseSpan.Length == 1 && phaseSpan[0] == '.' ? null :
            int.TryParse(phaseSpan, out var p) ? p : null;

        var attributes = new Dictionary<string, string>();
        var multiValueAttributes = new Dictionary<string, IReadOnlyList<string>>();

        if (count >= 9)
        {
            var attrString = new string(span[ranges[8]]);

            // Auto-detect dialect from attribute syntax if not yet determined
            if (!dialectDetected)
            {
                // GTF uses key "value"; pairs; GFF3 uses key=value pairs
                dialect = attrString.Contains('=') ? GffDialect.Gff3 : GffDialect.Gtf;
                dialectDetected = true;
            }

            if (dialect == GffDialect.Gff3)
            {
                ParseGff3Attributes(attrString, attributes, multiValueAttributes);
            }
            else
            {
                ParseGtfAttributes(attrString, attributes);
            }
        }

        return new GffRecord
        {
            Seqname = seqname,
            Source = source,
            Feature = feature,
            Start = start,
            End = end,
            Score = score,
            Strand = strandChar,
            Phase = phase,
            Attributes = attributes,
            MultiValueAttributes = multiValueAttributes,
            Dialect = dialect
        };
    }

    private static void ParseGff3Attributes(
        string attrString,
        Dictionary<string, string> attributes,
        Dictionary<string, IReadOnlyList<string>> multiValueAttributes)
    {
        var attrSpan = attrString.AsSpan();
        foreach (var semiRange in attrSpan.Split(';'))
        {
            var pair = attrSpan[semiRange].Trim();
            if (pair.IsEmpty)
            {
                continue;
            }

            var eq = pair.IndexOf('=');
            if (eq < 1)
            {
                continue;
            }

            var key = new string(pair[..eq].Trim());
            var rawValue = pair[(eq + 1)..].Trim();

            // GFF3 allows comma-separated multi-values
            if (rawValue.IndexOf(',') >= 0)
            {
                var valueList = new List<string>();
                foreach (var commaRange in rawValue.Split(','))
                {
                    var v = rawValue[commaRange];
                    if (!v.IsEmpty)
                    {
                        valueList.Add(Uri.UnescapeDataString(new string(v)));
                    }
                }
                if (valueList.Count > 0)
                {
                    multiValueAttributes[key] = valueList;
                    attributes[key] = valueList[0];
                }
            }
            else
            {
                attributes[key] = Uri.UnescapeDataString(new string(rawValue));
            }
        }
    }

    private static void ParseGtfAttributes(string attrString, Dictionary<string, string> attributes)
    {
        // GTF format: key "value"; key2 "value2";
        var attrSpan = attrString.AsSpan();
        foreach (var semiRange in attrSpan.Split(';'))
        {
            var pair = attrSpan[semiRange].Trim();
            if (pair.IsEmpty)
            {
                continue;
            }

            var spaceIdx = pair.IndexOf(' ');
            if (spaceIdx < 1)
            {
                continue;
            }

            var key = new string(pair[..spaceIdx].Trim());
            var rawValue = pair[(spaceIdx + 1)..].Trim();

            // Strip surrounding quotes if present
            if (rawValue.Length >= 2 && rawValue[0] == '"' && rawValue[^1] == '"')
            {
                rawValue = rawValue[1..^1];
            }

            attributes[key] = new string(rawValue);
        }
    }
}
