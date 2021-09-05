namespace OpenMedStack.BioSharp.Io.Sam
{
    using System;

    public record ReferenceSequence
    {
        private ReferenceSequence(string sn, int ln)
        {
            Sn = sn;
            Ln = ln;
        }

        public string Sn { get; }

        public int Ln { get; }

        public static ReferenceSequence Parse(string line)
        {
            var parts = line[4..].Split('\t', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return new ReferenceSequence(parts[0][3..]!, int.Parse(parts[1].AsSpan(3)));
        }
    }
}