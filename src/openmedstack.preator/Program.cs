namespace OpenMedStack.Preator
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
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
            await result.WithNotParsed(_ => NotParsed(result)).WithParsedAsync(Parsed);
        }

        private static async Task Parsed(Options options)
        {
            using var tokenSource = new CancellationTokenSource();
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
            foreach (var s in Directory.EnumerateFiles(outputDir))
            {
                File.Delete(s);
            }

            using var semaphore = new SemaphoreSlim(options.Threads);
            var tasks = new List<Task>();

            var states = new[] { TaskStatus.Canceled, TaskStatus.RanToCompletion, TaskStatus.Faulted };
            stopwatch.Start();

            var trimmer = DefaultQualityTrimmer.Instance;
            await foreach (var r in reader.ReadClusterData(1, tokenSource.Token))
            {
                await Task.Yield();
                await semaphore.WaitAsync(tokenSource.Token).ConfigureAwait(false);

                var task = ReadData(outputDir, runInfo, r, semaphore, logger, trimmer, tokenSource.Token);
                foreach (var t in tasks.Where(t => states.Contains(t.Status)))
                {
                    logger.LogInformation("Disposing task");
                    t.Dispose();
                }

                tasks = tasks.Where(t => !states.Contains(t.Status)).Append(task).ToList();
                logger.LogInformation("Processing {active} tasks", tasks.Count);
                GC.Collect(3);
            }

            await Task.WhenAll(tasks).ConfigureAwait(false);
            foreach (var task in tasks.Where(task => states.Contains(task.Status)))
            {
                task.Dispose();
            }
            tasks.Clear();

            stopwatch.Stop();
            logger.LogInformation("Processing took {elapsed}", stopwatch.Elapsed);
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
            SemaphoreSlim semaphore,
            ILogger logger,
            IQualityTrimmer trimmer,
            CancellationToken cancellationToken)
        {
            var path = Path.Combine(outputDir, $"L{r.Lane.ToString().PadLeft(3, '0')}_{r.Tile}_R001.fastq.gz");
            await Task.Yield();
            logger.LogInformation("Reading cluster data on thread {thread}", Environment.CurrentManagedThreadId);
            await using var file = File.Open(
                path,
                new FileStreamOptions
                {
                    Access = FileAccess.Write,
                    Mode = FileMode.Create,
                    Options = FileOptions.Asynchronous,
                    Share = FileShare.Read,
                });
            await using (var writer = new FastQWriter(logger, file))
            {
                var sequences = r.ReadBclData(trimmer, cancellationToken).Where(c => c.Type == ReadType.T);
                await writer.Write(sequences.Select(c => Sequence.FromCluster(c, run)), cancellationToken)
                    .ConfigureAwait(false);
            }

            semaphore.Release();
        }
    }
}
