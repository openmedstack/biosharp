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
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Logging.Console;
    using Microsoft.Extensions.Options;
    using OpenMedStack.BioSharp.Io.FastQ;
    using OpenMedStack.BioSharp.Model;

    class Program
    {
        static async Task Main(string[] args)
        {
            using var tokenSource = new CancellationTokenSource();
            var stopwatch = new Stopwatch();
            var logProvider = new ConsoleLoggerProvider(
                new OptionsMonitor<ConsoleLoggerOptions>(
                    new OptionsFactory<ConsoleLoggerOptions>(
                        Array.Empty<IConfigureOptions<ConsoleLoggerOptions>>(),
                        Array.Empty<IPostConfigureOptions<ConsoleLoggerOptions>>()),
                    Array.Empty<IOptionsChangeTokenSource<ConsoleLoggerOptions>>(),
                    new OptionsCache<ConsoleLoggerOptions>()));
            var logger = logProvider.CreateLogger("logger");
            var inputDir = new DirectoryInfo(Path.GetFullPath(args[0]));
            var readStructure = args.Length > 1 ? ReadStructure.Parse(args[1]) : null;
            logger.LogInformation("Reading from {inputDir}", inputDir.FullName);
            logger.LogInformation("Reading structure {readStructure}", readStructure);

            var reader = new IlluminaDataReader(inputDir, logger, readStructure);
            var runInfo = reader.RunInfo();
            var outputDir = args.Length > 2 ? Path.GetFullPath(args[2]) : Path.Combine(inputDir.FullName, "Unaligned", runInfo.Id);
            foreach (var s in Directory.EnumerateFiles(outputDir))
            {
                File.Delete(s);
            }

            using var semaphore = new SemaphoreSlim(Environment.ProcessorCount / 2);
            var tasks = new List<Task>();

            stopwatch.Start();

            await foreach(var r in reader.ReadClusterData(1, tokenSource.Token))
            {
                await Task.Yield();
                await semaphore.WaitAsync(tokenSource.Token).ConfigureAwait(false);

                var task = ReadData(outputDir, runInfo, r, semaphore, logger);
                foreach (var t in tasks.Where(t => t.IsCompleted || t.IsFaulted))
                {
                    logger.LogInformation("Disposing task");
                    t.Dispose();
                }
                tasks = tasks.Where(t => !t.IsCompleted && !t.IsFaulted).Append(task).ToList();
                logger.LogInformation("Processing {active} tasks", tasks.Count);
                GC.Collect(3);
            }
            while(tasks.Count > 0)
            {
                var completed = Task.WhenAny(tasks);
                completed.Dispose();
                tasks.Remove(completed);
                logger.LogInformation("Processing {active} tasks", tasks.Count);
            }
            await Task.WhenAll(tasks).ConfigureAwait(false);
            tasks.Clear();

            stopwatch.Stop();
            logger.LogInformation("Processing took {elapsed}", stopwatch.Elapsed);
        }

        private static async Task ReadData(string outputDir, Run run, SampleReader r, SemaphoreSlim semaphore, ILogger logger)
        {
            var path = Path.Combine(outputDir, $"L{r.Lane.ToString().PadLeft(3, '0')}_{r.Tile}_R001.fastq.gz");
            await Task.Yield();
            logger.LogInformation("Reading cluster data on thread {thread}", Environment.CurrentManagedThreadId);
            await using var file = File.Open(path, FileMode.Create, FileAccess.Write, FileShare.Read);
            await using (var writer = new FastQWriter(logger, file))
            {
                var sequences = r.ReadBclData(default).Where(c => c.Type == ReadType.T);
                await writer.Write(sequences.Select(c => Sequence.FromCluster(c, run))).ConfigureAwait(false);
            }
            semaphore.Release();
        }
    }
}
