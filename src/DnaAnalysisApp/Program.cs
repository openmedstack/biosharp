using System.Diagnostics;
using DnaAnalysisApp;
using Microsoft.Extensions.Logging.Abstractions;
using OpenMedStack.BioSharp.Calculations.Alignment;
using OpenMedStack.BioSharp.Io.FastA;
using OpenMedStack.BioSharp.Io.FastQ;
using OpenMedStack.BioSharp.Model;

return await ProgramEntry.RunAsync(args);

namespace DnaAnalysisApp
{
    internal static class ProgramEntry
    {
        public static async Task<int> RunAsync(string[] args)
        {
            AppOptions options;

            try
            {
                options = AppOptions.Parse(args);
            }
            catch (ArgumentException exception)
            {
                await Console.Error.WriteLineAsync(exception.Message);
                await Console.Error.WriteLineAsync();
                PrintUsage();
                return 1;
            }

            if (options.ShowHelp)
            {
                PrintUsage();
                return 0;
            }

            var stopwatch = new Stopwatch();
            stopwatch.Start();
            try
            {
                return await RunPipelineAsync(options);
            }
            catch (Exception exception)
            {
                await Console.Error.WriteLineAsync($"Analysis failed: {exception.Message}");
                return 1;
            }
            finally
            {
                stopwatch.Stop();
                Console.WriteLine($"Total runtime: {stopwatch.Elapsed}");
            }
        }

        private static async Task<int> RunPipelineAsync(AppOptions options)
        {
            if (!File.Exists(options.ReferencePath))
            {
                throw new FileNotFoundException("Reference FASTA file not found.", options.ReferencePath);
            }

            if (!File.Exists(options.FastqPath))
            {
                throw new FileNotFoundException("FASTQ file not found.", options.FastqPath);
            }

            if (!options.FastqPath.EndsWith(".gz", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    "BioSharp's FastQReader expects gzipped FASTQ input. Provide a .fastq.gz file.");
            }

            Directory.CreateDirectory(options.OutputDirectory);

            Console.WriteLine($"Loading reference from {options.ReferencePath}");
            var reference = await LoadReferenceAsync(options.ReferencePath, options.ReferenceIdContains);
            var chromosome = options.Chromosome ?? NormalizeSequenceId(reference.Id);

            Console.WriteLine($"Selected reference sequence: {reference.Id} ({reference.Length:N0} bp)");
            Console.WriteLine($"Using chromosome/output contig name: {chromosome}");

            var pipeline = new VariantCallingPipeline(
                reference,
                chromosome,
                new VariantCallingPipeline.PipelineOptions
                {
                    MinAlignmentScore = options.MinAlignmentScore,
                    MinVariantQuality = options.MinVariantQuality,
                    EnableSoftClipRealignment = options.EnableSoftClipRealignment,
                    EnableGraphSvDetection = options.EnableGraphSvDetection,
                    KmerSize = options.KmerSize,
                    MinGraphCoverage = options.MinGraphCoverage,
                    GraphWindowBp = options.GraphWindowBp
                });

            Console.WriteLine($"Reading FASTQ from {options.FastqPath}");
            var processedReads = await ProcessFastqAsync(pipeline, options.FastqPath, options.MaxReads);
            Console.WriteLine($"Processed reads: {processedReads:N0}");

            if (options.EnableGraphSvDetection)
            {
                Console.WriteLine("Running full-reference De Bruijn graph analysis");
                await pipeline.RunFullGraphAnalysisAsync();
            }

            var result = pipeline.BuildResult();
            var mergedVariants = result.Variants.OrderBy(variant => variant.Position).ToArray();

            var vcfPath = Path.Combine(options.OutputDirectory, options.OutputPrefix + ".vcf");
            var reportPath = Path.Combine(options.OutputDirectory, options.OutputPrefix + ".tsv");
            var summaryPath = Path.Combine(options.OutputDirectory, options.OutputPrefix + ".summary.txt");

            await WriteMergedVcfAsync(vcfPath, mergedVariants, chromosome, reference.Length);
            await WriteReportAsync(reportPath, mergedVariants);
            await WriteSummaryAsync(summaryPath, processedReads, result, mergedVariants);

            Console.WriteLine();
            Console.WriteLine(result.Metrics);
            Console.WriteLine($"Merged variants: {mergedVariants.Length:N0}");
            Console.WriteLine($"VCF: {vcfPath}");
            Console.WriteLine($"Report: {reportPath}");
            Console.WriteLine($"Summary: {summaryPath}");

            return 0;
        }

