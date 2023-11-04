using System.Diagnostics.CodeAnalysis;

namespace OpenMedStack.Preator
{
    using System;
    using System.Diagnostics;
    using System.IO;
    using System.IO.Compression;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using BioSharp.Io.Bcl;
    using BioSharp.Model.Bcl;
    using CommandLine;
    using CommandLine.Text;
    using Microsoft.Extensions.Logging;
    using OpenMedStack.BioSharp.Io.FastQ;

    // ReSharper disable once ClassNeverInstantiated.Global
    [RequiresUnreferencedCode("Requires reference to RunInfo.")]
    class Program
    {
        static async Task Main(string[] args)
        {
            var result = Parser.Default.ParseArguments<Options>(args);
            await result.WithNotParsed(_ => NotParsed(result)).WithParsedAsync(Parsed).ConfigureAwait(false);
        }

        [RequiresUnreferencedCode("Requires reference to RunInfo.")]
        private static async Task Parsed(Options options)
        {
            var tokenSource = new CancellationTokenSource();

            void ConsoleCancelKeyPress(object? sender, ConsoleCancelEventArgs e)
            {
                // ReSharper disable once AccessToDisposedClosure
                tokenSource.Cancel(false);
            }

            Console.CancelKeyPress += ConsoleCancelKeyPress;
            var stopwatch = new Stopwatch();
            var logger = LoggerFactory.Create(
                    b =>
                    {
                        b.AddJsonConsole(
                            f =>
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
            logger.LogInformation("Reading from {inputDir}", inputDir.FullName);
            logger.LogInformation("Reading structure {readStructure}", readStructure);

            var reader = new IlluminaDataReader(inputDir, logger, readStructure);
            var runInfo = reader.RunInfo();
            var outputDir = !string.IsNullOrWhiteSpace(options.OutputFolder)
                ? Path.GetFullPath(options.OutputFolder)
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
                // ReSharper disable once AccessToDisposedClosure
                .Select(lane => ProcessLane(outputDir, runInfo, lane, logger, reader, trimmer, tokenSource.Token)));

            stopwatch.Stop();
            logger.LogInformation("Processing took {elapsed}", stopwatch.Elapsed);

            Console.CancelKeyPress -= ConsoleCancelKeyPress;
            tokenSource.Dispose();
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
                logger.LogInformation("Wrote {count} sequences with {bytes} bytes", sequenceCount, byteCount);
            }
        }

        private static void NotParsed(ParserResult<Options> result)
        {
            var text = HelpText.AutoBuild(result, h => h, e => e);
            Console.WriteLine(text);
        }
    }
}
