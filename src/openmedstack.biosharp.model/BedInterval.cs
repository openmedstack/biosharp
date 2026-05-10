namespace OpenMedStack.BioSharp.Model;

/// <summary>
/// Represents a single interval from a BED or BEDGraph file.
/// Coordinates follow the BED convention: 0-based, half-open [start, end).
/// </summary>
public record BedInterval
{
    /// <summary>Chromosome or sequence name.</summary>
    public string Chrom { get; init; } = "";

    /// <summary>0-based start position (inclusive).</summary>
    public int Start { get; init; }

    /// <summary>0-based end position (exclusive).</summary>
    public int End { get; init; }

    /// <summary>Feature name (column 4). Null if not present.</summary>
    public string? Name { get; init; }

    /// <summary>Score 0–1000 (column 5). Null if not present.</summary>
    public float? Score { get; init; }

    /// <summary>Strand: '+', '-', or '.' (column 6). Null if not present.</summary>
    public char? Strand { get; init; }

    /// <summary>BEDGraph value (column 4 in 4-column BEDGraph). Null if not BEDGraph.</summary>
    public float? Value { get; init; }

    /// <summary>True when this record came from a BEDGraph file (4 numeric columns).</summary>
    public bool IsBedGraph { get; init; }

    /// <summary>Thick start position (column 7, 0-based). Null if not present.</summary>
    public int? ThickStart { get; init; }

    /// <summary>Thick end position (column 8, 0-based). Null if not present.</summary>
    public int? ThickEnd { get; init; }

    /// <summary>Item RGB colour as "R,G,B" string (column 9). Null if not present.</summary>
    public string? ItemRgb { get; init; }

    /// <summary>Block count (column 10). Null if not present.</summary>
    public int? BlockCount { get; init; }

    /// <summary>Block sizes as comma-separated list (column 11). Null if not present.</summary>
    public string? BlockSizes { get; init; }

    /// <summary>Block starts as comma-separated list (column 12, relative to Start). Null if not present.</summary>
    public string? BlockStarts { get; init; }
}
