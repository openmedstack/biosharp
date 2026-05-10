namespace OpenMedStack.BioSharp.Io.Sam;

using System;
using System.Collections.Generic;

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
        var span = line.AsSpan(4);
        Span<Range> tabRanges = stackalloc Range[20];
        var count = span.Split(tabRanges, '\t', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var parts = new Dictionary<string, string>(count);
        for (var i = 0; i < count; i++)
        {
            var field = span[tabRanges[i]];
            var colon = field.IndexOf(':');
            if (colon < 1)
            {
                continue;
            }

            parts[new string(field[..colon])] = new string(field[(colon + 1)..]);
        }

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
