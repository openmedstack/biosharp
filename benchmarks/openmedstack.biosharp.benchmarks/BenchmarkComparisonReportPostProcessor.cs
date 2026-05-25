namespace OpenMedStack.BioSharp.Benchmarks;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using BenchmarkDotNet.Attributes;

/// <summary>
/// Post-processes BenchmarkDotNet CSV exports and merges BioSharp + external-tool results
/// into a single comparison report.
///
/// Why this exists
/// ───────────────
/// A single full-lab BenchmarkDotNet run with warmup iterations for every benchmark can take a
/// long time. The intended workflow is therefore:
///
/// 1. Run focused benchmark classes independently (possibly on different machines / at different times).
/// 2. Let BenchmarkDotNet export its usual `*-report.csv` files.
/// 3. Run `merge-report` to aggregate those CSVs into one markdown + CSV summary.
///
/// The merger uses reflection over the benchmark assembly so it can enrich the raw CSV rows with:
/// - benchmark descriptions
/// - benchmark categories (`BioSharp`, `External`, `Alignment`, etc.)
/// - expected-but-missing benchmark methods
/// - external-tool availability notes
/// </summary>
public static class BenchmarkComparisonReportPostProcessor
{
    private const string DefaultResultsRelativePath = "BenchmarkDotNet.Artifacts/results";
    private const string DefaultMarkdownName = "benchmark-comparison-report.md";
    private const string DefaultSummaryCsvName = "benchmark-comparison-summary.csv";

    public static int Run(string[] args)
    {
        var options = ParseOptions(args);
        var resultsDir = options.ResultsDirectory;
        if (!Directory.Exists(resultsDir))
        {
            Console.Error.WriteLine($"Results directory not found: {resultsDir}");
            return 1;
        }

        var assembly = typeof(PipelineBenchmarks).Assembly;
        var metadataByType = LoadBenchmarkMetadata(assembly);
        var parsedRows = LoadCsvRows(resultsDir)
            .Where(row => metadataByType.ContainsKey(row.BenchmarkType))
            .Select(row => EnrichRow(row, metadataByType[row.BenchmarkType]))
            .ToList();

        var markdown = BuildMarkdownReport(parsedRows, metadataByType);
        var summaryCsv = BuildSummaryCsv(parsedRows, metadataByType);

        Directory.CreateDirectory(Path.GetDirectoryName(options.MarkdownOutputPath)!);
        File.WriteAllText(options.MarkdownOutputPath, markdown, Encoding.UTF8);
        File.WriteAllText(options.SummaryCsvOutputPath, summaryCsv, Encoding.UTF8);

        Console.WriteLine($"Merged report written to: {options.MarkdownOutputPath}");
        Console.WriteLine($"Summary CSV written to: {options.SummaryCsvOutputPath}");
        Console.WriteLine($"Merged {parsedRows.Count} benchmark rows from {resultsDir}");
        return 0;
    }