        private static async Task<Sequence> LoadReferenceAsync(string referencePath, string? referenceIdContains)
        {
            var reader = new FastAReader();
            var sequences = IsGzip(referencePath)
                ? reader.ReadGz(referencePath)
                : reader.Read(referencePath);

            await foreach (var sequence in sequences)
            {
                if (string.IsNullOrWhiteSpace(referenceIdContains) ||
                    sequence.Id.Contains(referenceIdContains, StringComparison.OrdinalIgnoreCase))
                {
                    return sequence;
                }
            }

            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(referenceIdContains)
                    ? "No reference sequences were found in the FASTA file."
                    : $"No reference ID containing '{referenceIdContains}' was found in the FASTA file.");
        }

        private static async Task<int> ProcessFastqAsync(
            VariantCallingPipeline pipeline,
            string fastqPath,
            int? maxReads)
        {
            var reader = new FastQReader(NullLogger.Instance);
            var processedReads = 0;

            await foreach (var read in reader.Read(fastqPath))
            {
                await pipeline.ProcessReadAsync(read);
                processedReads++;

                if (processedReads % 1000 == 0)
                {
                    Console.WriteLine($"Processed {processedReads:N0} reads...");
                }

                if (processedReads >= maxReads)
                {
                    break;
                }
            }

            return processedReads;
        }

        private static async Task WriteMergedVcfAsync(
            string outputPath,
            IReadOnlyCollection<LocalVariantResult> variants,
            string chromosome,
            long chromLength)
        {
            await using var stream = File.Create(outputPath);
            await VcfWriter.WriteAsync(stream, variants, chromosome, chromLength: chromLength);
        }

        private static async Task WriteReportAsync(string outputPath, IReadOnlyCollection<LocalVariantResult> variants)
        {
            await using var writer = new StreamWriter(outputPath);
            await writer.WriteLineAsync(
                "Chromosome\tPosition\tEndPosition\tType\tReference\tAlternate\tQuality\tDepth\tFilter\tStructuralVariant\tSvType\tGenotype");

            foreach (var variant in variants.OrderBy(item => item.Position))
            {
                var line = string.Join(
                    '\t',
                    variant.Chromosome,
                    variant.Position.ToString(),
                    variant.EndPosition.ToString(),
                    GetVariantType(variant),
                    variant.Reference,
                    variant.Alternate,
                    variant.QuantitativeQuality.ToString(),
                    variant.Depth.ToString(),
                    variant.IsPass ? "PASS" : "LOW_QUAL",
                    variant.IsStructuralVariant ? "true" : "false",
                    variant.SvType?.ToString() ?? string.Empty,
                    variant.Genotype?.ToString() ?? string.Empty);

                await writer.WriteLineAsync(line);
            }
        }

        private static async Task WriteSummaryAsync(
            string outputPath,
            int processedReads,
            VariantCallingPipeline.PipelineResult result,
            IReadOnlyCollection<LocalVariantResult> mergedVariants)
        {
            var counts = mergedVariants
                .GroupBy(GetVariantType)
                .OrderBy(group => group.Key)
                .ToDictionary(group => group.Key, group => group.Count());

            await using var writer = new StreamWriter(outputPath);
            await writer.WriteLineAsync("DNA Analysis Summary");
            await writer.WriteLineAsync($"ProcessedReads\t{processedReads}");
            await writer.WriteLineAsync($"MappedReads\t{result.Metrics.ReadsMapped}");
            await writer.WriteLineAsync($"SoftClipRealignedReads\t{result.Metrics.ReadsRealigned}");
            await writer.WriteLineAsync($"RawVariantCalls\t{result.Metrics.VariantsCalled}");
            await writer.WriteLineAsync($"MergedVariantCalls\t{result.Metrics.VariantsFinal}");
            await writer.WriteLineAsync($"StructuralVariants\t{result.Metrics.StructuralVariants}");
            await writer.WriteLineAsync();
            await writer.WriteLineAsync("VariantType\tCount");

            foreach (var count in counts)
            {
                await writer.WriteLineAsync($"{count.Key}\t{count.Value}");
            }
        }

