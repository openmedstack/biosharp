namespace OpenMedStack.BioSharp.Io.Sam
{
    using System;

    public record ReadGroup
    {
        private ReadGroup(string id)
        {
            Id = id;
        }

        public string Id { get; }

        public static ReadGroup Parse(string line)
        {
            var parts = line[4..].Split('\t', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return new ReadGroup(parts[0][3..]);
        }
    }
}