    private static PostProcessorOptions ParseOptions(string[] args)
    {
        var cwd = Directory.GetCurrentDirectory();
        var resultsDir = Path.GetFullPath(Path.Combine(cwd, DefaultResultsRelativePath));
        var markdownOutputPath = Path.Combine(resultsDir, DefaultMarkdownName);
        var summaryCsvOutputPath = Path.Combine(resultsDir, DefaultSummaryCsvName);

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--results" when i + 1 < args.Length:
                    resultsDir = Path.GetFullPath(args[++i]);
                    break;
                case "--output" when i + 1 < args.Length:
                    markdownOutputPath = Path.GetFullPath(args[++i]);
                    break;
                case "--csv-output" when i + 1 < args.Length:
                    summaryCsvOutputPath = Path.GetFullPath(args[++i]);
                    break;
            }
        }

        return new PostProcessorOptions(resultsDir, markdownOutputPath, summaryCsvOutputPath);
    }

    private static Dictionary<string, BenchmarkTypeMetadata> LoadBenchmarkMetadata(Assembly assembly)
    {
        var result = new Dictionary<string, BenchmarkTypeMetadata>(StringComparer.Ordinal);
        foreach (var type in assembly.GetTypes().Where(IsBenchmarkType))
        {
            var methods = type.GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .Where(method => method.GetCustomAttribute<BenchmarkAttribute>() != null)
                .Select(method =>
                {
                    var benchmark = method.GetCustomAttribute<BenchmarkAttribute>();
                    var categories = method.GetCustomAttributes<BenchmarkCategoryAttribute>()
                        .SelectMany(attribute => attribute.Categories)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToArray();
                    var displayName = string.IsNullOrWhiteSpace(benchmark?.Description)
                        ? method.Name
                        : benchmark!.Description!;
                    return new BenchmarkMethodMetadata(
                        MethodName: method.Name,
                        DisplayName: displayName,
                        Categories: categories,
                        Tool: ResolveTool(type, method, categories),
                        Area: ResolveArea(type, categories),
                        RequiredExternalTools: ResolveRequiredExternalTools(type, method));
                })
                .ToDictionary(item => item.MethodName, StringComparer.Ordinal);

            var parameterPropertyNames = type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Where(property => property.GetCustomAttribute<ParamsAttribute>() != null)
                .Select(property => property.Name)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();

            result[type.FullName!] = new BenchmarkTypeMetadata(
                TypeFullName: type.FullName!,
                TypeName: type.Name,
                Area: ResolveArea(type, Array.Empty<string>()),
                Methods: methods,
                ParameterPropertyNames: parameterPropertyNames);
        }

        return result;
    }

    private static bool IsBenchmarkType(Type type)
    {
        return type.IsClass && !type.IsAbstract &&
               type.GetMethods(BindingFlags.Instance | BindingFlags.Public)
                   .Any(method => method.GetCustomAttribute<BenchmarkAttribute>() != null);
    }

    private static string ResolveTool(Type type, MethodInfo method, IReadOnlyCollection<string> categories)
    {
        if (categories.Contains("External", StringComparer.OrdinalIgnoreCase))
        {
            return ResolveExternalToolLabel(method);
        }

        if (categories.Contains("BioSharp", StringComparer.OrdinalIgnoreCase))
        {
            return "BioSharp";
        }

        if (type.Name.Contains("Seeder", StringComparison.Ordinal) ||
            type.Name.Contains("FmIndex", StringComparison.Ordinal) ||
            type.Name.Contains("SmithWaterman", StringComparison.Ordinal) ||
            type.Name.Contains("AlignmentBenchmarks", StringComparison.Ordinal) ||
            type.Name.Contains("PipelineBenchmarks", StringComparison.Ordinal) ||
            type.Name.Contains("DeBruijnBenchmarks", StringComparison.Ordinal) ||
            type.Name.Contains("BclBenchmarks", StringComparison.Ordinal))
        {
            return "BioSharp";
        }

        return method.Name.StartsWith("BioSharp_", StringComparison.Ordinal) ? "BioSharp" : "Mixed";
    }

    private static string ResolveExternalToolLabel(MethodInfo method)
    {
        return method.Name switch
        {
            nameof(AlignmentHeadToHeadBenchmarks.BwaMem_Subprocess) => "BWA-MEM",
            nameof(AlignmentHeadToHeadBenchmarks.BwaMem2_Subprocess) => "BWA-MEM2",
            nameof(VariantCallingHeadToHeadBenchmarks.FreeBayes_VariantCalling) => "FreeBayes",
            nameof(VariantCallingHeadToHeadBenchmarks.SamtoolsMpileup_BcftoolsCall) => "SAMtools+BCFtools",
            nameof(BclHeadToHeadBenchmarks.BclConvert_Subprocess) => "bcl-convert",
            nameof(BclHeadToHeadBenchmarks.Bcl2Fastq_Subprocess) => "bcl2fastq",
            nameof(FastqProcessingHeadToHeadBenchmarks.FastQc_Subprocess) => "FastQC",
            nameof(FastqProcessingHeadToHeadBenchmarks.FastP_Subprocess) => "fastp",
            nameof(FastqProcessingHeadToHeadBenchmarks.Cutadapt_Subprocess) => "Cutadapt",
            nameof(CoverageAndDuplicateHeadToHeadBenchmarks.SamtoolsDepth_Subprocess) => "SAMtools depth",
            nameof(CoverageAndDuplicateHeadToHeadBenchmarks.SamtoolsMarkdup_Subprocess) => "SAMtools markdup",
            nameof(RepeatMaskingHeadToHeadBenchmarks.RepeatMasker_Subprocess) => "RepeatMasker",
            nameof(RepeatMaskingHeadToHeadBenchmarks.Trf_Subprocess) => "TRF",
            _ => "External"
        };
    }

    private static string ResolveArea(Type type, IReadOnlyCollection<string> categories)
    {
        foreach (var preferred in new[] { "Alignment", "VariantCalling", "BCL", "QualityControl", "Trimming", "Coverage", "DuplicateMarking", "RepeatMasking", "Construction", "Seeding" })
        {
            if (categories.Contains(preferred, StringComparer.OrdinalIgnoreCase))
            {
                return preferred;
            }
        }

        if (type.Name.Contains("SmithWaterman", StringComparison.Ordinal))
        {
            return "Alignment";
        }

        if (type.Name.Contains("Alignment", StringComparison.Ordinal))
        {
            return "Alignment";
        }

        if (type.Name.Contains("VariantCalling", StringComparison.Ordinal))
        {
            return "VariantCalling";
        }

        if (type.Name.Contains("Bcl", StringComparison.Ordinal))
        {
            return "BCL";
        }

        if (type.Name.Contains("Seeder", StringComparison.Ordinal) || type.Name.Contains("FmIndex", StringComparison.Ordinal))
        {
            return "Seeding";
        }

        if (type.Name.Contains("DeBruijn", StringComparison.Ordinal))
        {
            return "DeBruijn";
        }

        if (type.Name.Contains("FastqProcessing", StringComparison.Ordinal))
        {
            return "QualityControl";
        }

        if (type.Name.Contains("CoverageAndDuplicate", StringComparison.Ordinal))
        {
            return "Coverage";
        }

        if (type.Name.Contains("RepeatMasking", StringComparison.Ordinal))
        {
            return "RepeatMasking";
        }

        if (type.Name.Contains("Pipeline", StringComparison.Ordinal))
        {
            return "Pipeline";
        }

        return "Other";
    }

    private static string[] ResolveRequiredExternalTools(Type type, MethodInfo method)
    {
        if (type == typeof(AlignmentHeadToHeadBenchmarks))
        {
            return method.Name switch
            {
                nameof(AlignmentHeadToHeadBenchmarks.BwaMem_Subprocess) => ["bwa"],
                nameof(AlignmentHeadToHeadBenchmarks.BwaMem2_Subprocess) => ["bwa-mem2"],
                _ => []
            };
        }

        if (type == typeof(VariantCallingHeadToHeadBenchmarks))
        {
            return method.Name switch
            {
                nameof(VariantCallingHeadToHeadBenchmarks.FreeBayes_VariantCalling) => ["freebayes"],
                nameof(VariantCallingHeadToHeadBenchmarks.SamtoolsMpileup_BcftoolsCall) => ["samtools", "bcftools"],
                _ => []
            };
        }

        if (type == typeof(BclHeadToHeadBenchmarks))
        {
            return method.Name switch
            {
                nameof(BclHeadToHeadBenchmarks.BclConvert_Subprocess) => ["bcl-convert"],
                nameof(BclHeadToHeadBenchmarks.Bcl2Fastq_Subprocess) => ["bcl2fastq"],
                _ => []
            };
        }

        if (type == typeof(FastqProcessingHeadToHeadBenchmarks))
        {
            return method.Name switch
            {
                nameof(FastqProcessingHeadToHeadBenchmarks.FastQc_Subprocess) => ["fastqc"],
                nameof(FastqProcessingHeadToHeadBenchmarks.FastP_Subprocess) => ["fastp"],
                nameof(FastqProcessingHeadToHeadBenchmarks.Cutadapt_Subprocess) => ["cutadapt"],
                _ => []
            };
        }

        if (type == typeof(CoverageAndDuplicateHeadToHeadBenchmarks))
        {
            return method.Name switch
            {
                nameof(CoverageAndDuplicateHeadToHeadBenchmarks.SamtoolsDepth_Subprocess) => ["samtools"],
                nameof(CoverageAndDuplicateHeadToHeadBenchmarks.SamtoolsMarkdup_Subprocess) => ["samtools"],
                _ => []
            };
        }

        if (type == typeof(RepeatMaskingHeadToHeadBenchmarks))
        {
            return method.Name switch
            {
                nameof(RepeatMaskingHeadToHeadBenchmarks.RepeatMasker_Subprocess) => ["RepeatMasker"],
                nameof(RepeatMaskingHeadToHeadBenchmarks.Trf_Subprocess) => ["trf"],
                _ => []
            };
        }

        return [];
    }

    private static List<RawBenchmarkCsvRow> LoadCsvRows(string resultsDirectory)
    {
        var rows = new List<RawBenchmarkCsvRow>();
        foreach (var file in Directory.EnumerateFiles(resultsDirectory, "*-report.csv", SearchOption.TopDirectoryOnly))
        {
            var benchmarkType = Path.GetFileName(file);
            benchmarkType = benchmarkType[..^"-report.csv".Length];

            var lines = File.ReadAllLines(file);
            if (lines.Length == 0)
            {
                continue;
            }

            var headers = ParseCsvLine(lines[0]);
            for (var i = 1; i < lines.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(lines[i]))
                {
                    continue;
                }

                var values = ParseCsvLine(lines[i]);
                var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                for (var col = 0; col < headers.Count && col < values.Count; col++)
                {
                    dict[headers[col]] = values[col];
                }

                rows.Add(new RawBenchmarkCsvRow(
                    BenchmarkType: benchmarkType,
                    MethodName: dict.GetValueOrDefault("Method", string.Empty),
                    Columns: dict,
                    SourceFilePath: file));
            }
        }

        return rows;
    }

    private static BenchmarkResultRow EnrichRow(RawBenchmarkCsvRow row, BenchmarkTypeMetadata typeMetadata)
    {
        if (!typeMetadata.Methods.TryGetValue(row.MethodName, out var methodMetadata))
        {
            methodMetadata = typeMetadata.Methods.Values.FirstOrDefault(method =>
                                 string.Equals(method.DisplayName, row.MethodName, StringComparison.Ordinal))
                             ?? new BenchmarkMethodMetadata(row.MethodName, row.MethodName, [], "Unknown", typeMetadata.Area, []);
        }

        var meanText = row.Columns.GetValueOrDefault("Mean", string.Empty);
        var meanNs = TryParseDurationToNanoseconds(meanText, out var parsedMeanNs) ? (double?)parsedMeanNs : null;
        var parameterText = BuildParameterText(row.Columns, typeMetadata.ParameterPropertyNames);
        var unavailable = methodMetadata.RequiredExternalTools.Length > 0 &&
                          methodMetadata.RequiredExternalTools.Any(tool => !ExternalProcess.IsAvailable(tool));

        return new BenchmarkResultRow(
            BenchmarkType: row.BenchmarkType,
            BenchmarkTypeName: typeMetadata.TypeName,
            MethodName: row.MethodName,
            DisplayName: methodMetadata.DisplayName,
            ParameterText: parameterText,
            Area: methodMetadata.Area,
            Tool: methodMetadata.Tool,
            Categories: methodMetadata.Categories,
            MeanText: meanText,
            MeanNanoseconds: meanNs,
            ErrorText: row.Columns.GetValueOrDefault("Error", string.Empty),
            StdDevText: row.Columns.GetValueOrDefault("StdDev", string.Empty),
            AllocatedText: row.Columns.GetValueOrDefault("Allocated", string.Empty),
            Job: row.Columns.GetValueOrDefault("Job", string.Empty),
            RequiredExternalTools: methodMetadata.RequiredExternalTools,
            IsUnavailableOnThisMachine: unavailable,
            SourceFilePath: row.SourceFilePath);
    }

    private static string BuildMarkdownReport(
        IReadOnlyList<BenchmarkResultRow> rows,
        IReadOnlyDictionary<string, BenchmarkTypeMetadata> metadataByType)
    {
        var sb = new StringBuilder();
        var now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture);
        sb.AppendLine("# Benchmark Comparison Report");
        sb.AppendLine();
        sb.AppendLine($"Generated: {now}");
        sb.AppendLine();
        sb.AppendLine("This report merges BenchmarkDotNet CSV exports into one comparison view.");
        sb.AppendLine();
        sb.AppendLine("## Summary");
        sb.AppendLine();
        sb.AppendLine($"- Parsed benchmark rows: {rows.Count}");
        sb.AppendLine($"- Benchmark classes seen: {rows.Select(row => row.BenchmarkType).Distinct().Count()}");
        sb.AppendLine($"- Results files parsed: {rows.Select(row => row.SourceFilePath).Distinct().Count()}");
        sb.AppendLine();

        foreach (var section in BuildSectionOrder(metadataByType))
        {
            if (!metadataByType.TryGetValue(section, out var typeMetadata))
            {
                continue;
            }

            var sectionRows = rows.Where(row => row.BenchmarkType == section).ToList();
            var matchedMethods = new HashSet<string>(
                sectionRows.Select(row => ResolveReflectedMethodName(typeMetadata, row.MethodName)).Where(name => !string.IsNullOrEmpty(name))!,
                StringComparer.Ordinal);
            var missingMethods = typeMetadata.Methods.Keys
                .Where(method => !matchedMethods.Contains(method))
                .OrderBy(method => method, StringComparer.Ordinal)
                .ToList();

            if (sectionRows.Count == 0 && missingMethods.Count == 0)
            {
                continue;
            }

            sb.AppendLine($"## {typeMetadata.TypeName}");
            sb.AppendLine();
            sb.AppendLine($"Area: `{typeMetadata.Area}`");
            sb.AppendLine();

            if (sectionRows.Count > 0)
            {
                var comparableRows = sectionRows.Where(row => !row.IsUnavailableOnThisMachine && row.MeanNanoseconds.HasValue).ToList();
                var fastestNs = comparableRows.Count > 0 ? comparableRows.Min(row => row.MeanNanoseconds!.Value) : (double?)null;
                var bestBioSharpNs = comparableRows.Where(row => row.Tool == "BioSharp")
                    .Select(row => row.MeanNanoseconds!.Value)
                    .DefaultIfEmpty()
                    .Min();
                var hasBioSharp = comparableRows.Any(row => row.Tool == "BioSharp");
                var bestBioSharp = hasBioSharp ? bestBioSharpNs : (double?)null;

                sb.AppendLine("| Tool | Benchmark | Mean | Error | Allocated | vs fastest | vs best BioSharp | Notes |");
                sb.AppendLine("|---|---|---:|---:|---:|---:|---:|---|");
                foreach (var row in sectionRows.OrderBy(row => row.IsUnavailableOnThisMachine).ThenBy(row => row.MeanNanoseconds ?? double.MaxValue))
                {
                    var vsFastest = row.IsUnavailableOnThisMachine || !row.MeanNanoseconds.HasValue || fastestNs is null
                        ? "—"
                        : FormatRatio(row.MeanNanoseconds.Value / fastestNs.Value);
                    var vsBestBioSharpText = row.IsUnavailableOnThisMachine || !row.MeanNanoseconds.HasValue || bestBioSharp is null
                        ? "—"
                        : FormatRatio(row.MeanNanoseconds.Value / bestBioSharp.Value);
                    var notes = row.IsUnavailableOnThisMachine
                        ? $"Tool unavailable on this machine: {string.Join(", ", row.RequiredExternalTools)}"
                        : string.Empty;

                    sb.Append('|').Append(' ')
                        .Append(EscapePipe(row.Tool)).Append(" | ")
                        .Append(EscapePipe(DisplayNameWithParameters(row))).Append(" | ")
                        .Append(EscapePipe(row.MeanText)).Append(" | ")
                        .Append(EscapePipe(row.ErrorText)).Append(" | ")
                        .Append(EscapePipe(row.AllocatedText)).Append(" | ")
                        .Append(vsFastest).Append(" | ")
                        .Append(vsBestBioSharpText).Append(" | ")
                        .Append(EscapePipe(notes))
                        .AppendLine(" |");
                }

                sb.AppendLine();
            }

            if (missingMethods.Count > 0)
            {
                sb.AppendLine("### Missing / not yet run");
                sb.AppendLine();
                foreach (var method in missingMethods)
                    sb.AppendLine($"- `{method}`");
                sb.AppendLine();
            }
        }

        return sb.ToString();
    }

    private static string BuildSummaryCsv(
        IReadOnlyList<BenchmarkResultRow> rows,
        IReadOnlyDictionary<string, BenchmarkTypeMetadata> metadataByType)
    {
        var sb = new StringBuilder();
        sb.AppendLine("BenchmarkType,Area,Tool,Method,DisplayName,Parameters,Mean,MeanNanoseconds,Error,Allocated,Unavailable,RequiredExternalTools,SourceFile");
        foreach (var row in rows.OrderBy(row => row.Area).ThenBy(row => row.BenchmarkTypeName).ThenBy(row => row.Tool).ThenBy(row => row.DisplayName))
        {
            sb.Append(Csv(row.BenchmarkTypeName)).Append(',')
                .Append(Csv(row.Area)).Append(',')
                .Append(Csv(row.Tool)).Append(',')
                .Append(Csv(row.MethodName)).Append(',')
                .Append(Csv(row.DisplayName)).Append(',')
                .Append(Csv(row.ParameterText)).Append(',')
                .Append(Csv(row.MeanText)).Append(',')
                .Append(Csv(row.MeanNanoseconds?.ToString(CultureInfo.InvariantCulture) ?? string.Empty)).Append(',')
                .Append(Csv(row.ErrorText)).Append(',')
                .Append(Csv(row.AllocatedText)).Append(',')
                .Append(Csv(row.IsUnavailableOnThisMachine ? "true" : "false")).Append(',')
                .Append(Csv(string.Join(";", row.RequiredExternalTools))).Append(',')
                .Append(Csv(Path.GetFileName(row.SourceFilePath)))
                .AppendLine();
        }

        return sb.ToString();
    }

    private static IReadOnlyList<string> BuildSectionOrder(IReadOnlyDictionary<string, BenchmarkTypeMetadata> metadataByType)
    {
        var preferred = new[]
        {
            typeof(SeederComparisonBenchmarks).FullName!,
            typeof(FmIndexOperationBenchmarks).FullName!,
            typeof(SmithWatermanMicroBenchmarks).FullName!,
            typeof(AlignmentBenchmarks).FullName!,
            typeof(PipelineBenchmarks).FullName!,
            typeof(AlignmentHeadToHeadBenchmarks).FullName!,
            typeof(VariantCallingHeadToHeadBenchmarks).FullName!,
            typeof(BclBenchmarks).FullName!,
            typeof(BclHeadToHeadBenchmarks).FullName!,
            typeof(FastqProcessingHeadToHeadBenchmarks).FullName!,
            typeof(CoverageAndDuplicateHeadToHeadBenchmarks).FullName!,
            typeof(RepeatMaskingHeadToHeadBenchmarks).FullName!,
            typeof(DeBruijnBenchmarks).FullName!
        };

        return preferred.Where(metadataByType.ContainsKey)
            .Concat(metadataByType.Keys.Except(preferred).OrderBy(key => key, StringComparer.Ordinal))
            .ToList();
    }

    private static bool TryParseDurationToNanoseconds(string text, out double value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text) || text == "NA")
        {
            return false;
        }

        var parts = text.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
        {
            return false;
        }

        if (!double.TryParse(parts[0], NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var magnitude))
        {
            return false;
        }

        value = parts[1] switch
        {
            "ns" => magnitude,
            "us" => magnitude * 1_000.0,
            "μs" => magnitude * 1_000.0,
            "µs" => magnitude * 1_000.0,
            "ms" => magnitude * 1_000_000.0,
            "s" => magnitude * 1_000_000_000.0,
            _ => double.NaN
        };

        return !double.IsNaN(value);
    }

    private static List<string> ParseCsvLine(string line)
    {
        var result = new List<string>();
        var sb = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];
            if (ch == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    sb.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }

                continue;
            }

            if (ch == ',' && !inQuotes)
            {
                result.Add(sb.ToString());
                sb.Clear();
                continue;
            }

            sb.Append(ch);
        }

        result.Add(sb.ToString());
        return result;
    }

    private static string FormatRatio(double value)
    {
        return value.ToString("0.00x", CultureInfo.InvariantCulture);
    }

    private static string EscapePipe(string value)
    {
        return string.IsNullOrEmpty(value) ? string.Empty : value.Replace("|", "\\|");
    }

    private static string Csv(string value)
    {
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
        {
            return '"' + value.Replace("\"", "\"\"") + '"';
        }

        return value;
    }

    private static string BuildParameterText(IReadOnlyDictionary<string, string> columns, IReadOnlyList<string> parameterPropertyNames)
    {
        var values = new List<string>();
        foreach (var propertyName in parameterPropertyNames)
        {
            if (columns.TryGetValue(propertyName, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                values.Add($"{propertyName}={value}");
            }
        }

        return string.Join(", ", values);
    }

    private static string DisplayNameWithParameters(BenchmarkResultRow row)
    {
        return string.IsNullOrWhiteSpace(row.ParameterText)
            ? row.DisplayName
            : $"{row.DisplayName} [{row.ParameterText}]";
    }

    private static string? ResolveReflectedMethodName(BenchmarkTypeMetadata typeMetadata, string csvMethodName)
    {
        if (typeMetadata.Methods.ContainsKey(csvMethodName))
        {
            return csvMethodName;
        }

        return typeMetadata.Methods.Values
            .FirstOrDefault(method => string.Equals(method.DisplayName, csvMethodName, StringComparison.Ordinal))
            ?.MethodName;
    }

    private sealed record PostProcessorOptions(string ResultsDirectory, string MarkdownOutputPath, string SummaryCsvOutputPath);

    private sealed record RawBenchmarkCsvRow(
        string BenchmarkType,
        string MethodName,
        IReadOnlyDictionary<string, string> Columns,
        string SourceFilePath);

    private sealed record BenchmarkTypeMetadata(
        string TypeFullName,
        string TypeName,
        string Area,
        IReadOnlyDictionary<string, BenchmarkMethodMetadata> Methods,
        IReadOnlyList<string> ParameterPropertyNames);

    private sealed record BenchmarkMethodMetadata(
        string MethodName,
        string DisplayName,
        IReadOnlyList<string> Categories,
        string Tool,
        string Area,
        string[] RequiredExternalTools);

    private sealed record BenchmarkResultRow(
        string BenchmarkType,
        string BenchmarkTypeName,
        string MethodName,
        string DisplayName,
        string ParameterText,
        string Area,
        string Tool,
        IReadOnlyList<string> Categories,
        string MeanText,
        double? MeanNanoseconds,
        string ErrorText,
        string StdDevText,
        string AllocatedText,
        string Job,
        string[] RequiredExternalTools,
        bool IsUnavailableOnThisMachine,
        string SourceFilePath);
}