        private static string GetVariantType(LocalVariantResult variant)
        {
            if (variant.IsStructuralVariant)
            {
                return variant.SvType?.ToString() ?? "SV";
            }

            if (variant.IsInsertion)
            {
                return "Insertion";
            }

            if (variant.IsDeletion)
            {
                return "Deletion";
            }

            return "SNP";
        }

        private static bool IsGzip(string path)
        {
            return path.EndsWith(".gz", StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeSequenceId(string sequenceId)
        {
            return sequenceId.Split(' ', '\t', StringSplitOptions.RemoveEmptyEntries)[0];
        }

        private static void PrintUsage()
        {
            Console.WriteLine("Usage:");
            Console.WriteLine(
                "  dotnet run --project DnaAnalysisApp -- --reference <reference.fa|reference.fa.gz> --fastq <reads.fastq.gz> [options]");
            Console.WriteLine();
            Console.WriteLine("Required:");
            Console.WriteLine("  --reference <path>              FASTA or FASTA.GZ reference sequence file.");
            Console.WriteLine("  --fastq <path>                  Gzipped FASTQ file to analyze.");
            Console.WriteLine();
            Console.WriteLine("Optional:");
            Console.WriteLine("  --reference-id-contains <text>  Choose a FASTA record by partial ID match.");
            Console.WriteLine(
                "  --chromosome <name>             Override the output contig name used in the VCF/report.");
            Console.WriteLine("  --output-dir <path>             Output folder. Default: ./output");
            Console.WriteLine("  --output-prefix <name>          Output filename prefix. Default: variants");
            Console.WriteLine(
                "  --max-reads <int>               Stop after this many reads. Useful for quick smoke tests.");
            Console.WriteLine("  --min-alignment-score <int>     Default: 10");
            Console.WriteLine("  --min-variant-quality <int>     Default: 30");
            Console.WriteLine("  --disable-softclip-realign      Disable soft-clip realignment.");
            Console.WriteLine("  --enable-graph-sv               Run full-reference De Bruijn graph analysis.");
            Console.WriteLine("  --kmer-size <int>               Default: 15");
            Console.WriteLine("  --min-graph-coverage <int>      Default: 5");
            Console.WriteLine("  --graph-window-bp <int>         Default: 500");
            Console.WriteLine("  --help                          Show this help text.");
        }
    }

    internal sealed record AppOptions(
        string ReferencePath,
        string FastqPath,
        string? ReferenceIdContains,
        string? Chromosome,
        string OutputDirectory,
        string OutputPrefix,
        int? MaxReads,
        int MinAlignmentScore,
        int MinVariantQuality,
        bool EnableSoftClipRealignment,
        bool EnableGraphSvDetection,
        int KmerSize,
        int MinGraphCoverage,
        int GraphWindowBp,
        bool ShowHelp)
    {
        public static AppOptions Parse(string[] args)
        {
            string? referencePath = null;
            string? fastqPath = null;
            string? referenceIdContains = null;
            string? chromosome = null;
            var outputDirectory = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, "output"));
            var outputPrefix = "variants";
            int? maxReads = null;
            var minAlignmentScore = 10;
            var minVariantQuality = 30;
            var enableSoftClipRealignment = true;
            var enableGraphSvDetection = false;
            var kmerSize = 15;
            var minGraphCoverage = 5;
            var graphWindowBp = 500;
            var showHelp = false;

            for (var index = 0; index < args.Length; index++)
            {
                var arg = args[index];
                switch (arg)
                {
                    case "--reference":
                        referencePath = GetRequiredValue(args, ref index, arg);
                        break;
                    case "--fastq":
                        fastqPath = GetRequiredValue(args, ref index, arg);
                        break;
                    case "--reference-id-contains":
                        referenceIdContains = GetRequiredValue(args, ref index, arg);
                        break;
                    case "--chromosome":
                        chromosome = GetRequiredValue(args, ref index, arg);
                        break;
                    case "--output-dir":
                        outputDirectory = Path.GetFullPath(GetRequiredValue(args, ref index, arg));
                        break;
                    case "--output-prefix":
                        outputPrefix = GetRequiredValue(args, ref index, arg);
                        break;
                    case "--max-reads":
                        maxReads = int.Parse(GetRequiredValue(args, ref index, arg));
                        break;
                    case "--min-alignment-score":
                        minAlignmentScore = int.Parse(GetRequiredValue(args, ref index, arg));
                        break;
                    case "--min-variant-quality":
                        minVariantQuality = int.Parse(GetRequiredValue(args, ref index, arg));
                        break;
                    case "--disable-softclip-realign":
                        enableSoftClipRealignment = false;
                        break;
                    case "--enable-graph-sv":
                        enableGraphSvDetection = true;
                        break;
                    case "--kmer-size":
                        kmerSize = int.Parse(GetRequiredValue(args, ref index, arg));
                        break;
                    case "--min-graph-coverage":
                        minGraphCoverage = int.Parse(GetRequiredValue(args, ref index, arg));
                        break;
                    case "--graph-window-bp":
                        graphWindowBp = int.Parse(GetRequiredValue(args, ref index, arg));
                        break;
                    case "--help":
                    case "-h":
                        showHelp = true;
                        break;
                    default:
                        throw new ArgumentException($"Unknown argument: {arg}");
                }
            }

            if (showHelp)
            {
                return new AppOptions(
                    ReferencePath: string.Empty,
                    FastqPath: string.Empty,
                    ReferenceIdContains: referenceIdContains,
                    Chromosome: chromosome,
                    OutputDirectory: outputDirectory,
                    OutputPrefix: outputPrefix,
                    MaxReads: maxReads,
                    MinAlignmentScore: minAlignmentScore,
                    MinVariantQuality: minVariantQuality,
                    EnableSoftClipRealignment: enableSoftClipRealignment,
                    EnableGraphSvDetection: enableGraphSvDetection,
                    KmerSize: kmerSize,
                    MinGraphCoverage: minGraphCoverage,
                    GraphWindowBp: graphWindowBp,
                    ShowHelp: true);
            }

            if (string.IsNullOrWhiteSpace(referencePath))
            {
                throw new ArgumentException("Missing required argument: --reference");
            }

            if (string.IsNullOrWhiteSpace(fastqPath))
            {
                throw new ArgumentException("Missing required argument: --fastq");
            }

            return new AppOptions(
                ReferencePath: Path.GetFullPath(referencePath),
                FastqPath: Path.GetFullPath(fastqPath),
                ReferenceIdContains: referenceIdContains,
                Chromosome: chromosome,
                OutputDirectory: outputDirectory,
                OutputPrefix: outputPrefix,
                MaxReads: maxReads,
                MinAlignmentScore: minAlignmentScore,
                MinVariantQuality: minVariantQuality,
                EnableSoftClipRealignment: enableSoftClipRealignment,
                EnableGraphSvDetection: enableGraphSvDetection,
                KmerSize: kmerSize,
                MinGraphCoverage: minGraphCoverage,
                GraphWindowBp: graphWindowBp,
                ShowHelp: false);
        }

        private static string GetRequiredValue(string[] args, ref int index, string argName)
        {
            if (index + 1 >= args.Length)
            {
                throw new ArgumentException($"Missing value for {argName}");
            }

            index++;
            return args[index];
        }
    }
}
