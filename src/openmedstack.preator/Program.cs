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
            await result
                .WithNotParsed(e => NotParsed(result))
                .WithParsedAsync(Parsed);
        }

        private static async Task Parsed(Options options)
        {
            using var tokenSource = new CancellationTokenSource();
            var stopwatch = new Stopwatch();
            var logger = LoggerFactory.Create(b => { b.AddSystemdConsole(f => { f.UseUtcTimestamp = true; }); }).CreateLogger("all");
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

            stopwatch.Start();

            var trimmer = DefaultQualityTrimmer.Default;
            await foreach (var r in reader.ReadClusterData(1, tokenSource.Token))
            {
                await Task.Yield();
                await semaphore.WaitAsync(tokenSource.Token).ConfigureAwait(false);

                var task = ReadData(outputDir, runInfo, r, semaphore, logger, trimmer, tokenSource.Token);
                foreach (var t in tasks.Where(t => t.IsCompleted || t.IsFaulted))
                {
                    logger.LogInformation("Disposing task");
                    t.Dispose();
                }
                tasks = tasks.Where(t => !t.IsCompleted && !t.IsFaulted).Append(task).ToList();
                logger.LogInformation("Processing {active} tasks", tasks.Count);
                GC.Collect(3);
            }
            while (tasks.Count > 0)
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
            await using var file = File.Open(path, FileMode.Create, FileAccess.Write, FileShare.Read);
            await using (var writer = new FastQWriter(logger, file))
            {
                var sequences = r.ReadBclData(trimmer, cancellationToken).Where(c => c.Type == ReadType.T);
                await writer.Write(sequences.Select(c => Sequence.FromCluster(c, run)), cancellationToken).ConfigureAwait(false);
            }
            semaphore.Release();
        }
    }

    internal class Options
    {
        [Option('t', "threads", Required = false, Default = 4, HelpText = "Set amount of threads to use for processing.")]
        public int Threads { get; set; }

        [Option('i', "input", Required = true, HelpText = "Set the data input folder (can be relative).")]
        public string InputFolder { get; set; }

        [Option('o', "output", Required = false, Default = null, HelpText = "Set the data output folder (can be relative).")]
        public string OutputFolder { get; set; }

        [Option('r', "readstructure", Required = false, Default = null, HelpText = "Set the read structure for the data.")]
        public string ReadStructure { get; set; }
    }
}
