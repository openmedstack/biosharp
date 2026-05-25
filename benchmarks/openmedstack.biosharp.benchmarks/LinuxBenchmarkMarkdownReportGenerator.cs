namespace OpenMedStack.BioSharp.Benchmarks;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

internal static class LinuxBenchmarkMarkdownReportGenerator
{
    private static readonly ReportSection[] DefaultSections =
    [
        new("Alignment", "Alignment Benchmarks", "Comparison of BioSharp alignment methods vs BWA/BWA-MEM2 for short read alignment.", "csharp-linux-alignment.csv"),
        new("VariantCalling", "Variant Calling Benchmarks", "Comparison of BioSharp variant calling vs FreeBayes and the samtools/bcftools pipeline.", "csharp-linux-variant-calling.csv"),
        new("BCL", "BCL Conversion Benchmarks", "Three-way comparison of BioSharp BCL conversion vs bcl-convert and bcl2fastq.", "csharp-linux-bcl.csv"),
        new("Fastq", "FASTQ Processing Benchmarks", "Comparison of BioSharp FASTQ QC and trimming vs external tools.", "csharp-linux-fastq.csv"),
        new("Coverage", "Coverage and Duplicate Marking Benchmarks", "Comparison of BioSharp coverage / duplicate marking vs SAMtools subprocesses.", "csharp-linux-coverage-dup.csv"),
        new("RepeatMasking", "Repeat Masking Benchmarks", "Comparison of BioSharp repeat masking vs RepeatMasker and TRF.", "csharp-linux-repeatmask.csv")
    ];

