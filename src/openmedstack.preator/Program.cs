using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging.Abstractions;

namespace OpenMedStack.Preator;

using System;
using System.CommandLine;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BioSharp.Io.Bcl;
using BioSharp.Model.Bcl;
using Microsoft.Extensions.Logging;
using BioSharp.Io.FastQ;

// ReSharper disable once ClassNeverInstantiated.Global
internal class Program
{
    private static Task<int> Main(string[] args)
    {
        var inputOption = new Option<string>("--input", ["-i"])
        {
            Description = "Set the data input folder (can be relative).",
            Required = true
        };
        var outputOption = new Option<string?>("--output", ["-o"])
        {
            Description = "Set the data output folder (can be relative).",
            DefaultValueFactory = _ => null
        };
        var readStructureOption = new Option<string?>("--readstructure", ["-r"])
        {
            Description = "Set the read structure for the data.",
            DefaultValueFactory = _ => null
        };
        var lanesOption = new Option<string>("--lanes", ["-l"])
        {
            Description = "Comma separated list of lanes to read. Use * to read all.",
            DefaultValueFactory = _ => "1"
        };

        var rootCommand = new RootCommand("Preator - Illumina BCL to FastQ converter");
        rootCommand.Add(inputOption);
        rootCommand.Add(outputOption);
        rootCommand.Add(readStructureOption);
        rootCommand.Add(lanesOption);

        #pragma warning disable IL2026
        rootCommand.SetAction(GetAction(inputOption, outputOption, readStructureOption, lanesOption));
        #pragma warning restore IL2026

        return rootCommand.Parse(args).InvokeAsync(null!, CancellationToken.None);
    }

    [RequiresUnreferencedCode("Requires reference to RunInfo.")]
    private static Func<ParseResult, CancellationToken, Task> GetAction(
        Option<string> inputOption,
        Option<string?> outputOption,
        Option<string?> readStructureOption,
        Option<string> lanesOption)
    {
        return (parseResult, cancellationToken) =>
        {
            var options = new Options
            {
                InputFolder = parseResult.GetValue(inputOption)!,
                OutputFolder = parseResult.GetValue(outputOption),
                ReadStructure = parseResult.GetValue(readStructureOption),
                Lanes = parseResult.GetValue(lanesOption) ?? "1"
            };
            return Parsed(options, cancellationToken);
        };
    }

    [RequiresUnreferencedCode("Requires reference to RunInfo.")]
    private static async Task Parsed(Options options, CancellationToken cancellationToken)
    {
        var stopwatch = new Stopwatch();
        var logger = LoggerFactory.Create(b =>
            {
                b.AddJsonConsole(f =>
                {
                    f.UseUtcTimestamp = true;
                    f.IncludeScopes = true;
                });
            })
            .CreateLogger("all");
        var inputDir = new DirectoryInfo(options.InputFolder);
        var readStructure = !string.IsNullOrWhiteSpace(options.ReadStructure)
            ? ReadStructure.Parse(options.ReadStructure)
            : null;
        logger.LogInformation("Reading from {InputDir}", inputDir.FullName);
        logger.LogInformation("Reading structure {ReadStructure}", readStructure);

        var reader = new IlluminaDataReader(inputDir, NullLoggerFactory.Instance, readStructure);
        var runInfo = reader.RunInfo();
        var outputDir = !string.IsNullOrWhiteSpace(options.OutputFolder)
            ? Path.GetFullPath(options.OutputFolder)
            : Path.Combine(inputDir.FullName, "Unaligned", runInfo.Id);
        if (!Directory.Exists(outputDir)) Directory.CreateDirectory(outputDir);

        foreach (var s in Directory.EnumerateFiles(outputDir)) File.Delete(s);

        var trimmer = DefaultQualityTrimmer.Instance;
        stopwatch.Start();

        var lanes = options.Lanes.Contains('*')
            ? reader.GetAllLanes()
            : options.Lanes.Split(',').Select(int.Parse).ToArray();
        await Task.WhenAll(lanes.AsParallel()
            .WithExecutionMode(ParallelExecutionMode.ForceParallelism)
            .Select(lane => ProcessLane(outputDir, runInfo, lane, logger, reader, trimmer, cancellationToken)));

        stopwatch.Stop();
        logger.LogInformation("Processing took {Elapsed}", stopwatch.Elapsed);
    }

    [RequiresUnreferencedCode("Requires reference to RunInfo.")]
    private static async Task ProcessLane(
        string outputDir,
        Run runInfo,
        int lane,
        ILogger logger,
        IlluminaDataReader reader,
        IQualityTrimmer trimmer,
        CancellationToken cancellationToken)
    {
        await using var file = File.Open(
            Path.Combine(outputDir,
                $"{runInfo.Instrument}_{runInfo.Number}_L{lane.ToString().PadLeft(3, '0')}.fastq.gz"),
            new FileStreamOptions
            {
                Access = FileAccess.Write,
                Mode = FileMode.Create,
                Options = FileOptions.Asynchronous,
                Share = FileShare.None
            });
        await using var index = File.Open(
            Path.Combine(outputDir,
                $"{runInfo.Instrument}_{runInfo.Number}_L{lane.ToString().PadLeft(3, '0')}.fastqi.gz"),
            new FileStreamOptions
            {
                Access = FileAccess.Write,
                Mode = FileMode.Create,
                Options = FileOptions.Asynchronous,
                Share = FileShare.None
            });
        await using var indexZip = new GZipStream(index, CompressionLevel.Fastest, true);
        await using var writer = new FastQWriter(logger, file, indexZip, CompressionLevel.Fastest, null, true);
        await foreach (var bclData in reader.ReadClusterData(lane, cancellationToken)
            .Select(sr => sr.ReadBclData(trimmer, cancellationToken))
            .WithCancellation(cancellationToken))
        {
            var (sequenceCount, byteCount) = await writer.Write(bclData, cancellationToken).ConfigureAwait(false);
            logger.LogInformation("Wrote {Count} sequences with {Bytes} bytes", sequenceCount, byteCount);
        }
    }
}
