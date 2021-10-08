namespace OpenMedStack.BioSharp.Model.Bcl
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.RegularExpressions;

    public class ReadStructure
    {
        private static readonly Regex ReadPattern = new("(?<read>\\d{1,}[TBSM])", RegexOptions.Compiled);
        public ReadStructure(params Read[] reads)
        {
            Reads = reads.ToList();
        }

        public static ReadStructure Parse(string input)
        {
            var matches = ReadPattern.Matches(input).Select((m, i) =>
            {
                var value = m.Groups["read"].Value;
                return new Read
                {
                    IsIndexedRead = value.EndsWith('B') ? "Y" : "N",
                    Number = i + 1,
                    NumCycles = int.Parse(value[..^1]),
                    Type = Enum.Parse<ReadType>(value[^1..], true)
                };
            }).ToArray();
            return new ReadStructure(matches);
        }

        public List<Read> Reads { get; }

        /// <inheritdoc />
        public override string ToString()
        {
            return string.Join("", Reads.Select(r => $"{r.NumCycles}{r.Type}"));
        }
    }
}