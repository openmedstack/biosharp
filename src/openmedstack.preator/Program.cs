using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("OpenMedStack.Preator.Tests")]

namespace OpenMedStack.Preator;

using System.CommandLine;
using System.Threading;
using System.Threading.Tasks;

internal static class Program
{
    private static readonly IPreatorInteractiveUi InteractiveUi = new PreatorInteractiveUi();

    private static Task<int> Main(string[] args)
    {
        return Invoke(args, CancellationToken.None);
    }

    internal static Task<int> Invoke(
        string[] args,
        CancellationToken cancellationToken,
        IPreatorInteractiveUi? interactiveUi = null)
    {
        var rootCommand = CreateRootCommand();
        if (args.Length == 0)
        {
            return (interactiveUi ?? InteractiveUi).Run(rootCommand, cancellationToken);
        }

        return rootCommand.Parse(args).InvokeAsync(null!, cancellationToken);
    }

    internal static RootCommand CreateRootCommand()
    {
        return new RootCommand("Preator - BioSharp command-line tools")
        {
            CreateAlignmentCommand(),
            CreateAnnotateCommand(),
            CreateBclCommand(),
            CreateCoverageCommand(),
            CreateE2ECommand(),
            CreateIndexCommand(),
            CreateMarkDupCommand(),
            CreateRepeatMaskCommand(),
            CreateVariantCallCommand(),
            CreateQcCommand(),
            CreateTrimCommand(),
        };
    }

    private static Command CreateBclCommand()
    {
        var command = new Command("bcl", "Convert Illumina BCL data to FASTQ")
        {
            PreatorCommandOptions.InputOption,
            PreatorCommandOptions.OutputOption,
            PreatorCommandOptions.ReadStructureOption,
            PreatorCommandOptions.LanesOption
        };

        command.SetAction(BclCommand.Invoke);
        return command;
    }

    private static Command CreateVariantCallCommand()
    {
        var command = new Command("variantcall", "Run the variant call pipeline")
        {
            PreatorCommandOptions.ReferenceOption,
            PreatorCommandOptions.FastqOption,
            PreatorCommandOptions.FastaOption,
            PreatorCommandOptions.BamOption,
            PreatorCommandOptions.ReferenceIdContainsOption,
            PreatorCommandOptions.ChromosomeOption,
            PreatorCommandOptions.OutputOption,
            PreatorCommandOptions.OutputPrefixOption,
            PreatorCommandOptions.MaxReadsOption,
            PreatorCommandOptions.MinAlignmentScoreOption,
            PreatorCommandOptions.MinVariantQualityOption,
            PreatorCommandOptions.MinAlternateObservationCountOption,
            PreatorCommandOptions.MinAlternateFractionOption,
            PreatorCommandOptions.DisableSoftclipRealignOption,
            PreatorCommandOptions.EnableGraphSvOption,
            PreatorCommandOptions.KmerSizeOption,
            PreatorCommandOptions.MinGraphCoverageOption,
            PreatorCommandOptions.GraphWindowBpOption,
            PreatorCommandOptions.MaxCoresOption
        };

        command.Validators.Add(result =>
        {
            var hasFastq = result.GetValue(PreatorCommandOptions.FastqOption) is not null;
            var hasFasta = result.GetValue(PreatorCommandOptions.FastaOption) is not null;
            var hasBam = result.GetValue(PreatorCommandOptions.BamOption) is not null;
            if (!hasFastq && !hasFasta && !hasBam)
            {
                result.AddError("Missing required argument: --fastq, --fasta, or --bam");
            }
        });

        command.SetAction(VariantCallCommand.Invoke);
        return command;
    }

