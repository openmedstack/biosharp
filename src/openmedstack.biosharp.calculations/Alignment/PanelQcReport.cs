using OpenMedStack.BioSharp.Model.Alignment;

namespace OpenMedStack.BioSharp.Calculations.Alignment;

using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Io.Sam;
using Model;

/// <summary>
/// Complete panel QC report composing FastQC metrics, alignment statistics, and coverage metrics.
/// Produces pass/fail/warn outcomes against configurable thresholds.
/// </summary>
public sealed class PanelQcReport
{
    // ── Raw metrics ───────────────────────────────────────────────────────────
    public double MeanCoverage { get; init; }
    public double MappingRate { get; init; }
    public double DuplicateRate { get; init; }
    public double Q30Fraction { get; init; }
    public long TotalReads { get; init; }
    public long MappedReads { get; init; }
    public long DuplicateReads { get; init; }
    public long TotalBases { get; init; }

    // ── Threshold values (for reporting) ─────────────────────────────────────
    public double ThresholdMeanCoverage { get; init; }
    public double ThresholdMappingRate { get; init; }
    public double ThresholdDuplicateRate { get; init; }
    public double ThresholdQ30Fraction { get; init; }

    // ── Status ────────────────────────────────────────────────────────────────
    public QcStatus MeanCoverageStatus { get; init; }
    public QcStatus MappingRateStatus { get; init; }
    public QcStatus DuplicateRateStatus { get; init; }
    public QcStatus Q30FractionStatus { get; init; }

    /// <summary>Overall status: worst of the individual metric statuses.</summary>
    public QcStatus OverallStatus
    {
        get
        {
            return new[] { MeanCoverageStatus, MappingRateStatus, DuplicateRateStatus, Q30FractionStatus }
                .OrderByDescending(s => (int)s)
                .First();
        }
    }

    /// <summary>
    /// Generates a complete panel QC report.
    /// </summary>
    public static async Task<PanelQcReport> Generate(
        IAsyncEnumerable<Sequence> fastqReads,
        IReadOnlyList<AlignmentSection> bamReads,
        int referenceLength,
        PanelQcThresholds? thresholds = null,
        IReadOnlyList<BedInterval>? targetIntervals = null,
        CancellationToken cancellationToken = default)
    {
        thresholds ??= new PanelQcThresholds();

        // Compute all three sub-reports in parallel
        var fastqTask = FastQQualityReport.Compute(fastqReads, cancellationToken: cancellationToken);
        var alignStats = AlignmentStatsCalculator.Compute(bamReads);
        var coverage = CoverageCalculator.Compute(bamReads, referenceLength, targetIntervals);

        var fastqReport = await fastqTask.ConfigureAwait(false);

        // Q30 fraction from per-sequence quality histogram
        var q30Fraction = ComputeQ30Fraction(fastqReport);

        // Duplicate rate from alignment stats
        var dupRate = alignStats.TotalReads == 0
            ? 0.0
            : (double)alignStats.DuplicateReads / alignStats.TotalReads;

        return new PanelQcReport
        {
            MeanCoverage = coverage.MeanDepth,
            MappingRate = alignStats.MappingRate,
            DuplicateRate = dupRate,
            Q30Fraction = q30Fraction,
            TotalReads = alignStats.TotalReads,
            MappedReads = alignStats.MappedReads,
            DuplicateReads = alignStats.DuplicateReads,
            TotalBases = fastqReport.TotalBases,

            ThresholdMeanCoverage = thresholds.MinMeanCoverage,
            ThresholdMappingRate = thresholds.MinMappingRate,
            ThresholdDuplicateRate = thresholds.MaxDuplicateRate,
            ThresholdQ30Fraction = thresholds.MinQ30Fraction,

            MeanCoverageStatus = EvalHigherIsBetter(
                coverage.MeanDepth, thresholds.MinMeanCoverage, thresholds.WarnMeanCoverage),
            MappingRateStatus = EvalHigherIsBetter(
                alignStats.MappingRate, thresholds.MinMappingRate, thresholds.WarnMappingRate),
            DuplicateRateStatus = EvalLowerIsBetter(
                dupRate, thresholds.MaxDuplicateRate, thresholds.WarnDuplicateRate),
            Q30FractionStatus = EvalHigherIsBetter(
                q30Fraction, thresholds.MinQ30Fraction, thresholds.WarnQ30Fraction)
        };
    }

    /// <summary>Renders the report as an HTML page (no JavaScript).</summary>
    public string ToHtml()
    {
        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html><head><meta charset='utf-8'><title>Panel QC Report</title></head><body>");
        sb.AppendLine("<h1>Panel QC Report</h1>");
        sb.AppendLine("<table>");
        sb.AppendLine("<tr><th>Metric</th><th>Value</th><th>Threshold</th><th>Status</th></tr>");

        AppendRow(sb, "Mean Coverage", $"{MeanCoverage:F1}x",
            $"≥{ThresholdMeanCoverage:F1}x", MeanCoverageStatus);
        AppendRow(sb, "Mapping Rate", $"{MappingRate:P1}",
            $"≥{ThresholdMappingRate:P1}", MappingRateStatus);
        AppendRow(sb, "Duplicate Rate", $"{DuplicateRate:P1}",
            $"≤{ThresholdDuplicateRate:P1}", DuplicateRateStatus);
        AppendRow(sb, "Q30 Fraction", $"{Q30Fraction:P1}",
            $"≥{ThresholdQ30Fraction:P1}", Q30FractionStatus);

        sb.AppendLine("</table>");
        sb.AppendLine($"<p>Overall: <strong>{OverallStatus}</strong></p>");
        sb.AppendLine("</body></html>");
        return sb.ToString();
    }

    private static void AppendRow(StringBuilder sb, string metric, string value, string threshold, QcStatus status)
    {
        var color = status switch
        {
            QcStatus.Pass => "green",
            QcStatus.Warn => "orange",
            _ => "red"
        };
        sb.AppendLine(
            $"<tr><td>{metric}</td><td>{value}</td><td>{threshold}</td><td style='color:{color}'>{status}</td></tr>");
    }

    private static double ComputeQ30Fraction(FastQReport fastqReport)
    {
        long totalReads = 0;
        long q30Reads = 0;
        foreach (var (q, count) in fastqReport.PerSequenceQualityHistogram)
        {
            totalReads += count;
            if (q >= 30)
            {
                q30Reads += count;
            }
        }

        return totalReads == 0 ? 0.0 : (double)q30Reads / totalReads;
    }

    private static QcStatus EvalHigherIsBetter(double value, double passThreshold, double warnThreshold)
    {
        if (value >= passThreshold)
        {
            return QcStatus.Pass;
        }

        return value >= warnThreshold ? QcStatus.Warn : QcStatus.Fail;
    }

    private static QcStatus EvalLowerIsBetter(double value, double passThreshold, double warnThreshold)
    {
        if (value <= passThreshold)
        {
            return QcStatus.Pass;
        }

        return value <= warnThreshold ? QcStatus.Warn : QcStatus.Fail;
    }
}
