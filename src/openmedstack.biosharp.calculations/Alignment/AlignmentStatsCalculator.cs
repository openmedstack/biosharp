using System.Linq;
using OpenMedStack.BioSharp.Model.Alignment;

namespace OpenMedStack.BioSharp.Calculations.Alignment;

using System.Collections.Generic;
using Io.Sam;

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
                var nmTag = Enumerable.FirstOrDefault(a.Tags, tag => tag.Key == "NM");
                if (nmTag is { Value: int nm })
                {
                    nmSum += nm;
                    mappedBases += a.Sequence.Length;
                }
                else if (nmTag?.Key != null && int.TryParse(nmTag.Value.ToString(), out var nmParsed))
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
