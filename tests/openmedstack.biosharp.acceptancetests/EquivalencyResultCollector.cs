namespace OpenMedStack.BioSharp.AcceptanceTests;

using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

/// <summary>
/// Thread-safe store for equivalency comparison results gathered during a test run.
/// A static collector ensures results survive across multiple test scenarios and are
/// available to the <c>[AfterTestRun]</c> hook that writes the markdown report.
/// </summary>
public static class EquivalencyResultCollector
{
    private static readonly ConcurrentQueue<EquivalencyResult> _results = new();

    /// <summary>Records one comparison result.</summary>
    public static void Add(EquivalencyResult result) => _results.Enqueue(result);

    /// <summary>Returns a snapshot of all collected results in insertion order.</summary>
    public static IReadOnlyList<EquivalencyResult> GetAll() => _results.ToArray();

    /// <summary>Writes a markdown equivalency report to <paramref name="outputPath"/>.</summary>
    public static void WriteMarkdownReport(string outputPath)
    {
        var allResults = GetAll();

        var sb = new StringBuilder();

        sb.AppendLine("# BioSharp Tool Equivalency Report");
        sb.AppendLine();
        sb.AppendLine($"**Generated:** {System.DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        sb.AppendLine($"**Platform:** {System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier}");
        sb.AppendLine($"**Architecture:** {System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture}");
        sb.AppendLine();

        if (allResults.Count == 0)
        {
            sb.AppendLine("> No equivalency results were collected.  All scenarios may have been skipped because");
            sb.AppendLine("> the required external tools were not available on this platform.");
            File.WriteAllText(outputPath, sb.ToString());
            return;
        }

        // ── Summary table ─────────────────────────────────────────────────────
        var categories = allResults.Select(r => r.Category).Distinct().OrderBy(c => c).ToList();
        var passed = allResults.Count(r => r.Passed);
        var failed = allResults.Count(r => !r.Passed);

        sb.AppendLine("## Summary");
        sb.AppendLine();
        sb.AppendLine($"| Category | Passed | Failed | Total |");
        sb.AppendLine($"|---|---:|---:|---:|");

        foreach (var cat in categories)
        {
            var catResults = allResults.Where(r => r.Category == cat).ToList();
            sb.AppendLine($"| {cat} | {catResults.Count(r => r.Passed)} | {catResults.Count(r => !r.Passed)} | {catResults.Count} |");
        }

        sb.AppendLine($"| **TOTAL** | **{passed}** | **{failed}** | **{allResults.Count}** |");
        sb.AppendLine();

        if (failed > 0)
        {
            sb.AppendLine("> **WARNING:** One or more equivalency checks FAILED.  ");
            sb.AppendLine("> BioSharp results deviated from the external tool reference beyond the allowed tolerance.");
            sb.AppendLine();
        }
        else
        {
            sb.AppendLine("> All equivalency checks PASSED.");
            sb.AppendLine();
        }

        // ── Per-category detail tables ─────────────────────────────────────────
        foreach (var cat in categories)
        {
            var catResults = allResults.Where(r => r.Category == cat).ToList();
            sb.AppendLine($"---");
            sb.AppendLine();
            sb.AppendLine($"## {cat}");
            sb.AppendLine();
            sb.AppendLine("| External Tool | Parameters | Metric | BioSharp | External | Delta | Tolerance | Result |");
            sb.AppendLine("|---|---|---|---:|---:|---:|---:|:---:|");

            foreach (var r in catResults)
            {
                var delta = r.ExternalValue == 0
                    ? (r.BioSharpValue == 0 ? 0.0 : 100.0)
                    : System.Math.Abs(r.BioSharpValue - r.ExternalValue) / r.ExternalValue * 100.0;

                var resultEmoji = r.Passed ? "✅ PASS" : "❌ FAIL";
                sb.AppendLine(
                    $"| {r.ExternalTool} | {r.Parameters} | {r.Metric} " +
                    $"| {r.BioSharpValue:F1} | {r.ExternalValue:F1} " +
                    $"| {delta:F1}% | ±{r.TolerancePct:F0}% " +
                    $"| {resultEmoji} |");
            }

            sb.AppendLine();
        }

        // ── Methodology note ───────────────────────────────────────────────────
        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine("## Methodology");
        sb.AppendLine();
        sb.AppendLine("All equivalency scenarios use **identical synthetic input data** generated");
        sb.AppendLine("deterministically (random seed 42) so results are reproducible.");
        sb.AppendLine();
        sb.AppendLine("- **Alignment:** counted SAM records where flag bit 4 (unmapped) is not set.");
        sb.AppendLine("- **Adapter trimming:** reads surviving the trim + minimum-length filter.");
        sb.AppendLine("- **QC metrics:** total read count and mean per-base Phred quality.");
        sb.AppendLine("- **Variant calling:** Jaccard overlap of 1-based SNP positions called by each tool.");
        sb.AppendLine();
        sb.AppendLine("Tolerances reflect the expected range of algorithmic differences (seeding");
        sb.AppendLine("strategy, partial overlap scoring, genotype model) rather than bugs.");

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        File.WriteAllText(outputPath, sb.ToString());
    }
}

/// <summary>One tool-equivalency comparison result.</summary>
public sealed record EquivalencyResult(
    string Category,
    string ExternalTool,
    string Parameters,
    string Metric,
    double BioSharpValue,
    double ExternalValue,
    double TolerancePct,
    bool Passed);
