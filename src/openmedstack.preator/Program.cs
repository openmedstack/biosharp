using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging.Abstractions;

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
    using Microsoft.Extensions.Logging;
    using OpenMedStack.BioSharp.Io.FastQ;

    // ReSharper disable once ClassNeverInstantiated.Global
    [RequiresUnreferencedCode("Requires reference to RunInfo.")]
    internal class Program
    {
        static async Task Main(string[] args)
        {
            var result = Parser.Default.ParseArguments<Options>(args);
            _ = await result.WithParsedAsync(Parsed).ConfigureAwait(false);
            _ = await result.WithNotParsedAsync(NotParsed).ConfigureAwait(false);
        }

        [RequiresUnreferencedCode("Requires reference to RunInfo.")]
        private static async Task Parsed(Options options)
        {
            var tokenSource = new CancellationTokenSource();

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
            logger.LogInformation("Reading from {InputDir}", inputDir.FullName);
            logger.LogInformation("Reading structure {ReadStructure}", readStructure);

            var reader = new IlluminaDataReader(inputDir, NullLoggerFactory.Instance, readStructure);
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
            logger.LogInformation("Processing took {Elapsed}", stopwatch.Elapsed);

            Console.CancelKeyPress -= ConsoleCancelKeyPress;
            tokenSource.Dispose();
            return;

            void ConsoleCancelKeyPress(object? sender, ConsoleCancelEventArgs e)
            {
                // ReSharper disable once AccessToDisposedClosure
                tokenSource.Cancel(false);
            }
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

        private static Task NotParsed(IEnumerable<Error> errors)
        {
            foreach (var error in errors)
            {
                PrintError(error);
            }

            return Task.CompletedTask;
        }

        private static void PrintError(Error error)
        {
            switch (error.Tag)
            {
                case ErrorType.BadFormatTokenError:
                {
                    var e = (BadFormatTokenError)error;
                    Console.WriteLine($"Bad format token: {e.Token}");
                }
                    break;
                case ErrorType.MissingValueOptionError:
                {
                    var e = (MissingValueOptionError)error;
                    Console.WriteLine($"Missing value option: {e.NameInfo.NameText}");
                }
                    break;
                case ErrorType.UnknownOptionError:
                {
                    var e = (UnknownOptionError)error;
                    Console.WriteLine($"Unknown option: {e.Token}");
                }
                    break;
                case ErrorType.MissingRequiredOptionError:
                {
                    var e = (MissingRequiredOptionError)error;
                    Console.WriteLine($"Missing required option: {e.NameInfo.NameText}");
                }
                    break;
                case ErrorType.MutuallyExclusiveSetError:
                {
                    var e = (MutuallyExclusiveSetError)error;
                    Console.WriteLine($"Mutually exclusive set: {e.SetName}");
                }
                    break;
                case ErrorType.BadFormatConversionError:
                {
                    var e = (BadFormatConversionError)error;
                    Console.WriteLine($"Bad format conversion: {e.NameInfo.NameText}");
                }
                    break;
                case ErrorType.SequenceOutOfRangeError:
                {
                    var e = (SequenceOutOfRangeError)error;
                    Console.WriteLine($"Sequence out of range: {e.NameInfo.NameText}");
                }
                    break;
                case ErrorType.RepeatedOptionError:
                {
                    var e = (RepeatedOptionError)error;
                    Console.WriteLine($"Repeated option: {e.NameInfo.NameText}");
                }
                    break;
                case ErrorType.NoVerbSelectedError:
                {
                    Console.WriteLine($"No verb selected");
                }
                    break;
                case ErrorType.BadVerbSelectedError:
                {
                    var e = (BadVerbSelectedError)error;
                    Console.WriteLine($"Bad verb selected: {e.Token}");
                }
                    break;
                case ErrorType.HelpRequestedError:
                {
                    Console.WriteLine($"Help requested");
                }
                    break;
                case ErrorType.HelpVerbRequestedError:
                {
                    Console.WriteLine($"Help verb requested");
                }
                    break;
                case ErrorType.VersionRequestedError:
                {
                    Console.WriteLine($"Version requested");
                }
                    break;
                case ErrorType.SetValueExceptionError:
                {
                    var e = (SetValueExceptionError)error;
                    Console.WriteLine($"Set value exception: {e.NameInfo.NameText}");
                }
                    break;
                case ErrorType.InvalidAttributeConfigurationError:
                {
                    var e = (InvalidAttributeConfigurationError)error;
                    Console.WriteLine($"Invalid attribute configuration");
                }
                    break;
                case ErrorType.MissingGroupOptionError:
                {
                    var e = (MissingGroupOptionError)error;
                    Console.WriteLine(
                        $"Missing group option: {e.Group} -> {string.Join(", ", e.Names.Select(n => n.NameText))}");
                }
                    break;
                case ErrorType.GroupOptionAmbiguityError:
                {
                    var e = (GroupOptionAmbiguityError)error;
                    Console.WriteLine(
                        $"Group option ambiguity: {e.NameInfo.NameText}");
                }
                    break;
                case ErrorType.MultipleDefaultVerbsError:
                {
                    Console.WriteLine($"Multiple default verbs");
                }
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(error));
            }
        }
    }
}
