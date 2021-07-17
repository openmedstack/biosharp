namespace OpenMedStack.BioSharp.Io.Sam
{
    using System;

    public record Program
    {
        public string Id { get; init; }

        public static Program Parse(string line)
        {
            var parts = line[4..].Split('\t', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return new Program {Id = parts[0][3..] };
        }
    }
}