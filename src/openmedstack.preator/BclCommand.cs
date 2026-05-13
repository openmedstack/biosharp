using System;
using System.CommandLine;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OpenMedStack.BioSharp.Io.Bcl;
using OpenMedStack.BioSharp.Io.FastQ;
using OpenMedStack.BioSharp.Model.Bcl;

namespace OpenMedStack.Preator;

internal partial class BclCommand
{
    internal static Task Invoke(ParseResult parseResult, CancellationToken cancellationToken)
    {
        var options = new BclOptions
        {
            InputFolder = new DirectoryInfo(parseResult.GetValue(PreatorCommandOptions.InputOption)!),
            OutputFolder = new DirectoryInfo(parseResult.GetValue(PreatorCommandOptions.OutputOption)!),
            ReadStructure = parseResult.GetValue(PreatorCommandOptions.ReadStructureOption),
            Lanes = parseResult.GetValue(PreatorCommandOptions.LanesOption) ?? "1"
        };

        return InvokeBclRead(options, cancellationToken);
    }

    internal static async Task InvokeBclRead(BclOptions options, CancellationToken cancellationToken)
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
        var inputDir = options.InputFolder;
        var readStructure = !string.IsNullOrWhiteSpace(options.ReadStructure)
            ? ReadStructure.Parse(options.ReadStructure)
            : null;
        LogReadingFromInputdir(logger, inputDir.FullName);
        LogReadingStructureReadStructure(logger, readStructure);

        var reader = new IlluminaDataReader(inputDir, NullLoggerFactory.Instance, readStructure);
        var runInfo = reader.RunInfo();
        var outputDir = !string.IsNullOrWhiteSpace(options.OutputFolder?.FullName)
            ? Path.GetFullPath(options.OutputFolder.FullName)
            : Path.Combine(inputDir.FullName, "Unaligned", runInfo.Id);
        if (!Directory.Exists(outputDir))
        {
            Directory.CreateDirectory(outputDir);
        }

        foreach (var s in Directory.EnumerateFiles(outputDir))
        {
            File.Delete(s);
        }

        var trimmer = DefaultQualityTrimmer.Instance;
        stopwatch.Start();

        var lanes = options.Lanes.Contains('*')
            ? reader.GetAllLanes()
            : options.Lanes.Split(',').Select(int.Parse).ToArray();
        await Task.WhenAll(lanes.AsParallel()
            .WithExecutionMode(ParallelExecutionMode.ForceParallelism)
            .Select(lane => ProcessLane(outputDir, runInfo, lane, logger, reader, trimmer, cancellationToken)));

        stopwatch.Stop();
        LogProcessingTookElapsed(logger, stopwatch.Elapsed);
    }

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

    [LoggerMessage(LogLevel.Information, "Reading from {InputDir}")]
    static partial void LogReadingFromInputdir(ILogger logger, string inputDir);

    [LoggerMessage(LogLevel.Information, "Reading structure {ReadStructure}")]
    static partial void LogReadingStructureReadStructure(ILogger logger, ReadStructure? readStructure);

    [LoggerMessage(LogLevel.Information, "Processing took {Elapsed}")]
    static partial void LogProcessingTookElapsed(ILogger logger, TimeSpan elapsed);
}
