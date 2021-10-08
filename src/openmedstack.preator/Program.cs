namespace OpenMedStack.Preator
{
    using System;
    using System.IO;
    using System.Linq;
    using System.Threading.Tasks;
    using BioSharp.Io.Bcl;
    using BioSharp.Model.Bcl;
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Logging.Console;
    using Microsoft.Extensions.Options;

    class Program
    {
        static async Task Main(string[] args)
        {
            var logProvider = new ConsoleLoggerProvider(
                new OptionsMonitor<ConsoleLoggerOptions>(
                    new OptionsFactory<ConsoleLoggerOptions>(
                        Array.Empty<IConfigureOptions<ConsoleLoggerOptions>>(),
                        Array.Empty<IPostConfigureOptions<ConsoleLoggerOptions>>()),
                    Array.Empty<IOptionsChangeTokenSource<ConsoleLoggerOptions>>(),
                    new OptionsCache<ConsoleLoggerOptions>()));
            var logger = logProvider.CreateLogger("logger");
            var inputDir = new DirectoryInfo(args[0]);
            var readStructure = args.Length > 1 ? ReadStructure.Parse(args[1]) : null;

            logger.LogInformation("Reading from " + inputDir.FullName);
            logger.LogInformation("Reading structure " + readStructure);

            var reader = new IlluminaDataReader(inputDir, readStructure);
            var runInfo = reader.RunInfo();

            var demuxWriter = new DemultiplexFastQWriter(
                s => Path.Combine(inputDir.FullName, "Unaligned", runInfo.Id, $"Sample_{s.Barcode}", $"{s.Barcode}_L{s.Lane.ToString().PadLeft(3, '0')}_R001.fastq.gz"),
                runInfo,
                logger);
            await using (demuxWriter.ConfigureAwait(false))
            {
                var sequences = reader.ReadClusterData().Where(c => c.Type == ReadType.T);
                await demuxWriter.WriteDemultiplexed(sequences).ConfigureAwait(false);
            }
        }
    }
}
