namespace OpenMedStack.BioSharp.Calculations.Alignment;

using System.Collections.Generic;
using System.Text.Json.Serialization;
using Io.Sam;

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
    public double MappingRate => TotalReads == 0 ? 0.0 : (double)MappedReads / TotalReads;

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

/// <summary>
/// Computes alignment summary statistics from a collection of <see cref="AlignmentSection"/> records
/// in a single streaming pass.
/// </summary>
public static class AlignmentStatsCalculator
{
    /// <summary>
    /// Computes statistics from the given alignment records.
    /// </summary>
    public static AlignmentSummaryStats Compute(IReadOnlyList<AlignmentSection> alignments)
    {
        long total = 0, mapped = 0, unmapped = 0, properlyPaired = 0, singleton = 0;
        long duplicate = 0, secondary = 0, supplementary = 0;
        double mapqSum = 0;
        long mappedForMapq = 0;
        long nmSum = 0;
        long mappedBases = 0;

        var lengthDist = new Dictionary<int, int>();

        foreach (var a in alignments)
        {
            total++;

            var flag = a.Flag;
            var isUnmapped = (flag & AlignmentSection.AlignmentFlag.SegmentUnmapped) != 0;
            var isMultiSegment = (flag & AlignmentSection.AlignmentFlag.MultipleSegments) != 0;
            var isProperlyAligned = (flag & AlignmentSection.AlignmentFlag.EachSegmentProperlyAligned) != 0;
            var isMateUnmapped = (flag & AlignmentSection.AlignmentFlag.NextSegmentInTemplateUnmapped) != 0;
            var isDuplicate = (flag & AlignmentSection.AlignmentFlag.PcrOrOpticalDuplicate) != 0;
            var isSecondary = (flag & AlignmentSection.AlignmentFlag.SecondaryAlignment) != 0;
            var isSupplementary = (flag & AlignmentSection.AlignmentFlag.SupplementaryAlignment) != 0;

            if (isUnmapped)
            {
                unmapped++;
            }
            else
            {
                mapped++;
                mapqSum += a.MappingQuality;
                mappedForMapq++;

                // Error rate from NM tag
                AlignmentTag? nmTag = null;
                foreach (var tag in a.Tags)
                {
                    if (tag.Key == "NM") { nmTag = tag; break; }
                }
                if (nmTag?.Key != null && nmTag.Value is int nm)
                {
                    nmSum += nm;
                    mappedBases += a.Sequence.Length;
                }
                else if (nmTag?.Key != null && int.TryParse(nmTag.Value?.ToString(), out var nmParsed))
                {
                    nmSum += nmParsed;
                    mappedBases += a.Sequence.Length;
                }
            }

            if (isDuplicate)
            {
                duplicate++;
            }

            if (isSecondary)
            {
                secondary++;
            }

            if (isSupplementary)
            {
                supplementary++;
            }

            if (isMultiSegment && isProperlyAligned)
            {
                properlyPaired++;
            }

            // Singleton: mapped AND not part of a properly-aligned pair (includes both unpaired and
            // paired-end reads where the mate did not map)
            if (!isUnmapped && !isProperlyAligned)
            {
                singleton++;
            }

            // Read length
            var seqLen = a.Sequence == "*" ? 0 : a.Sequence.Length;
            if (seqLen > 0)
            {
                lengthDist[seqLen] = lengthDist.GetValueOrDefault(seqLen) + 1;
            }
        }

        return new AlignmentSummaryStats
        {
            TotalReads = total,
            MappedReads = mapped,
            UnmappedReads = unmapped,
            ProperlyPairedReads = properlyPaired,
            SingletonReads = singleton,
            DuplicateReads = duplicate,
            SecondaryAlignmentReads = secondary,
            SupplementaryAlignmentReads = supplementary,
            AverageMappingQuality = mappedForMapq == 0
                ? 0.0
                : mapqSum / mappedForMapq,
            ReadLengthDistribution = lengthDist,
            ErrorRate = mappedBases == 0
                ? 0.0
                : (double)nmSum / mappedBases
        };
    }
}
