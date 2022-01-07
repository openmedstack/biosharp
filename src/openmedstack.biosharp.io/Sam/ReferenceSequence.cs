namespace OpenMedStack.BioSharp.Io.Sam
{
    using System;

    public record ReferenceSequence
    {
        internal ReferenceSequence(string name, uint length)
        {
            Name = name;
            Length = length;
        }

        public string Name { get; }

        public uint Length { get; }

        public static ReferenceSequence Parse(string line)
        {
            var parts = line[4..].Split('\t', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return new ReferenceSequence(parts[0][3..]!, uint.Parse(parts[1].AsSpan(3)));
        }
    }
}