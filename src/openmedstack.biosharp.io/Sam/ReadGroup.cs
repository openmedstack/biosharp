namespace OpenMedStack.BioSharp.Io.Sam
{
    using System;

    public record ReadGroup
    {
        public string Id { get; init; }

        public static ReadGroup Parse(string line)
        {
            var parts = line[4..].Split('\t', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return new ReadGroup {Id = parts[0][3..] };
        }
    }
}