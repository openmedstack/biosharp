namespace OpenMedStack.BioSharp.Io.Sam
{
    using System;
    using System.Linq;
    using System.Text;
    using System.Text.RegularExpressions;

    public record FileMetadata
    {
        internal FileMetadata(string vn, SortingOrder so, AlignmentGrouping go, string? subSort = null)
        {
            if (!Regex.IsMatch(vn, "^[0-9]+\\.[0-9]+$"))
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
            var parts = line[4..]
                .Split('\t', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(x => x.Split(':'))
                .ToDictionary(x => x[0], x => string.Join(':', x.Skip(1)));
            return new FileMetadata(
                parts["VN"],
                parts.TryGetValue("SO", out var so) ? Enum.Parse<SortingOrder>(so, true) : SortingOrder.unknown,
                parts.TryGetValue("GO", out var go) ? Enum.Parse<AlignmentGrouping>(go, true) : AlignmentGrouping.none,
                parts.TryGetValue("SS", out var ss) ? ss : null);
        }

        public enum SortingOrder : byte
        {
            /// <summary>
            /// default
            /// </summary>
            unknown = 0,
            unsorted = 1,
            queryname = 2,
            coordinate = 3
        }

        public enum AlignmentGrouping : byte
        {
            /// <summary>
            /// default
            /// </summary>
            none = 0,

            /// <summary>
            ///  alignments are grouped by QNAME
            /// </summary>
            query = 1,

            /// <summary>
            ///  alignments are grouped by RNAME/POS
            /// </summary>
            reference = 2
        }

        /// <inheritdoc />
        public override string ToString()
        {
            var builder = new StringBuilder( $"@HD\tVN:{Vn}\tSO:{So.ToString().ToLowerInvariant()}");
            if (SubSort != null)
            {
                builder.Append($"\tSS:{SubSort}");
            }

            return builder.ToString();
        }
    }
}