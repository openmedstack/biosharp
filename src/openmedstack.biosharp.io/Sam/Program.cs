namespace OpenMedStack.BioSharp.Io.Sam
{
    using System;

    public record Program
    {
        private Program(string id)
        {
            Id = id;
        }

        public string Id { get; }

        public static Program Parse(string line)
        {
            var parts = line[4..].Split('\t', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return new Program(parts[0][3..]);
        }
    }
}