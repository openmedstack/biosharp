namespace OpenMedStack.Preator
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
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
    using OpenMedStack.BioSharp.Model;

    class Program
    {
        static async Task Main(string[] args)
        {
            var result = Parser.Default.ParseArguments<Options>(args);
            await result.WithNotParsed(_ => NotParsed(result)).WithParsedAsync(Parsed).ConfigureAwait(false);
        }

        private static async Task Parsed(Options options)
        {
            using var tokenSource = new CancellationTokenSource();

            void ConsoleCancelKeyPress(object? sender, ConsoleCancelEventArgs e)
            {
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

            stopwatch.Start();

            ConcurrentDictionary<string, FastQWriter> writers = new();
            var trimmer = DefaultQualityTrimmer.Instance;
            using var semaphore = new SemaphoreSlim(options.Threads == 0 ? Environment.ProcessorCount : options.Threads);
            await foreach (var r in reader.ReadClusterData(1, tokenSource.Token).ConfigureAwait(false))
            {
                await semaphore.WaitAsync(tokenSource.Token).ConfigureAwait(false);
                await ReadData(outputDir, runInfo, r, logger, trimmer, writers, tokenSource.Token).ConfigureAwait(false);
                semaphore.Release();
            }

            stopwatch.Stop();
            logger.LogInformation("Processing took {elapsed}", stopwatch.Elapsed);

            await Task.WhenAll(writers.Values.Select(w => w.DisposeAsync().AsTask())).ConfigureAwait(false);

            Console.CancelKeyPress -= ConsoleCancelKeyPress;
        }

        private static void NotParsed(ParserResult<Options> result)
        {
            var text = HelpText.AutoBuild(result, h => h, e => e);
            Console.WriteLine(text);
        }

        private static async Task ReadData(
            string outputDir,
            Run run,
            SampleReader r,
            ILogger logger,
            IQualityTrimmer trimmer,
            ConcurrentDictionary<string, FastQWriter> writers,
            CancellationToken cancellationToken)
        {
            logger.LogInformation("Reading cluster data on thread {thread}", Environment.CurrentManagedThreadId);

            await foreach (var group in r.ReadBclData(trimmer, cancellationToken)
                               .Where(x => x.Type is ReadType.T)
                               .Select(x => Sequence.FromCluster(x, run))
                               .GroupBy(
                                   sequence =>
                                       $"L{sequence.Header.Lane.ToString().PadLeft(3, '0')}_{sequence.Header.Barcode}_R00{(sequence.Header.Direction == ReadDirection.Forward ? '1' : '2')}.fastq.gz")
                               .WithCancellation(cancellationToken)
                               .ConfigureAwait(false))
            {
                await Persist(outputDir, logger, writers, cancellationToken, group);
            }
        }

        private static async Task Persist(
            string outputDir,
            ILogger logger,
            ConcurrentDictionary<string, FastQWriter> writers,
            CancellationToken cancellationToken,
            IAsyncGrouping<string, Sequence> group)
        {
            var path = Path.Combine(outputDir, group.Key);

            var writer = writers.GetOrAdd(
                path,
                static (p, l) =>
                {
                    l.LogInformation("Create writer to {path}", p);
                    return new FastQWriter(
                        l,
                        File.Open(
                            p,
                            new FileStreamOptions
                            {
                                Access = FileAccess.Write,
                                Mode = FileMode.Create,
                                Options = FileOptions.Asynchronous,
                                Share = FileShare.Read
                            }));
                },
                logger);
            await writer.Write(group, cancellationToken).ConfigureAwait(false);
        }
    }
}
