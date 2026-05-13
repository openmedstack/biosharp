namespace OpenMedStack.BioSharp.Model;

using System.Collections.Generic;

/// <summary>
/// Represents a single feature record from a GFF3 or GTF annotation file.
/// </summary>
public record GffRecord
{
    /// <summary>Chromosome/scaffold name (column 1).</summary>
    public string Seqname { get; init; } = "";

    /// <summary>Feature source/database (column 2). '.' if not specified.</summary>
    public string Source { get; init; } = ".";

    /// <summary>Feature type, e.g. gene, transcript, exon, CDS, UTR (column 3).</summary>
    public string Feature { get; init; } = "";

    /// <summary>1-based start coordinate (column 4, inclusive).</summary>
    public int Start { get; init; }

    /// <summary>1-based end coordinate (column 5, inclusive).</summary>
    public int End { get; init; }

    /// <summary>Score (column 6). Null if '.'.</summary>
    public float? Score { get; init; }

    /// <summary>Strand: '+', '-', or '.' (column 7).</summary>
    public char Strand { get; init; } = '.';

    /// <summary>Reading frame phase 0, 1, or 2 for CDS features (column 8). Null if '.'.</summary>
    public int? Phase { get; init; }

    /// <summary>
    /// Parsed attribute key-value pairs from column 9.
    /// In GFF3: key=value pairs separated by ';'.
    /// In GTF: key "value" pairs separated by '; '.
    /// Multi-value attributes are stored with the first value only (use <see cref="MultiValueAttributes"/> for all).
    /// </summary>
    public IReadOnlyDictionary<string, string> Attributes { get; init; } =
        new Dictionary<string, string>();

    /// <summary>
    /// Multi-value attributes where a key maps to multiple values (GFF3 comma-separated values).
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> MultiValueAttributes { get; init; } =
        new Dictionary<string, IReadOnlyList<string>>();

    /// <summary>The dialect in which this record was parsed.</summary>
    public GffDialect Dialect { get; init; }
}