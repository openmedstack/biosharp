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
            CreateAnnotateCommand()
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
}
