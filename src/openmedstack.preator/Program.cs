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
            CreateBclCommand(),
            CreateAnalysisCommand(),
            CreateE2ECommand(),
            CreateAnnotateCommand(),
            CreateTrimCommand(),
            CreateQcCommand(),
            CreateVariantCallCommand()
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

    private static Command CreateAnalysisCommand()
    {
        var command = new Command("analysis", "Run the DNA analysis pipeline")
        {
            PreatorCommandOptions.ReferenceOption,
            PreatorCommandOptions.FastqOption,
            PreatorCommandOptions.FastaOption,
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
            if (!hasFastq && !hasFasta)
            {
                result.AddError("Missing required argument: --fastq or --fasta");
            }
        });

        command.SetAction(AnalysisCommand.Invoke);
        return command;
    }

    private static Command CreateE2ECommand()
    {
        var command = new Command(
            "e2e",
            "Run end-to-end: read FASTA/FASTQ, call variants, annotate and produce a clinical report")
        {
            PreatorCommandOptions.ReferenceOption,
            PreatorCommandOptions.FastqOption,
            PreatorCommandOptions.FastaOption,
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
            PreatorCommandOptions.DatabaseOption,
            PreatorCommandOptions.TranscriptIdOption,
            PreatorCommandOptions.MinQualityOption
        };

        command.Validators.Add(result =>
        {
            var hasFastq = result.GetValue(PreatorCommandOptions.FastqOption) is not null;
            var hasFasta = result.GetValue(PreatorCommandOptions.FastaOption) is not null;
            if (!hasFastq && !hasFasta)
            {
                result.AddError("Missing required argument: --fastq or --fasta");
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

    private static Command CreateVariantCallCommand()
    {
        var command = new Command(
            "variantcall",
            "Call variants from a sorted BAM file against a reference (equivalent to freebayes / bcftools call)")
        {
            PreatorCommandOptions.BamOption,
            PreatorCommandOptions.ReferenceOption,
            PreatorCommandOptions.ReferenceIdContainsOption,
            PreatorCommandOptions.ChromosomeOption,
            PreatorCommandOptions.OutputOption,
            PreatorCommandOptions.OutputPrefixOption,
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

        command.SetAction(VariantCallCommand.Invoke);
        return command;
    }
}
