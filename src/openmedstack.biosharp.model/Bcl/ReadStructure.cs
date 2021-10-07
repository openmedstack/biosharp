namespace OpenMedStack.BioSharp.Model.Bcl
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.RegularExpressions;

    public enum ReadType : byte
    {
        Template,
        Barcode,
        MolecularBarcode,
        Skip
    }

    public class ReadStructure
    {
        private static readonly Regex ReadPattern = new Regex("(?<read>\\d{1,}[TBSM])", RegexOptions.Compiled);
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
                    Number = i,
                    NumCycles = int.Parse(value[..^1]),
                    Type = Enum.Parse<ReadType>(value[^1..], true)
                };
            }).ToArray();
            return new ReadStructure(matches);
        }

        public List<Read> Reads { get; }
    }
}