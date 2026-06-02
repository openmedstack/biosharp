namespace OpenMedStack.BioSharp.Benchmarks;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;

internal static class WarmBenchmarkReportRunner
{
    private const int DefaultWarmups = 1;
    private const int DefaultIterations = 3;

    private static readonly Type[] DefaultBenchmarkTypes =
    [
        typeof(AlignmentHeadToHeadBenchmarks),
        typeof(VariantCallingHeadToHeadBenchmarks),
        typeof(BclHeadToHeadBenchmarks),
        typeof(FastqProcessingHeadToHeadBenchmarks),
        typeof(CoverageAndDuplicateHeadToHeadBenchmarks),
        typeof(RepeatMaskingHeadToHeadBenchmarks),
        typeof(E2eHeadToHeadBenchmarks)
    ];

    public static int Run(string[] args)
    {
        var output = Path.Combine("benchmark-results", "csharp-warm-benchmarks.csv");
        var warmups = DefaultWarmups;
        var iterations = DefaultIterations;
        var filters = new List<string>();

        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--output" when index + 1 < args.Length:
                    output = args[++index];
                    break;
                case "--warmups" when index + 1 < args.Length:
                    warmups = int.Parse(args[++index], CultureInfo.InvariantCulture);
                    break;
                case "--iterations" when index + 1 < args.Length:
                    iterations = int.Parse(args[++index], CultureInfo.InvariantCulture);
                    break;
                case "--filter" when index + 1 < args.Length:
                    filters.Add(args[++index]);
                    break;
            }
        }

        var selectedTypes = DefaultBenchmarkTypes
            .Where(type => filters.Count == 0 || MatchesTypeOrAnyMethod(type, filters))
            .ToArray();

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output)) ?? ".");
        var rows = new List<ResultRow>();
        foreach (var type in selectedTypes)
        {
            rows.AddRange(RunType(type, warmups, iterations, filters));
        }

        WriteCsv(output, rows);
        Console.WriteLine($"Wrote {rows.Count.ToString(CultureInfo.InvariantCulture)} rows to {output}");
        return rows.Any(row => row.Status == "Measured") ? 0 : 1;
    }

    private static IEnumerable<ResultRow> RunType(Type type, int warmups, int iterations, IReadOnlyList<string> filters)
    {
        foreach (var parameterSet in ExpandParameters(type))
        {
            object? instance = null;
            Exception? setupException = null;
            try
            {
                instance = Activator.CreateInstance(type) ?? throw new InvalidOperationException($"Could not create {type.Name}.");
                ApplyParameters(instance, parameterSet);
                InvokeLifecycle(instance, typeof(GlobalSetupAttribute));
            }
            catch (Exception exception)
            {
                setupException = exception;
                Cleanup(instance);
            }

            if (setupException != null)
            {
                foreach (var method in BenchmarkMethods(type, filters))
                {
                    yield return ResultRow.Failed(type.Name, MethodLabel(method), parameterSet, setupException);
                }

                continue;
            }

            var benchmarkInstance = instance ?? throw new InvalidOperationException($"Could not create {type.Name}.");

            foreach (var method in BenchmarkMethods(type, filters))
            {
                yield return RunMethod(benchmarkInstance, method, parameterSet, warmups, iterations);
            }

            Cleanup(benchmarkInstance);
        }
    }

    private static ResultRow RunMethod(object instance, MethodInfo method, IReadOnlyDictionary<string, object?> parameters, int warmups, int iterations)
    {
        try
        {
            Console.WriteLine($"Running {instance.GetType().Name}.{method.Name} {FormatParametersForProgress(parameters)}");
            for (var warmup = 0; warmup < warmups; warmup++)
            {
                InvokeBenchmark(instance, method);
            }

            var elapsedMilliseconds = new List<double>(iterations);
            object? lastResult = null;
            for (var iteration = 0; iteration < iterations; iteration++)
            {
                var stopwatch = Stopwatch.StartNew();
                lastResult = InvokeBenchmark(instance, method);
                stopwatch.Stop();
                elapsedMilliseconds.Add(stopwatch.Elapsed.TotalMilliseconds);
            }

            return ResultRow.Measured(instance.GetType().Name, MethodLabel(method), parameters, elapsedMilliseconds, lastResult);
        }
        catch (Exception exception)
        {
            return ResultRow.Failed(instance.GetType().Name, MethodLabel(method), parameters, exception);
        }
    }

    private static IEnumerable<MethodInfo> BenchmarkMethods(Type type, IReadOnlyList<string>? filters = null)
    {
        return type.GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Where(method => method.GetCustomAttribute<BenchmarkAttribute>() != null)
            .Where(method => filters == null || filters.Count == 0 || filters.Any(filter => MatchesMethod(type, method, filter)))
            .OrderBy(method => method.MetadataToken);
    }

    private static bool MatchesTypeOrAnyMethod(Type type, IReadOnlyList<string> filters)
    {
        return filters.Any(filter => type.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
            (type.FullName?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false)) ||
            BenchmarkMethods(type).Any(method => filters.Any(filter => MatchesMethod(type, method, filter)));
    }

    private static bool MatchesMethod(Type type, MethodInfo method, string filter)
    {
        var normalizedFilter = filter.Trim('*');
        return method.Name.Contains(normalizedFilter, StringComparison.OrdinalIgnoreCase) ||
            MethodLabel(method).Contains(normalizedFilter, StringComparison.OrdinalIgnoreCase) ||
            $"{type.FullName}.{method.Name}".Contains(normalizedFilter, StringComparison.OrdinalIgnoreCase);
    }

    private static string MethodLabel(MethodInfo method)
    {
        var benchmark = method.GetCustomAttribute<BenchmarkAttribute>();
        return string.IsNullOrWhiteSpace(benchmark?.Description) ? method.Name : benchmark.Description!;
    }

    private static IReadOnlyList<IReadOnlyDictionary<string, object?>> ExpandParameters(Type type)
    {
        var parameterProperties = type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Select(property => new { Property = property, Attribute = property.GetCustomAttribute<ParamsAttribute>() })
            .Where(item => item.Attribute != null)
            .ToArray();

        if (parameterProperties.Length == 0)
        {
            return [new Dictionary<string, object?>()];
        }

        var combinations = new List<Dictionary<string, object?>> { new() };
        foreach (var parameter in parameterProperties)
        {
            var values = parameter.Attribute!.Values.Length == 0
                ? [parameter.Property.GetValue(Activator.CreateInstance(type))]
                : parameter.Attribute.Values;
            combinations = combinations
                .SelectMany(existing => values.Select(value =>
                {
                    var next = new Dictionary<string, object?>(existing, StringComparer.Ordinal);
                    next[parameter.Property.Name] = ConvertParameter(value, parameter.Property.PropertyType);
                    return next;
                }))
                .ToList();
        }

        return combinations;
    }

    private static object? ConvertParameter(object? value, Type targetType)
    {
        if (value == null || targetType.IsInstanceOfType(value))
        {
            return value;
        }

        return Convert.ChangeType(value, targetType, CultureInfo.InvariantCulture);
    }

    private static void ApplyParameters(object instance, IReadOnlyDictionary<string, object?> parameterSet)
    {
        foreach (var parameter in parameterSet)
        {
            var property = instance.GetType().GetProperty(parameter.Key, BindingFlags.Instance | BindingFlags.Public)
                ?? throw new InvalidOperationException($"Parameter property {parameter.Key} was not found.");
            property.SetValue(instance, parameter.Value);
        }
    }

    private static void InvokeLifecycle(object instance, Type attributeType)
    {
        foreach (var method in instance.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                     .Where(method => method.GetCustomAttributes(attributeType, inherit: true).Any()))
        {
            _ = InvokeBenchmark(instance, method);
        }
    }

    private static object? InvokeBenchmark(object instance, MethodInfo method)
    {
        var result = method.Invoke(instance, null);
        if (result is Task task)
        {
            task.GetAwaiter().GetResult();
            var resultProperty = task.GetType().GetProperty("Result", BindingFlags.Instance | BindingFlags.Public);
            return resultProperty?.GetValue(task);
        }

        return result;
    }

    private static void Cleanup(object? instance)
    {
        if (instance == null)
        {
            return;
        }

        try
        {
            InvokeLifecycle(instance, typeof(GlobalCleanupAttribute));
        }
        catch
        {
            // Cleanup failures should not hide benchmark measurements.
        }
    }

    private static void WriteCsv(string output, IEnumerable<ResultRow> rows)
    {
        using var writer = new StreamWriter(output, append: false, Encoding.UTF8);
        writer.WriteLine("Class,Method,Parameters,Status,Iterations,MeanMs,MinMs,MaxMs,LastResult,Error");
        foreach (var row in rows)
        {
            writer.WriteLine(string.Join(',',
                Csv(row.ClassName),
                Csv(row.Method),
                Csv(row.Parameters),
                Csv(row.Status),
                row.Iterations.ToString(CultureInfo.InvariantCulture),
                Csv(row.MeanMs?.ToString("0.###", CultureInfo.InvariantCulture) ?? string.Empty),
                Csv(row.MinMs?.ToString("0.###", CultureInfo.InvariantCulture) ?? string.Empty),
                Csv(row.MaxMs?.ToString("0.###", CultureInfo.InvariantCulture) ?? string.Empty),
                Csv(row.LastResult ?? string.Empty),
                Csv(row.Error ?? string.Empty)));
        }
    }

    private static string Csv(string value)
    {
        return $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    }

    private static string FormatParametersForProgress(IReadOnlyDictionary<string, object?> parameters)
    {
        return parameters.Count == 0 ? string.Empty : $"[{ResultRow.FormatParameters(parameters)}]";
    }

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
        string? Error)
    {
        public static ResultRow Measured(string className, string method, IReadOnlyDictionary<string, object?> parameters, IReadOnlyList<double> elapsedMilliseconds, object? lastResult)
        {
            return new ResultRow(
                className,
                method,
                FormatParameters(parameters),
                "Measured",
                elapsedMilliseconds.Count,
                elapsedMilliseconds.Average(),
                elapsedMilliseconds.Min(),
                elapsedMilliseconds.Max(),
                Convert.ToString(lastResult, CultureInfo.InvariantCulture),
                null);
        }

        public static ResultRow Failed(string className, string method, IReadOnlyDictionary<string, object?> parameters, Exception exception)
        {
            var root = exception is TargetInvocationException && exception.InnerException != null
                ? exception.InnerException
                : exception;
            return new ResultRow(className, method, FormatParameters(parameters), "Failed", 0, null, null, null, null, root.Message);
        }

        public static string FormatParameters(IReadOnlyDictionary<string, object?> parameters)
        {
            return parameters.Count == 0
                ? string.Empty
                : string.Join(";", parameters.Select(parameter => $"{parameter.Key}={Convert.ToString(parameter.Value, CultureInfo.InvariantCulture)}"));
        }
    }
}