    private static Command CreateE2ECommand()
    {
        var command = new Command(
            "e2e",
            "Run end-to-end: read FASTA/FASTQ/BAM, trim, align, mark duplicates, call variants, annotate and produce a clinical report")
        {
            PreatorCommandOptions.ReferenceOption,
            PreatorCommandOptions.FastqOption,
            PreatorCommandOptions.FastaOption,
            PreatorCommandOptions.BamOption,
            PreatorCommandOptions.ReferenceIdContainsOption,
            PreatorCommandOptions.ChromosomeOption,
            PreatorCommandOptions.OutputOption,
            PreatorCommandOptions.OutputPrefixOption,
            PreatorCommandOptions.MaxReadsOption,
            PreatorCommandOptions.MinAlignmentScoreOption,
            PreatorCommandOptions.MinVariantQualityOption,
            PreatorCommandOptions.MinAlternateObservationCountOption,
            PreatorCommandOptions.MinAlternateFractionOption,
            PreatorCommandOptions.DisableSoftclipRealignOption,
            PreatorCommandOptions.EnableGraphSvOption,
            PreatorCommandOptions.KmerSizeOption,
            PreatorCommandOptions.MinGraphCoverageOption,
            PreatorCommandOptions.GraphWindowBpOption,
            PreatorCommandOptions.MaxCoresOption,
            // Adapter trimming
            PreatorCommandOptions.AdapterOption,
            PreatorCommandOptions.MinLengthOption,
            PreatorCommandOptions.MaxMismatchesOption,
            // Alignment
            PreatorCommandOptions.MinSeedLenOption,
            PreatorCommandOptions.MaxSeedHitsThresholdOption,
            PreatorCommandOptions.SeedStepOption,
            PreatorCommandOptions.WindowPaddingOption,
            PreatorCommandOptions.MaxCandidateWindowsPerReadOption,
            // Duplicate marking
            PreatorCommandOptions.OpticalPixelDistanceOption,
            // Annotation (optional)
            PreatorCommandOptions.OptionalDatabaseOption,
            PreatorCommandOptions.TranscriptIdOption,
            PreatorCommandOptions.MinQualityOption
        };

        command.Validators.Add(result =>
        {
            var hasFastq = result.GetValue(PreatorCommandOptions.FastqOption) is not null;
            var hasFasta = result.GetValue(PreatorCommandOptions.FastaOption) is not null;
            var hasBam = result.GetValue(PreatorCommandOptions.BamOption) is not null;
            if (!hasFastq && !hasFasta && !hasBam)
            {
                result.AddError("Missing required argument: --fastq, --fasta, or --bam");
            }
        });

        command.SetAction(E2ECommand.Invoke);
        return command;
    }

    private static Command CreateAnnotateCommand()
    {
        var command = new Command("annotate", "Annotate variants in a VCF using a transcript SQLite database")
        {
            PreatorCommandOptions.VcfOption,
            PreatorCommandOptions.DatabaseOption,
            PreatorCommandOptions.TranscriptIdOption,
            PreatorCommandOptions.MinQualityOption,
            PreatorCommandOptions.OutputDirOption,
            PreatorCommandOptions.OutputPrefixOption
        };

        command.SetAction(AnnotateCommand.Invoke);
        return command;
    }

    private static Command CreateTrimCommand()
    {
        var command = new Command("trim", "Trim adapter sequences from FASTQ reads")
        {
            PreatorCommandOptions.FastqRequiredOption,
            PreatorCommandOptions.AdapterOption,
            PreatorCommandOptions.MinLengthOption,
            PreatorCommandOptions.MaxMismatchesOption,
            PreatorCommandOptions.MaxReadsOption,
            PreatorCommandOptions.OutputOption,
            PreatorCommandOptions.OutputPrefixOption
        };

        command.Validators.Add(result =>
        {
            if (result.GetValue(PreatorCommandOptions.AdapterOption) is null)
            {
                result.AddError("Missing required argument: --adapter");
            }
        });

        command.SetAction(TrimCommand.Invoke);
        return command;
    }

    private static Command CreateQcCommand()
    {
        var command = new Command("qc", "Compute FastQC-equivalent quality metrics from a FASTQ file")
        {
            PreatorCommandOptions.FastqRequiredOption,
            PreatorCommandOptions.AdapterOption,
            PreatorCommandOptions.MaxReadsOption,
            PreatorCommandOptions.OutputDirOption,
            PreatorCommandOptions.OutputPrefixOption
        };

        command.SetAction(QcCommand.Invoke);
        return command;
    }

