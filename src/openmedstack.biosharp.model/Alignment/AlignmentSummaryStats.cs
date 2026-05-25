using System.Collections.Generic;

namespace OpenMedStack.BioSharp.Model.Alignment;

/// <summary>
/// Summary statistics from scanning a BAM stream, equivalent to <c>samtools flagstat</c>
/// plus <c>samtools stats</c>.
/// </summary>
public sealed class AlignmentSummaryStats
{
    /// <summary>Total reads (all records).</summary>
    public long TotalReads { get; init; }

    /// <summary>Reads that are mapped (RNAME != "*" and not 0x4 flag).</summary>
    public long MappedReads { get; init; }

    /// <summary>Reads with the unmapped flag (0x4).</summary>
    public long UnmappedReads { get; init; }

    /// <summary>Fraction of reads that are mapped.</summary>
    public double MappingRate
    {
        get { return TotalReads == 0 ? 0.0 : (double)MappedReads / TotalReads; }
    }

    /// <summary>Reads with both 0x1 (multi-segment) and 0x2 (properly aligned) flags.</summary>
    public long ProperlyPairedReads { get; init; }

    /// <summary>Reads that are part of a pair but whose mate is unmapped (singletons).</summary>
    public long SingletonReads { get; init; }

    /// <summary>Reads with the PCR/optical duplicate flag (0x400).</summary>
    public long DuplicateReads { get; init; }

    /// <summary>Reads with the secondary alignment flag (0x100).</summary>
    public long SecondaryAlignmentReads { get; init; }

    /// <summary>Reads with the supplementary alignment flag (0x800).</summary>
    public long SupplementaryAlignmentReads { get; init; }

    /// <summary>Average mapping quality across mapped reads.</summary>
    public double AverageMappingQuality { get; init; }

    /// <summary>Read length histogram: length → count.</summary>
    public Dictionary<int, int> ReadLengthDistribution { get; init; } = new();

    /// <summary>Error rate: mismatches per mapped base (from NM tag when present).</summary>
    public double ErrorRate { get; init; }
}