namespace OpenMedStack.BioSharp.Io.Sam
{
    using System;
    using System.Text.RegularExpressions;

    public record FileMetadata
    {
        private readonly string _version;

        private FileMetadata() { }

        public string Vn
        {
            get { return _version; }
            init
            {
                if (!Regex.IsMatch(value, "^[0-9]+\\.[0-9]+$"))
                {
                    throw new ArgumentException("Value does not match regex: /^[0-9]+\\.[0-9]+$/", nameof(value));
                }

                _version = value;
            }
        }

        public SortingOrder So { get; init; }

        public AlignmentGrouping Go { get; init; }

        public static FileMetadata Parse(string line)
        {
            var parts = line[4..].Split('\t', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return new FileMetadata { Vn = parts[0][3..], So = Enum.Parse<SortingOrder>(parts[1][3..], true) };
        }

        public enum SortingOrder
        {
            /// <summary>
            /// default
            /// </summary>
            unknown = 0,
            unsorted = 1,
            queryname = 2,
            coordinate = 3
        }

        public enum AlignmentGrouping
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