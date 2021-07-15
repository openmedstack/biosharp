namespace OpenMedStack.BioSharp.Io.Sam
{
    using System;

    public class ReferenceSequence
    {
        private ReferenceSequence() { }

        public string Sn { get; init; }

        public int Ln { get; init; }

        public static ReferenceSequence Parse(string line)
        {
            var parts = line.Split('\t', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return new ReferenceSequence { Sn = parts[0], Ln = int.Parse(parts[1]) };
        }
    }
}