namespace OpenMedStack.BioSharp.Io.Sam
{
    using System;
    using System.Text.RegularExpressions;

    public record FileMetadata
    {
        private FileMetadata(string vn, SortingOrder so, AlignmentGrouping go)
        {
            if (!Regex.IsMatch(vn, "^[0-9]+\\.[0-9]+$"))
            {
                throw new ArgumentException("Value does not match regex: /^[0-9]+\\.[0-9]+$/", nameof(vn));
            }

            Vn = vn;
            So = so;
            Go = go;
        }

        public string Vn { get; }

        public SortingOrder So { get; }

        public AlignmentGrouping Go { get; }

        public static FileMetadata Parse(string line)
        {
            var parts = line[4..].Split('\t', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return new FileMetadata(
                parts[0][3..],
                Enum.Parse<SortingOrder>(parts[1][3..], true),
                AlignmentGrouping.none);
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
    }
}