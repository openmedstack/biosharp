namespace OpenMedStack.BioSharp.Calculations.Alignment;

using System;
using System.Buffers;
using System.Collections.Generic;
using Io.Sam;
using Model;

/// <summary>
/// Base Quality Score Recalibration (BQSR).
///
/// BQSR corrects systematic biases in Illumina base quality scores by:
/// <list type="number">
///   <item><description>
///     <b>CollectCovariates</b>: scanning aligned reads against a reference to measure
///     the empirical error rate for each (reported Phred quality, read cycle,
///     dinucleotide context) combination, excluding known-variant sites.
///   </description></item>
///   <item><description>
///     <b>ApplyRecalibration</b>: replacing each base's quality with the empirical
///     Phred score derived from the recalibration table.
///   </description></item>
/// </list>
/// </summary>
public sealed class BaseQualityRecalibrator
{
    /// <summary>
    /// Collects base-error covariates by comparing aligned reads to a reference sequence.
    ///
    /// Sites that are already known to be variants are excluded via
    /// <paramref name="knownVariantPositions"/> (1-based, optional).
    /// </summary>
    /// <param name="alignments">Aligned reads with valid <c>Sequence</c> and <c>Quality</c>.</param>
    /// <param name="reference">Reference sequence (1-based positions match alignment positions).</param>
    /// <param name="knownVariantPositions">
    /// 1-based positions of known variants to skip during covariate collection.
    /// Pass null or an empty set to include all sites.
    /// </param>
    /// <returns>A <see cref="RecalibrationTable"/> ready for <see cref="ApplyRecalibration"/>.</returns>
    public static RecalibrationTable CollectCovariates(
        IEnumerable<AlignmentSection> alignments,
        Sequence reference,
        IReadOnlySet<int>? knownVariantPositions = null)
    {
        var table = new RecalibrationTable();
        var refSpan = reference.GetData().Span;

        foreach (var alignment in alignments)
        {
            if ((alignment.Flag & AlignmentSection.AlignmentFlag.SegmentUnmapped) != 0)
            {
                continue;
            }

            var seq = alignment.Sequence.AsSpan();
            var qual = alignment.Quality.AsSpan();
            var refPos = alignment.Position - 1; // convert to 0-based

            for (var cycle = 0; cycle < seq.Length; cycle++, refPos++)
            {
                if (refPos < 0 || refPos >= refSpan.Length)
                {
                    break;
                }

                var refPos1Based = refPos + 1;
                if (knownVariantPositions != null && knownVariantPositions.Contains(refPos1Based))
                {
                    continue;
                }

                var readBase = char.ToUpperInvariant(seq[cycle]);
                var refBase = char.ToUpperInvariant(refSpan[refPos]);

                // Ignore ambiguous bases
                if (refBase == 'N' || readBase == 'N')
                {
                    continue;
                }

                var reportedPhred = qual[cycle] - 33;
                if (reportedPhred < 0)
                {
                    reportedPhred = 0;
                }

                var dinuc = cycle > 0
                    ? string.Concat(char.ToUpperInvariant(seq[cycle - 1]).ToString(), readBase.ToString())
                    : "NN";

                var isError = readBase != refBase;
                table.Record(reportedPhred, cycle, dinuc, isError);
            }
        }

        return table;
    }

    /// <summary>
    /// Applies recalibration to a collection of alignments, replacing each base quality
    /// with the empirical Phred score from the <paramref name="table"/>.
    /// </summary>
    /// <param name="alignments">Alignments to recalibrate.</param>
    /// <param name="table">Recalibration table produced by <see cref="CollectCovariates"/>.</param>
    /// <returns>New <see cref="AlignmentSection"/> list with adjusted quality strings.</returns>
    public static IReadOnlyList<AlignmentSection> ApplyRecalibration(
        IEnumerable<AlignmentSection> alignments,
        RecalibrationTable table)
    {
        var result = new List<AlignmentSection>();

        foreach (var alignment in alignments)
        {
            if ((alignment.Flag & AlignmentSection.AlignmentFlag.SegmentUnmapped) != 0 ||
                string.IsNullOrEmpty(alignment.Quality))
            {
                result.Add(alignment);
                continue;
            }

            var qual = alignment.Quality.AsSpan();
            var seq = alignment.Sequence.AsSpan();

            var rentedBuffer = ArrayPool<char>.Shared.Rent(qual.Length);
            try
            {
                var newQual = rentedBuffer.AsSpan(0, qual.Length);

                for (var cycle = 0; cycle < qual.Length; cycle++)
                {
                    var reportedPhred = qual[cycle] - 33;
                    if (reportedPhred < 0)
                    {
                        reportedPhred = 0;
                    }

                    var readBase = char.ToUpperInvariant(seq[cycle]);
                    var dinuc = cycle > 0
                        ? string.Concat(char.ToUpperInvariant(seq[cycle - 1]).ToString(), readBase.ToString())
                        : "NN";

                    var empiricalPhred = table.GetEmpiricalQuality(reportedPhred, cycle, dinuc);
                    // Clamp to [0, 40] (Phred 40 is the practical maximum for Illumina)
                    empiricalPhred = Math.Clamp(empiricalPhred, 0, 40);
                    newQual[cycle] = (char)(empiricalPhred + 33);
                }

                result.Add(new AlignmentSection(
                    alignment.QName,
                    alignment.Flag,
                    alignment.ReadName,
                    alignment.Position,
                    alignment.MappingQuality,
                    alignment.Cigar,
                    alignment.ReferenceIdOfNextSegment,
                    alignment.NextPosition,
                    alignment.TemplateLength,
                    alignment.Sequence,
                    new string(newQual),
                    alignment.Tags,
                    alignment.Index));
            }
            finally
            {
                ArrayPool<char>.Shared.Return(rentedBuffer);
            }
        }

        return result;
    }
}