    private static Command CreateAlignmentCommand()
    {
        var command = new Command(
            "align",
            "Align FASTQ reads against a reference FASTA using FM-index seeding + Smith-Waterman (equivalent to bwa-mem), or reformat BAM to SAM")
        {
            PreatorCommandOptions.ReferenceOption,
            PreatorCommandOptions.FastqOption,
            PreatorCommandOptions.BamOption,
            PreatorCommandOptions.PreloadIndexOption,
            PreatorCommandOptions.OutputOption,
            PreatorCommandOptions.MaxReadsOption,
            PreatorCommandOptions.MinAlignmentScoreOption,
            PreatorCommandOptions.MinSeedLenOption,
            PreatorCommandOptions.MaxSeedHitsThresholdOption,
            PreatorCommandOptions.SeedStepOption,
            PreatorCommandOptions.WindowPaddingOption,
            PreatorCommandOptions.MaxCandidateWindowsPerReadOption,
            PreatorCommandOptions.MaxCoresOption,
            PreatorCommandOptions.OutputPrefixOption
        };

        command.Validators.Add(result =>
        {
            var hasFastq = result.GetValue(PreatorCommandOptions.FastqOption) is not null;
            var hasBam = result.GetValue(PreatorCommandOptions.BamOption) is not null;
            if (!hasFastq && !hasBam)
            {
                result.AddError("Missing required argument: --fastq or --bam");
            }
        });

        command.SetAction(AlignmentCommand.Invoke);
        return command;
    }

    private static Command CreateIndexCommand()
    {
        var command = new Command(
            "index",
            "Build an FM-index (and optionally a k-mer reference index) from a FASTA reference or BAM read set")
        {
            PreatorCommandOptions.FastaOption,
            PreatorCommandOptions.BamOption,
            PreatorCommandOptions.ReferenceIdContainsOption,
            PreatorCommandOptions.FmSampleRateOption,
            PreatorCommandOptions.AlsoBuildReferenceIndexOption,
            PreatorCommandOptions.OutputOption,
            PreatorCommandOptions.OutputPrefixOption
        };

        command.Validators.Add(result =>
        {
            var hasFasta = result.GetValue(PreatorCommandOptions.FastaOption) is not null;
            var hasBam = result.GetValue(PreatorCommandOptions.BamOption) is not null;
            if (!hasFasta && !hasBam)
            {
                result.AddError("Missing required argument: --fasta or --bam");
            }
        });

        command.SetAction(IndexCommand.Invoke);
        return command;
    }

    private static Command CreateCoverageCommand()
    {
        var command = new Command("coverage", "Compute per-position and summary read depth from a sorted BAM file (equivalent to samtools depth)")
        {
            PreatorCommandOptions.BamRequiredOption,
            PreatorCommandOptions.BedOption,
            PreatorCommandOptions.WriteDepthTsvOption,
            PreatorCommandOptions.OutputOption,
            PreatorCommandOptions.OutputPrefixOption
        };

        command.SetAction(CoverageCommand.Invoke);
        return command;
    }

    private static Command CreateMarkDupCommand()    {
        var command = new Command("markdup", "Mark PCR and optical duplicate reads in a sorted BAM file")
        {
            PreatorCommandOptions.BamRequiredOption,
            PreatorCommandOptions.OutputOption,
            PreatorCommandOptions.OutputPrefixOption,
            PreatorCommandOptions.OpticalPixelDistanceOption
        };

        command.SetAction(MarkDupCommand.Invoke);
        return command;
    }

    private static Command CreateRepeatMaskCommand()
    {
        var command = new Command("repeatmask", "Mask repeat sequences in a FASTA file using a repeat library")
        {
            PreatorCommandOptions.FastaRequiredOption,
            PreatorCommandOptions.LibraryOption,
            PreatorCommandOptions.MinMotifLengthOption,
            PreatorCommandOptions.OutputOption,
            PreatorCommandOptions.OutputPrefixOption
        };

        command.SetAction(RepeatMaskCommand.Invoke);
        return command;
    }
}