    public static int Run(string[] args)
    {
        var resultsDir = Path.GetFullPath("benchmark-results");
        var toolVersionsPath = Path.Combine(resultsDir, "tool-versions-linux.txt");
        var outputPath = Path.Combine(resultsDir, "benchmark-results-linux.md");

        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--results-dir" when index + 1 < args.Length:
                    resultsDir = Path.GetFullPath(args[++index]);
                    break;
                case "--tool-versions" when index + 1 < args.Length:
                    toolVersionsPath = Path.GetFullPath(args[++index]);
                    break;
                case "--output" when index + 1 < args.Length:
                    outputPath = Path.GetFullPath(args[++index]);
                    break;
            }
        }

        var markdown = BuildMarkdown(resultsDir, toolVersionsPath);
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? ".");
        File.WriteAllText(outputPath, markdown, Encoding.UTF8);
        Console.WriteLine($"Markdown report written to: {outputPath}");
        return 0;
    }

    private static string BuildMarkdown(string resultsDir, string toolVersionsPath)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# BioSharp Linux Container Benchmark Results");
        builder.AppendLine();
        builder.AppendLine($"**Generated**: {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}  ");
        builder.AppendLine($"**Platform**: {RuntimeInformation.OSDescription.Trim()} ({RuntimeInformation.ProcessArchitecture})  ");
        builder.AppendLine("**Build Configuration**: .NET 10, Release, Self-contained, ReadyToRun, OptimizationPreference=Speed  ");
        builder.AppendLine($"**Results Directory**: `{resultsDir}`");
        builder.AppendLine();

        AppendToolVersions(builder, toolVersionsPath);

        builder.AppendLine("## Benchmark Results Summary");
        builder.AppendLine();

        var sectionNumber = 1;
        foreach (var section in DefaultSections)
        {
            AppendSection(builder, resultsDir, section, sectionNumber++);
        }

        AppendConclusions(builder, resultsDir);
        return builder.ToString();
    }

    private static void AppendToolVersions(StringBuilder builder, string toolVersionsPath)
    {
        builder.AppendLine("## Tool Versions");
        builder.AppendLine();
        builder.AppendLine("| Tool | Version | Status |");
        builder.AppendLine("|------|---------|--------|");

        var records = LoadToolVersions(toolVersionsPath);
        if (records.Count == 0)
        {
            builder.AppendLine("| n/a | n/a | Tool version file not found |");
            builder.AppendLine();
            return;
        }

        foreach (var record in records)
        {
            builder.AppendLine($"| {EscapePipes(record.Tool)} | {EscapePipes(record.Version)} | {EscapePipes(record.Status)} |");
        }

        builder.AppendLine();
    }

    private static List<ToolVersionRecord> LoadToolVersions(string toolVersionsPath)
    {
        if (!File.Exists(toolVersionsPath))
        {
            return [];
        }

        var records = new List<ToolVersionRecord>();
        string? currentTool = null;
        var currentValue = new StringBuilder();

        foreach (var rawLine in File.ReadLines(toolVersionsPath))
        {
            var line = rawLine.TrimEnd();
            var separator = line.IndexOf(':');
            if (separator > 0)
            {
                FlushToolRecord(records, currentTool, currentValue.ToString());
                currentTool = line[..separator].Trim();
                currentValue.Clear();
                currentValue.Append(line[(separator + 1)..].Trim());
            }
            else if (currentTool != null)
            {
                var continuation = line.Trim();
                if (continuation.Length > 0)
                {
                    if (currentValue.Length > 0)
                    {
                        currentValue.Append(' ');
                    }

                    currentValue.Append(continuation);
                }
            }
        }

        FlushToolRecord(records, currentTool, currentValue.ToString());
        return records;
    }

    private static void FlushToolRecord(List<ToolVersionRecord> records, string? tool, string value)
    {
        if (string.IsNullOrWhiteSpace(tool))
        {
            return;
        }

        var normalizedValue = string.IsNullOrWhiteSpace(value) ? "not available" : value.Trim();
        var normalizedLower = normalizedValue.ToLowerInvariant();
        var status = normalizedLower.Contains("not available", StringComparison.Ordinal)
            ? "Not available"
            : "Available";
        records.Add(new ToolVersionRecord(tool, normalizedValue, status));
    }

    private static void AppendSection(StringBuilder builder, string resultsDir, ReportSection section, int sectionNumber)
    {
        var path = Path.Combine(resultsDir, section.FileName);
        builder.AppendLine($"### {sectionNumber}. {section.Title}");
        builder.AppendLine();
        builder.AppendLine(section.Description);
        builder.AppendLine();

        if (!File.Exists(path))
        {
            builder.AppendLine($"**Status**: Result file `{section.FileName}` was not found.");
            builder.AppendLine();
            return;
        }

        var rows = LoadRows(path);
        rows = section.Kind == "BCL" ? EnsureExpectedBclRows(rows) : rows;
        if (rows.Count == 0)
        {
            builder.AppendLine($"**Status**: Result file `{section.FileName}` did not contain any benchmark rows.");
            builder.AppendLine();
            return;
        }

        builder.AppendLine("| Method | Parameters | Mean (ms) | Result | Status |");
        builder.AppendLine("|--------|------------|-----------|--------|--------|");
        foreach (var row in rows)
        {
            var mean = row.MeanMs.HasValue ? row.MeanMs.Value.ToString("0.###", CultureInfo.InvariantCulture) : "n/a";
            var result = string.IsNullOrWhiteSpace(row.LastResult) ? "n/a" : row.LastResult!;
            var status = row.Status == "Failed" && !string.IsNullOrWhiteSpace(row.Error)
                ? $"Failed: {row.Error}"
                : row.Status;
            builder.AppendLine($"| {EscapePipes(row.Method)} | {EscapePipes(DisplayParameters(row.Parameters))} | {mean} | {EscapePipes(result)} | {EscapePipes(status)} |");
        }

        builder.AppendLine();

        var evaluation = section.Kind switch
        {
            "Alignment" => EvaluateAlignment(rows),
            "VariantCalling" => EvaluateVariantCalling(rows),
            "BCL" => EvaluateBcl(rows),
            "Fastq" => EvaluateFastq(rows),
            "Coverage" => EvaluateCoverage(rows),
            "RepeatMasking" => EvaluateRepeatMasking(rows),
            _ => null
        };

        if (!string.IsNullOrWhiteSpace(evaluation))
        {
            builder.AppendLine($"**Evaluation**: {evaluation}");
            builder.AppendLine();
        }
    }

    private static string? EvaluateAlignment(IReadOnlyList<ResultRow> rows)
    {
        var comparisons = new List<string>();
        foreach (var parameter in rows.Select(row => row.Parameters).Distinct(StringComparer.Ordinal))
        {
            var parameterRows = rows.Where(row => row.Parameters == parameter).ToArray();
            var baseline = FindMeasured(parameterRows, "bwa-mem (subprocess)");
            var bestBioSharp = parameterRows
                .Where(row => row.Status == "Measured" && row.MeanMs.HasValue && row.Method.StartsWith("BioSharp", StringComparison.Ordinal))
                .OrderBy(row => row.MeanMs)
                .FirstOrDefault();
            if (baseline == null || bestBioSharp == null)
            {
                continue;
            }

            comparisons.Add($"For {DisplayParameters(parameter)}, the best measured BioSharp path was {bestBioSharp.Method} at {FormatMs(bestBioSharp.MeanMs)} versus bwa-mem at {FormatMs(baseline.MeanMs)}, so BioSharp was {FormatRatio(baseline.MeanMs!.Value / bestBioSharp.MeanMs!.Value, fasterWhenAboveOne: true)}.");
        }

        return comparisons.Count == 0 ? "No measured BioSharp vs bwa-mem alignment baseline was available." : string.Join(" ", comparisons);
    }

    private static string? EvaluateVariantCalling(IReadOnlyList<ResultRow> rows)
    {
        var bestBioSharp = rows
            .Where(row => row.Status == "Measured" && row.MeanMs.HasValue && row.Method.StartsWith("BioSharp", StringComparison.Ordinal))
            .OrderBy(row => row.MeanMs)
            .FirstOrDefault();
        var freebayes = FindMeasured(rows, "freebayes (subprocess)");
        var samtools = FindMeasured(rows, "samtools-mpileup|bcftools (subprocess)");

        if (bestBioSharp == null)
        {
            return "No measured BioSharp variant-calling rows were available.";
        }

        var parts = new List<string> { $"The fastest measured BioSharp variant-calling row was {bestBioSharp.Method} at {FormatMs(bestBioSharp.MeanMs)}." };
        if (freebayes != null)
        {
            parts.Add($"Compared with freebayes at {FormatMs(freebayes.MeanMs)}, BioSharp was {FormatRatio(freebayes.MeanMs!.Value / bestBioSharp.MeanMs!.Value, fasterWhenAboveOne: true)}.");
        }

        if (samtools != null)
        {
            parts.Add($"Compared with the samtools/bcftools pipeline at {FormatMs(samtools.MeanMs)}, BioSharp was {FormatRatio(samtools.MeanMs!.Value / bestBioSharp.MeanMs!.Value, fasterWhenAboveOne: true)}.");
        }

        return string.Join(" ", parts);
    }

    private static string? EvaluateBcl(IReadOnlyList<ResultRow> rows)
    {
        var statements = new List<string>();
        foreach (var parameter in rows.Select(row => row.Parameters).Distinct(StringComparer.Ordinal))
        {
            var runFolder = FindMeasured(rows, "BioSharp-BCL-RunFolder+FastqWrite", parameter);
            if (runFolder == null)
            {
                continue;
            }

            var bclConvert = FindMeasured(rows, "bcl-convert (subprocess)", parameter);
            var bcl2fastq = FindMeasured(rows, "bcl2fastq (subprocess)", parameter);

            if (bclConvert == null && bcl2fastq == null)
            {
                var failures = rows.Where(row => row.Parameters == parameter && (row.Method == "bcl-convert (subprocess)" || row.Method == "bcl2fastq (subprocess)") && row.Status == "Failed").ToArray();
                if (failures.Length > 0)
                {
                    var failedTools = string.Join(" and ", failures.Select(failure => failure.Method.Replace(" (subprocess)", string.Empty, StringComparison.Ordinal)));
                    var noun = failures.Length == 1 ? "was" : "were";
                    statements.Add($"For {DisplayParameters(parameter)}, BioSharp's comparable run-folder path completed in {FormatMs(runFolder.MeanMs)}, but {failedTools} {noun} unavailable, so the 3-way external comparison could not be measured.");
                }
                else
                {
                    statements.Add($"For {DisplayParameters(parameter)}, BioSharp's comparable run-folder path completed in {FormatMs(runFolder.MeanMs)}, but no external BCL converter produced a measured row.");
                }

                continue;
            }

            var measuredExternal = new[] { bclConvert, bcl2fastq }
                .Where(row => row != null)
                .Select(row => row!)
                .OrderBy(row => row.MeanMs)
                .ToArray();
            var fastestExternal = measuredExternal.First();
            statements.Add($"For {DisplayParameters(parameter)}, BioSharp's run-folder path measured {FormatMs(runFolder.MeanMs)}. The fastest external converter was {fastestExternal.Method} at {FormatMs(fastestExternal.MeanMs)}, so BioSharp was {FormatRatio(fastestExternal.MeanMs!.Value / runFolder.MeanMs!.Value, fasterWhenAboveOne: true)}.");
        }

        return statements.Count == 0 ? "No BCL benchmark rows were available." : string.Join(" ", statements);
    }

    private static string? EvaluateFastq(IReadOnlyList<ResultRow> rows)
    {
        var statements = new List<string>();
        AppendSpecificComparison(statements, rows, "BioSharp-AdapterTrim", "cutadapt (subprocess)", "Adapter trimming");
        AppendSpecificComparison(statements, rows, "BioSharp-FastQ-QC+AdapterTrim+FastqWrite", "fastp (subprocess)", "Combined QC + trim + FASTQ writing");
        AppendSpecificComparison(statements, rows, "BioSharp-FastQ-QC", "fastqc (subprocess)", "FASTQ QC");
        return statements.Count == 0 ? "No measured FASTQ head-to-head comparisons were available." : string.Join(" ", statements);
    }

    private static string? EvaluateCoverage(IReadOnlyList<ResultRow> rows)
    {
        if (rows.All(row => row.Status == "Failed"))
        {
            return $"All coverage / duplicate-marking rows failed in this run. Representative failure: {rows.First().Error}";
        }

        var statements = new List<string>();
        AppendSpecificComparison(statements, rows, "BioSharp-CoverageCalculator", "samtools-depth (subprocess)", "Coverage calculation");
        AppendSpecificComparison(statements, rows, "BioSharp-DuplicateMarker", "samtools-markdup (subprocess)", "Duplicate marking");
        return statements.Count == 0 ? "No measured BioSharp vs SAMtools coverage / duplicate comparisons were available." : string.Join(" ", statements);
    }

    private static string? EvaluateRepeatMasking(IReadOnlyList<ResultRow> rows)
    {
        var bioSharp = rows.FirstOrDefault(row => row.Status == "Measured" && row.Method.StartsWith("BioSharp", StringComparison.Ordinal));
        if (bioSharp == null)
        {
            return rows.All(row => row.Status == "Failed")
                ? $"No repeat-masking benchmark produced a measured row. Representative failure: {rows.First().Error}"
                : "No measured BioSharp repeat-masking row was available.";
        }

        var externalMeasured = rows.Where(row => row.Status == "Measured" && !row.Method.StartsWith("BioSharp", StringComparison.Ordinal)).ToArray();
        if (externalMeasured.Length == 0)
        {
            var unavailable = rows.Where(row => row.Status == "Failed" && !row.Method.StartsWith("BioSharp", StringComparison.Ordinal)).Select(row => row.Method).ToArray();
            return unavailable.Length == 0
                ? $"BioSharp repeat masking measured {FormatMs(bioSharp.MeanMs)}, but no external repeat masker produced a measured row."
                : $"BioSharp repeat masking measured {FormatMs(bioSharp.MeanMs)}, while external tools ({string.Join(", ", unavailable)}) were unavailable in this run.";
        }

        var bestExternal = externalMeasured.OrderBy(row => row.MeanMs).First();
        return $"BioSharp repeat masking measured {FormatMs(bioSharp.MeanMs)}. The fastest external repeat masker was {bestExternal.Method} at {FormatMs(bestExternal.MeanMs)}, so BioSharp was {FormatRatio(bestExternal.MeanMs!.Value / bioSharp.MeanMs!.Value, fasterWhenAboveOne: true)}.";
    }

    private static void AppendSpecificComparison(List<string> statements, IReadOnlyList<ResultRow> rows, string bioMethod, string externalMethod, string label)
    {
        var bioSharp = FindMeasured(rows, bioMethod);
        var external = FindMeasured(rows, externalMethod);
        if (bioSharp == null || external == null)
        {
            return;
        }

        statements.Add($"{label}: {bioMethod} measured {FormatMs(bioSharp.MeanMs)} versus {externalMethod} at {FormatMs(external.MeanMs)}, so BioSharp was {FormatRatio(external.MeanMs!.Value / bioSharp.MeanMs!.Value, fasterWhenAboveOne: true)}.");
    }

    private static ResultRow? FindMeasured(IEnumerable<ResultRow> rows, string method, string? parameters = null)
    {
        return rows.FirstOrDefault(row =>
            row.Status == "Measured" &&
            row.MeanMs.HasValue &&
            string.Equals(row.Method, method, StringComparison.Ordinal) &&
            (parameters == null || string.Equals(row.Parameters, parameters, StringComparison.Ordinal)));
    }

    private static List<ResultRow> EnsureExpectedBclRows(IReadOnlyList<ResultRow> rows)
    {
        var normalized = rows.ToList();
        foreach (var parameter in normalized.Select(row => row.Parameters).Distinct(StringComparer.Ordinal).ToArray())
        {
            EnsureSyntheticFailureRow(normalized, "bcl-convert (subprocess)", parameter);
            EnsureSyntheticFailureRow(normalized, "bcl2fastq (subprocess)", parameter);
        }

        return normalized;
    }

    private static void EnsureSyntheticFailureRow(List<ResultRow> rows, string method, string parameters)
    {
        if (rows.Any(row => string.Equals(row.Method, method, StringComparison.Ordinal) && string.Equals(row.Parameters, parameters, StringComparison.Ordinal)))
        {
            return;
        }

        var toolName = method.Replace(" (subprocess)", string.Empty, StringComparison.Ordinal);
        rows.Add(new ResultRow(
            ClassName: "BclHeadToHeadBenchmarks",
            Method: method,
            Parameters: parameters,
            Status: "Failed",
            Iterations: 0,
            MeanMs: null,
            MinMs: null,
            MaxMs: null,
            LastResult: null,
            Error: $"{toolName} did not produce a row in the result CSV. Re-run the BCL benchmark on a host where {toolName} is staged if you need a full three-way measurement."));
    }

    private static void AppendConclusions(StringBuilder builder, string resultsDir)
    {
        builder.AppendLine("## Conclusions");
        builder.AppendLine();

        var conclusions = new List<string>();

        var alignmentRows = TryLoadRows(Path.Combine(resultsDir, "csharp-linux-alignment.csv"));
        var alignmentComparisons = alignmentRows
            .Select(row => row.Parameters)
            .Distinct(StringComparer.Ordinal)
            .Select(parameter =>
            {
                var baseline = FindMeasured(alignmentRows, "bwa-mem (subprocess)", parameter);
                var bestBio = alignmentRows.Where(row => row.Parameters == parameter && row.Status == "Measured" && row.MeanMs.HasValue && row.Method.StartsWith("BioSharp", StringComparison.Ordinal)).OrderBy(row => row.MeanMs).FirstOrDefault();
                return baseline != null && bestBio != null ? (double?)(baseline.MeanMs!.Value / bestBio.MeanMs!.Value) : null;
            })
            .Where(value => value.HasValue)
            .Select(value => value!.Value)
            .ToArray();
        if (alignmentComparisons.Length > 0)
        {
            conclusions.Add($"Alignment: the best measured BioSharp path ranged from {alignmentComparisons.Min():0.##}x to {alignmentComparisons.Max():0.##}x faster than bwa-mem across the measured read counts.");
        }

        var variantRows = TryLoadRows(Path.Combine(resultsDir, "csharp-linux-variant-calling.csv"));
        var bestVariantBio = variantRows.Where(row => row.Status == "Measured" && row.MeanMs.HasValue && row.Method.StartsWith("BioSharp", StringComparison.Ordinal)).OrderBy(row => row.MeanMs).FirstOrDefault();
        var freebayes = FindMeasured(variantRows, "freebayes (subprocess)");
        if (bestVariantBio != null && freebayes != null)
        {
            conclusions.Add($"Variant calling: the fastest measured BioSharp row was {freebayes.MeanMs!.Value / bestVariantBio.MeanMs!.Value:0.##}x faster than freebayes.");
        }

        var fastqRows = TryLoadRows(Path.Combine(resultsDir, "csharp-linux-fastq.csv"));
        var trimBio = FindMeasured(fastqRows, "BioSharp-AdapterTrim");
        var cutadapt = FindMeasured(fastqRows, "cutadapt (subprocess)");
        if (trimBio != null && cutadapt != null)
        {
            conclusions.Add($"FASTQ processing: BioSharp adapter trimming was {cutadapt.MeanMs!.Value / trimBio.MeanMs!.Value:0.##}x faster than cutadapt on the measured fixture.");
        }

        var bclRows = TryLoadRows(Path.Combine(resultsDir, "csharp-linux-bcl.csv"));
        var anyExternalBclMeasured = FindMeasured(bclRows, "bcl-convert (subprocess)") != null || FindMeasured(bclRows, "bcl2fastq (subprocess)") != null;
        conclusions.Add(anyExternalBclMeasured
            ? "BCL conversion: the report includes a full three-way comparison between BioSharp, bcl-convert, and bcl2fastq for every measured BCL parameter set."
            : "BCL conversion: BioSharp measured successfully, but at least one vendor BCL converter was unavailable, so some three-way rows remain informational rather than fully measured.");

        if (conclusions.Count == 0)
        {
            conclusions.Add("No measured comparisons were available to summarize.");
        }

        foreach (var conclusion in conclusions)
        {
            builder.AppendLine($"- {conclusion}");
        }

        builder.AppendLine();
    }

    private static List<ResultRow> TryLoadRows(string path)
    {
        return File.Exists(path) ? LoadRows(path) : [];
    }

    private static List<ResultRow> LoadRows(string path)
    {
        var rows = new List<ResultRow>();
        using var reader = new StreamReader(path, Encoding.UTF8);
        _ = reader.ReadLine();
        while (reader.ReadLine() is { } line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var fields = ParseCsvLine(line);
            if (fields.Count < 10)
            {
                continue;
            }

            rows.Add(new ResultRow(
                fields[0],
                fields[1],
                fields[2],
                fields[3],
                ParseInt(fields[4]),
                ParseDouble(fields[5]),
                ParseDouble(fields[6]),
                ParseDouble(fields[7]),
                fields[8],
                fields[9]));
        }

        return rows;
    }

    private static List<string> ParseCsvLine(string line)
    {
        var fields = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;

        for (var index = 0; index < line.Length; index++)
        {
            var ch = line[index];
            if (inQuotes)
            {
                if (ch == '"')
                {
                    if (index + 1 < line.Length && line[index + 1] == '"')
                    {
                        current.Append('"');
                        index++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    current.Append(ch);
                }
            }
            else
            {
                if (ch == ',')
                {
                    fields.Add(current.ToString());
                    current.Clear();
                }
                else if (ch == '"')
                {
                    inQuotes = true;
                }
                else
                {
                    current.Append(ch);
                }
            }
        }

        fields.Add(current.ToString());
        return fields;
    }

    private static int ParseInt(string value)
    {
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;
    }

    private static double? ParseDouble(string value)
    {
        return double.TryParse(value, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
    }

    private static string DisplayParameters(string parameters)
    {
        return string.IsNullOrWhiteSpace(parameters) ? "-" : parameters.Replace(";", "; ", StringComparison.Ordinal);
    }

    private static string FormatMs(double? value)
    {
        return value.HasValue ? $"{value.Value:0.###} ms" : "n/a";
    }

    private static string FormatRatio(double ratio, bool fasterWhenAboveOne)
    {
        if (ratio >= 1)
        {
            return fasterWhenAboveOne ? $"{ratio:0.##}x faster" : $"{ratio:0.##}x slower";
        }

        var inverse = 1 / ratio;
        return fasterWhenAboveOne ? $"{inverse:0.##}x slower" : $"{inverse:0.##}x faster";
    }

    private static string EscapePipes(string value)
    {
        return value.Replace("|", "\\|", StringComparison.Ordinal);
    }

    private sealed record ReportSection(string Kind, string Title, string Description, string FileName);

    private sealed record ToolVersionRecord(string Tool, string Version, string Status);

    private sealed record ResultRow(
        string ClassName,
        string Method,
        string Parameters,
        string Status,
        int Iterations,
        double? MeanMs,
        double? MinMs,
        double? MaxMs,
        string? LastResult,
        string? Error);
}