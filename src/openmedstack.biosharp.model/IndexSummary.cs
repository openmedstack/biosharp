namespace OpenMedStack.BioSharp.Model;

/// <summary>
/// Metadata produced by the <c>preator index</c> command after building a reference index.
/// </summary>
public sealed class IndexSummary
{
    /// <summary>Path to the input FASTA or BAM file that was indexed.</summary>
    public string InputPath { get; init; } = string.Empty;

    /// <summary><c>fasta</c> or <c>bam</c>.</summary>
    public string InputFormat { get; init; } = string.Empty;

    /// <summary>Ordered list of index types built, e.g. ["FmIndex", "ReferenceIndex"].</summary>
    public string[] IndexTypes { get; init; } = [];

    /// <summary>Path to the saved FM-index file (<c>.fmi</c>), or <c>null</c> if not built.</summary>
    public string? FmIndexPath { get; init; }

    /// <summary>Path to the saved k-mer reference-index file (<c>.bsi</c>), or <c>null</c> if not built.</summary>
    public string? ReferenceIndexPath { get; init; }

    /// <summary>Length of the indexed sequence in bases.</summary>
    public long SequenceLength { get; init; }

    /// <summary>Sample rate used when building the FM-index (higher = smaller file, slower locate).</summary>
    public int FmSampleRate { get; init; }

    /// <summary>Wall-clock milliseconds spent building all indexes.</summary>
    public double BuildTimeMs { get; init; }
}
