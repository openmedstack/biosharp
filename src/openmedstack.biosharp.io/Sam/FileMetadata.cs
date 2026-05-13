namespace OpenMedStack.BioSharp.Io.Sam;

using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

public partial record FileMetadata
{
    internal FileMetadata(string vn, SortingOrder so, AlignmentGrouping go, string? subSort = null)
    {
        if (!FileMetadataRegex().IsMatch(vn))
        {
            throw new ArgumentException("Value does not match regex: /^[0-9]+\\.[0-9]+$/", nameof(vn));
        }

        Vn = vn;
        So = so;
        Go = go;
        SubSort = subSort;
    }

    public string Vn { get; }

    public SortingOrder So { get; }

    public AlignmentGrouping Go { get; }
    public string? SubSort { get; }

    public static FileMetadata Parse(string line)
    {
        var span = line.AsSpan(4);
        Span<Range> tabRanges = stackalloc Range[20];
        var count = span.Split(tabRanges, '\t', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var parts = new Dictionary<string, string>(count);
        for (var i = 0; i < count; i++)
        {
            var field = span[tabRanges[i]];
            var colon = field.IndexOf(':');
            if (colon < 1)
            {
                continue;
            }

            parts[new string(field[..colon])] = new string(field[(colon + 1)..]);
        }
        return new FileMetadata(
            parts["VN"],
            parts.TryGetValue("SO", out var so) ? Enum.Parse<SortingOrder>(so, true) : SortingOrder.Unknown,
            parts.TryGetValue("GO", out var go) ? Enum.Parse<AlignmentGrouping>(go, true) : AlignmentGrouping.None,
            parts.GetValueOrDefault("SS"));
    }

    public enum SortingOrder : byte
    {
        /// <summary>
        /// default
        /// </summary>
        Unknown = 0,
        Unsorted = 1,
        Queryname = 2,
        Coordinate = 3
    }

    public enum AlignmentGrouping : byte
    {
        /// <summary>
        /// default
        /// </summary>
        None = 0,

        /// <summary>
        ///  alignments are grouped by QNAME
        /// </summary>
        Query = 1,

        /// <summary>
        ///  alignments are grouped by RNAME/POS
        /// </summary>
        Reference = 2
    }

    /// <inheritdoc />
    public override string ToString()
    {
        if (So == SortingOrder.Unknown)
        {
            return "";
        }

        var builder = new StringBuilder($"@HD\tVN:{Vn}\tSO:{So.ToString().ToLowerInvariant()}");
        if (SubSort != null)
        {
            builder.Append($"\tSS:{SubSort}");
        }

        return builder.ToString();
    }

    [GeneratedRegex("^[0-9]+\\.[0-9]+$")]
    private static partial Regex FileMetadataRegex();
}
