namespace OpenMedStack.BioSharp.Io.Sam
{
    using System;
    using System.Linq;

    public record ReferenceSequence
    {
        internal ReferenceSequence(
            string name,
            uint length,
            string? alternateLocus = null,
            string? alternateName = null,
            string? genomeAssembly = null,
            string? description = null,
            string? md5Checksum = null,
            string? species = null,
            string? moleculeTopology = null,
            string? uri = null)
        {
            Name = name;
            Length = length;
            AlternateLocus = alternateLocus;
            AlternateName = alternateName;
            GenomeAssembly = genomeAssembly;
            Description = description;
            Md5Checksum = md5Checksum;
            Species = species;
            MoleculeTopology = moleculeTopology;
            Uri = uri;
        }

        public string Name { get; }

        public uint Length { get; }
        public string? AlternateLocus { get; }
        public string? AlternateName { get; }
        public string? GenomeAssembly { get; }
        public string? Description { get; }
        public string? Md5Checksum { get; }
        public string? Species { get; }
        public string? MoleculeTopology { get; }
        public string? Uri { get; }

        public static ReferenceSequence Parse(string line)
        {
            var parts = line[4..]
                .Split('\t', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(x => x.Split(':'))
                .ToDictionary(x => x[0], x => string.Join(':', x.Skip(1)));

            return new ReferenceSequence(
                parts["SN"],
                uint.Parse(parts["LN"]),
                parts.TryGetValue("AH", out var ah) ? ah : null,
                parts.TryGetValue("AN", out var an) ? an : null,
                parts.TryGetValue("AS", out var xas) ? xas : null,
                parts.TryGetValue("DS", out var ds) ? ds : null,
                parts.TryGetValue("M5", out var m5) ? m5 : null,
                parts.TryGetValue("SP", out var sp) ? sp : null,
                parts.TryGetValue("TP", out var tp) ? tp : null,
                parts.TryGetValue("UR", out var ur) ? ur : null);
        }
    }
}