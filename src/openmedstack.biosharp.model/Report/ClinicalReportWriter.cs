using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using OpenMedStack.BioSharp.Model.Alignment;

namespace OpenMedStack.BioSharp.Model.Report;

/// <summary>
/// Produces JSON and HTML clinical variant reports from annotated variant records.
/// </summary>
public static class ClinicalReportWriter
{
    /// <summary>
    /// Generates a machine-readable JSON report.
    /// Variants are sorted by chromosome then position.
    /// </summary>
    public static string WriteJson(
        IEnumerable<VariantAnnotation> variants,
        ClinicalReportMetadata metadata,
        QcSummary? qcSummary = null)
    {
        var sorted = variants
            .OrderBy(v => v.Chromosome, StringComparer.OrdinalIgnoreCase)
            .ThenBy(v => v.Position)
            .ToList();

        var report = new ClinicalReport
        {
            SampleName = metadata.SampleName,
            RunDate = metadata.RunDate.ToString("O"),
            PipelineVersion = metadata.PipelineVersion,
            ReferenceGenome = metadata.ReferenceGenome,
            LabName = metadata.LabName,
            Operator = metadata.Operator,
            QcMetrics = qcSummary,
            Variants = sorted.Select(v => new VariantReport
            {
                Chromosome = v.Chromosome,
                Position = v.Position,
                Reference = v.Reference,
                Alternate = v.Alternate,
                HgvsNotation = string.IsNullOrEmpty(v.HgvsNotation) ? v.HgvsCoding : v.HgvsNotation,
                Consequence = v.Consequence.ToString(),
                ClinicalSignificance = v.ClinicalSignificance,
                AffectedGene = v.AffectedGene ?? "",
                HgvsProtein = v.HgvsProtein
            }).ToArray()
        };

        return JsonSerializer.Serialize(report, ReportJsonContext.Default.ClinicalReport);
    }

    /// <summary>
    /// Generates a human-readable HTML report (no JavaScript, renders in any browser).
    /// Variants are sorted by chromosome then position.
    /// </summary>
    public static string WriteHtml(
        IEnumerable<VariantAnnotation> variants,
        ClinicalReportMetadata metadata,
        QcSummary? qcSummary = null)
    {
        var sorted = variants
            .OrderBy(v => v.Chromosome, StringComparer.OrdinalIgnoreCase)
            .ThenBy(v => v.Position)
            .ToList();

        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html>");
        sb.AppendLine("<head>");
        sb.AppendLine("  <meta charset='utf-8'>");
        sb.AppendLine($"  <title>Clinical Variant Report – {EscapeHtml(metadata.SampleName)}</title>");
        sb.AppendLine("  <style>");
        sb.AppendLine("    body { font-family: Arial, sans-serif; margin: 20px; }");
        sb.AppendLine("    table { border-collapse: collapse; width: 100%; }");
        sb.AppendLine("    th, td { border: 1px solid #ccc; padding: 6px 10px; text-align: left; }");
        sb.AppendLine("    th { background: #f0f0f0; }");
        sb.AppendLine("    .pathogenic { color: red; }");
        sb.AppendLine("    .benign { color: green; }");
        sb.AppendLine("    .vus { color: orange; }");
        sb.AppendLine("  </style>");
        sb.AppendLine("</head>");
        sb.AppendLine("<body>");

        // Header
        sb.AppendLine($"<h1>Clinical Variant Report</h1>");
        sb.AppendLine("<table>");
        sb.AppendLine($"<tr><th>Sample</th><td>{EscapeHtml(metadata.SampleName)}</td></tr>");
        sb.AppendLine($"<tr><th>Run Date</th><td>{metadata.RunDate:yyyy-MM-dd HH:mm:ss} UTC</td></tr>");
        sb.AppendLine($"<tr><th>Pipeline Version</th><td>{EscapeHtml(metadata.PipelineVersion)}</td></tr>");
        sb.AppendLine($"<tr><th>Reference Genome</th><td>{EscapeHtml(metadata.ReferenceGenome)}</td></tr>");
        sb.AppendLine("</table>");

        // QC metrics section
        if (qcSummary != null)
        {
            sb.AppendLine("<h2>QC Metrics</h2>");
            sb.AppendLine("<table>");
            sb.AppendLine("<tr><th>Metric</th><th>Value</th></tr>");
            sb.AppendLine($"<tr><td>Mean Coverage</td><td>{qcSummary.MeanCoverage:F1}x</td></tr>");
            sb.AppendLine($"<tr><td>Mapping Rate</td><td>{qcSummary.MappingRate:F2}</td></tr>");
            sb.AppendLine($"<tr><td>Duplicate Rate</td><td>{qcSummary.DuplicateRate:F2}</td></tr>");
            sb.AppendLine("</table>");
        }

        // Variants table
        sb.AppendLine("<h2>Variants</h2>");
        if (sorted.Count == 0)
        {
            sb.AppendLine("<p>No variants called.</p>");
        }
        else
        {
            sb.AppendLine("<table>");
            sb.AppendLine("<tr><th>Chromosome</th><th>Position</th><th>Ref</th><th>Alt</th>" +
                "<th>HGVS</th><th>Consequence</th><th>Clinical Significance</th><th>Gene</th></tr>");

            foreach (var v in sorted)
            {
                var clinSig = v.ClinicalSignificance;
                var cssClass = clinSig.Contains("Pathogenic", StringComparison.OrdinalIgnoreCase)
                    ? "pathogenic"
                    : clinSig.Contains("Benign", StringComparison.OrdinalIgnoreCase)
                        ? "benign"
                        : clinSig.Contains("VUS", StringComparison.OrdinalIgnoreCase) ||
                        clinSig.Contains("Uncertain", StringComparison.OrdinalIgnoreCase)
                            ? "vus"
                            : string.Empty;

                var hgvs = string.IsNullOrEmpty(v.HgvsNotation) ? v.HgvsCoding ?? string.Empty : v.HgvsNotation;

                sb.AppendLine(
                    $"<tr>" +
                    $"<td>{EscapeHtml(v.Chromosome)}</td>" +
                    $"<td>{v.Position}</td>" +
                    $"<td>{EscapeHtml(v.Reference)}</td>" +
                    $"<td>{EscapeHtml(v.Alternate)}</td>" +
                    $"<td>{EscapeHtml(hgvs)}</td>" +
                    $"<td>{v.Consequence}</td>" +
                    $"<td class='{cssClass}'>{EscapeHtml(clinSig)}</td>" +
                    $"<td>{EscapeHtml(v.AffectedGene)}</td>" +
                    $"</tr>");
            }

            sb.AppendLine("</table>");
        }

        sb.AppendLine("</body>");
        sb.AppendLine("</html>");
        return sb.ToString();
    }

    private static string EscapeHtml(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        return text
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;")
            .Replace("'", "&#39;");
    }
}
