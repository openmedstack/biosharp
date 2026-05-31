namespace OpenMedStack.Preator;

using System;
using System.Collections.Generic;
using System.CommandLine;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Spectre.Console;

internal interface IPreatorInteractiveUi
{
    Task<int> Run(RootCommand rootCommand, CancellationToken cancellationToken);
}

internal sealed class PreatorInteractiveUi : IPreatorInteractiveUi
{
    private const string VariantCallInputModeKey = "variantcall-input-mode";
    private const string E2EInputModeKey = "e2e-input-mode";

    // Shared advanced variant-calling options reused by both `analysis` and `variantcall` specs
    private static readonly IReadOnlyList<InteractiveOptionSpec> VariantCallingAdvancedOptions =
    [
        new InteractiveOptionSpec("min-alignment-score", "--min-alignment-score", "Minimum alignment score.", PromptValueKind.Integer, DefaultValue: "10"),
        new InteractiveOptionSpec("min-variant-quality", "--min-variant-quality", "Minimum variant quality.", PromptValueKind.Integer, DefaultValue: "30"),
        new InteractiveOptionSpec("min-alternate-observation-count", "--min-alternate-observation-count", "Minimum supporting-read count for merged calls.", PromptValueKind.Integer, DefaultValue: "1"),
        new InteractiveOptionSpec("min-alternate-fraction", "--min-alternate-fraction", "Minimum alternate fraction for merged calls.", PromptValueKind.Double, DefaultValue: "0.0"),
        new InteractiveOptionSpec("disable-softclip-realign", "--disable-softclip-realign", "Disable soft-clip realignment.", PromptValueKind.Flag, DefaultFlagValue: false),
        new InteractiveOptionSpec("enable-graph-sv", "--enable-graph-sv", "Enable full-reference graph SV detection.", PromptValueKind.Flag, DefaultFlagValue: false),
        new InteractiveOptionSpec("kmer-size", "--kmer-size", "K-mer size for graph SV detection.", PromptValueKind.Integer, DefaultValue: "15"),
        new InteractiveOptionSpec("min-graph-coverage", "--min-graph-coverage", "Minimum graph coverage.", PromptValueKind.Integer, DefaultValue: "5"),
        new InteractiveOptionSpec("graph-window-bp", "--graph-window-bp", "Graph window size in bp.", PromptValueKind.Integer, DefaultValue: "500"),
        new InteractiveOptionSpec("max-cores", "--max-cores", "Maximum cores to use.", PromptValueKind.Integer, DefaultValue: "10")
    ];
    private readonly IAnsiConsole _console = AnsiConsole.Console;

    public async Task<int> Run(RootCommand rootCommand, CancellationToken cancellationToken)
    {
        var commands = CreateCommandSpecs();

        RenderWelcome(commands);

        var command = _console.Prompt(
            new SelectionPrompt<InteractiveCommandSpec>()
                .Title("Choose the [green]workflow[/] to run")
                .PageSize(commands.Count)
                .MoreChoicesText("[grey](Use the arrow keys to move)[/]")
                .UseConverter(spec => $"[green]{spec.Name}[/] [grey]- {Markup.Escape(spec.Description)}[/]")
                .AddChoices(commands));

        var answers = new InteractiveAnswerStore();

        _console.Write(new Rule($"[green]{command.Name}[/] parameters"));
        _console.MarkupLine($"[grey]{Markup.Escape(command.Description)}[/]");
        _console.WriteLine();

        PromptOptions(command.BasicOptions, answers);
        if (command.AdvancedOptions.Count > 0 && PromptForConfirmation("Configure advanced options?", false))
        {
            _console.Write(new Rule("[grey]Advanced options[/]"));
            PromptOptions(command.AdvancedOptions, answers);
        }

        var commandLine = string.Join(' ', command.BuildArguments(answers).Select(QuoteIfNeeded));
        _console.Write(
            new Panel(new Text(commandLine))
                .Header("Equivalent command")
                .Border(BoxBorder.Rounded)
                .BorderStyle(new Style(Color.Grey)));

        if (!PromptForConfirmation("Run this command now?", true))
        {
            _console.MarkupLine("[yellow]Canceled before execution.[/]");
            return 0;
        }

        _console.WriteLine();

        var parseResult = rootCommand.Parse(command.BuildArguments(answers));
        if (parseResult.Errors.Count > 0)
        {
            foreach (var error in parseResult.Errors)
            {
                _console.MarkupLine($"[red]{Markup.Escape(error.Message)}[/]");
            }

            return 1;
        }

        return await parseResult.InvokeAsync(null!, cancellationToken);
    }

    internal static IReadOnlyList<InteractiveCommandSpec> CreateCommandSpecs()
    {
        return
        [
            new InteractiveCommandSpec(
                Name: "bcl",
                Description: "Convert Illumina BCL data to FASTQ",
                BasicOptions:
                [
                    new InteractiveOptionSpec("input", "--input", "Input BCL run directory.", PromptValueKind.Text, IsRequired: true),
                    new InteractiveOptionSpec("output", "--output", "Output FASTQ directory.", PromptValueKind.Text, DefaultValue: Environment.CurrentDirectory),
                    new InteractiveOptionSpec("lanes", "--lanes", "Comma-separated lanes or * for all lanes.", PromptValueKind.Text, DefaultValue: "1"),
                    new InteractiveOptionSpec("readstructure", "--readstructure", "Read structure string, for example 26T8B98T.", PromptValueKind.Text)
                ],
                AdvancedOptions:
                [
                ],
                BuildArguments: answers => BuildStandardArguments(
                    "bcl",
                    ("--input", answers.GetRequiredValue("input")),
                    ("--output", answers.GetValue("output")),
                    ("--lanes", answers.GetValue("lanes")),
                    ("--readstructure", answers.GetValue("readstructure")))),
            new InteractiveCommandSpec(
                Name: "variantcall",
                Description: "Run the variant call pipeline",
                BasicOptions:
                [
                    new InteractiveOptionSpec(VariantCallInputModeKey, "Input mode", "Choose whether reads come from FASTQ or FASTA.", PromptValueKind.Choice, DefaultValue: "fastq", choices: ["fastq", "fasta"]),
                    new InteractiveOptionSpec("reference", "--reference", "Reference FASTA or FASTA.GZ file.", PromptValueKind.Text, IsRequired: true),
                    new InteractiveOptionSpec("reads-path", "Read input path", "Read file matching the selected input mode.", PromptValueKind.Text, IsRequired: true),
                    new InteractiveOptionSpec("output", "--output", "Output directory.", PromptValueKind.Text, DefaultValue: Environment.CurrentDirectory),
                    new InteractiveOptionSpec("output-prefix", "--output-prefix", "Output filename prefix.", PromptValueKind.Text, DefaultValue: "variants"),
                    new InteractiveOptionSpec("max-reads", "--max-reads", "Optional read limit for smoke tests.", PromptValueKind.Integer),
                    new InteractiveOptionSpec("reference-id-contains", "--reference-id-contains", "Select a FASTA record by partial ID match.", PromptValueKind.Text),
                    new InteractiveOptionSpec("chromosome", "--chromosome", "Override the output contig/chromosome name.", PromptValueKind.Text)
                ],
                AdvancedOptions: VariantCallingAdvancedOptions,
                BuildArguments: BuildAnalysisArguments),
            new InteractiveCommandSpec(
                Name: "e2e",
                Description: "Read FASTA/FASTQ, call variants, annotate and produce a clinical report in one pass",
                BasicOptions:
                [
                    new InteractiveOptionSpec(E2EInputModeKey, "Input mode", "Choose whether reads come from FASTQ or FASTA.", PromptValueKind.Choice, DefaultValue: "fastq", choices: ["fastq", "fasta"]),
                    new InteractiveOptionSpec("reference", "--reference", "Reference FASTA or FASTA.GZ file.", PromptValueKind.Text, IsRequired: true),
                    new InteractiveOptionSpec("reads-path", "Read input path", "Read file matching the selected input mode.", PromptValueKind.Text, IsRequired: true),
                    new InteractiveOptionSpec("database", "--database", "Transcript annotation SQLite database file.", PromptValueKind.Text, IsRequired: true),
                    new InteractiveOptionSpec("output", "--output", "Output directory.", PromptValueKind.Text, DefaultValue: Environment.CurrentDirectory),
                    new InteractiveOptionSpec("output-prefix", "--output-prefix", "Output filename prefix.", PromptValueKind.Text, DefaultValue: "variants")
                ],
                AdvancedOptions:
                [
                    new InteractiveOptionSpec("max-reads", "--max-reads", "Optional read limit for smoke tests.", PromptValueKind.Integer),
                    new InteractiveOptionSpec("reference-id-contains", "--reference-id-contains", "Select a FASTA record by partial ID match.", PromptValueKind.Text),
                    new InteractiveOptionSpec("chromosome", "--chromosome", "Override the output contig/chromosome name.", PromptValueKind.Text),
                    ..VariantCallingAdvancedOptions,
                    new InteractiveOptionSpec("transcript-id", "--transcript-id", "Restrict annotation to a single transcript ID.", PromptValueKind.Text),
                    new InteractiveOptionSpec("min-quality", "--min-quality", "Minimum QUAL threshold required before annotation.", PromptValueKind.Double, DefaultValue: "0")
                ],
                BuildArguments: BuildE2EArguments),
            new InteractiveCommandSpec(
                Name: "annotate",
                Description: "Annotate variants in a VCF using a transcript SQLite database",
                BasicOptions:
                [
                    new InteractiveOptionSpec("vcf", "--vcf", "Input VCF or VCF.GZ file.", PromptValueKind.Text, IsRequired: true),
                    new InteractiveOptionSpec("database", "--database", "Transcript annotation SQLite database file.", PromptValueKind.Text, IsRequired: true),
                    new InteractiveOptionSpec("output-dir", "--output-dir", "Output directory.", PromptValueKind.Text, DefaultValue: Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, "output"))),
                    new InteractiveOptionSpec("output-prefix", "--output-prefix", "Output filename prefix.", PromptValueKind.Text, DefaultValue: "annotated-variants"),
                    new InteractiveOptionSpec("transcript-id", "--transcript-id", "Restrict annotation to a single transcript ID.", PromptValueKind.Text),
                    new InteractiveOptionSpec("min-quality", "--min-quality", "Minimum QUAL threshold required before annotation.", PromptValueKind.Double, DefaultValue: "0")
                ],
                AdvancedOptions:
                [
                ],
                BuildArguments: answers => BuildStandardArguments(
                    "annotate",
                    ("--vcf", answers.GetRequiredValue("vcf")),
                    ("--database", answers.GetRequiredValue("database")),
                    ("--output-dir", answers.GetValue("output-dir")),
                    ("--output-prefix", answers.GetValue("output-prefix")),
                    ("--transcript-id", answers.GetValue("transcript-id")),
                    ("--min-quality", answers.GetValue("min-quality")))),
            new InteractiveCommandSpec(
                Name: "trim",
                Description: "Trim adapter sequences from FASTQ reads (equivalent to fastp / cutadapt)",
                BasicOptions:
                [
                    new InteractiveOptionSpec("fastq", "--fastq", "Input FASTQ or FASTQ.GZ file.", PromptValueKind.Text, IsRequired: true),
                    new InteractiveOptionSpec("adapter", "--adapter", "Adapter sequence to trim from reads.", PromptValueKind.Text, IsRequired: true),
                    new InteractiveOptionSpec("min-length", "--min-length", "Minimum read length after trimming. Shorter reads are discarded.", PromptValueKind.Integer, DefaultValue: "20"),
                    new InteractiveOptionSpec("max-mismatches", "--max-mismatches", "Maximum mismatches allowed during adapter matching.", PromptValueKind.Integer, DefaultValue: "2"),
                    new InteractiveOptionSpec("output", "--output", "Output directory.", PromptValueKind.Text, DefaultValue: Environment.CurrentDirectory),
                    new InteractiveOptionSpec("output-prefix", "--output-prefix", "Output filename prefix.", PromptValueKind.Text, DefaultValue: "trimmed")
                ],
                AdvancedOptions:
                [
                    new InteractiveOptionSpec("max-reads", "--max-reads", "Stop after this many reads (useful for smoke tests).", PromptValueKind.Integer)
                ],
                BuildArguments: answers => BuildStandardArguments(
                    "trim",
                    ("--fastq", answers.GetRequiredValue("fastq")),
                    ("--adapter", answers.GetRequiredValue("adapter")),
                    ("--min-length", answers.GetValue("min-length")),
                    ("--max-mismatches", answers.GetValue("max-mismatches")),
                    ("--output", answers.GetValue("output")),
                    ("--output-prefix", answers.GetValue("output-prefix")),
                    ("--max-reads", answers.GetValue("max-reads")))),
            new InteractiveCommandSpec(
                Name: "qc",
                Description: "Compute FastQC-equivalent quality metrics from a FASTQ file",
                BasicOptions:
                [
                    new InteractiveOptionSpec("fastq", "--fastq", "Input FASTQ or FASTQ.GZ file.", PromptValueKind.Text, IsRequired: true),
                    new InteractiveOptionSpec("output-dir", "--output-dir", "Output directory.", PromptValueKind.Text, DefaultValue: Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, "output"))),
                    new InteractiveOptionSpec("output-prefix", "--output-prefix", "Output filename prefix.", PromptValueKind.Text, DefaultValue: "qc-report")
                ],
                AdvancedOptions:
                [
                    new InteractiveOptionSpec("adapter", "--adapter", "Optional adapter sequence for adapter-content analysis.", PromptValueKind.Text),
                    new InteractiveOptionSpec("max-reads", "--max-reads", "Stop after this many reads (useful for smoke tests).", PromptValueKind.Integer)
                ],
                BuildArguments: answers => BuildStandardArguments(
                    "qc",
                    ("--fastq", answers.GetRequiredValue("fastq")),
                    ("--output-dir", answers.GetValue("output-dir")),
                    ("--output-prefix", answers.GetValue("output-prefix")),
                    ("--adapter", answers.GetValue("adapter")),
                    ("--max-reads", answers.GetValue("max-reads")))),
            new InteractiveCommandSpec(
                Name: "align",
                Description: "Align FASTQ reads against a reference using FM-index + Smith-Waterman (like bwa-mem)",
                BasicOptions:
                 [
                    new InteractiveOptionSpec("reference", "--reference", "Reference FASTA or FASTA.GZ file.", PromptValueKind.Text, IsRequired: true),
                    new InteractiveOptionSpec("reads", "--fastq", "FASTQ or FASTQ.GZ file to align.", PromptValueKind.Text, IsRequired: true),
                    new InteractiveOptionSpec("output", "--output", "Output directory.", PromptValueKind.Text, DefaultValue: Environment.CurrentDirectory),
                    new InteractiveOptionSpec("output-prefix", "--output-prefix", "Output filename prefix.", PromptValueKind.Text, DefaultValue: "aligned"),
                    new InteractiveOptionSpec("max-reads", "--max-reads", "Stop after this many reads (useful for smoke tests).", PromptValueKind.Integer),
                 ],
                AdvancedOptions:
                 [
                    new InteractiveOptionSpec("min-alignment-score", "--min-alignment-score", "Minimum alignment score.", PromptValueKind.Integer, DefaultValue: "10"),
                    new InteractiveOptionSpec("min-seed-len", "--min-seed-len", "Minimum seed length for FM-index seeding.", PromptValueKind.Integer, DefaultValue: "19"),
                    new InteractiveOptionSpec("max-seed-hits", "--max-seed-hits", "Discard seeds mapping to more than this many positions.", PromptValueKind.Integer, DefaultValue: "64"),
                    new InteractiveOptionSpec("seed-step", "--seed-step", "Step size between sampled seeds (1=every position).", PromptValueKind.Integer, DefaultValue: "1"),
                    new InteractiveOptionSpec("window-padding", "--window-padding", "Extra bases on both sides of a candidate window.", PromptValueKind.Integer, DefaultValue: "64"),
                    new InteractiveOptionSpec("max-windows", "--max-windows", "Max candidate windows per read.", PromptValueKind.Integer, DefaultValue: "8"),
                    new InteractiveOptionSpec("max-cores", "--max-cores", "Maximum cores to use.", PromptValueKind.Integer, DefaultValue: "10")
                 ],
                BuildArguments: answers => BuildStandardArguments(
                     "align",
                     ("--reference", answers.GetRequiredValue("reference")),
                     ("--fastq", answers.GetRequiredValue("reads")),
                     ("--output", answers.GetValue("output")),
                     ("--output-prefix", answers.GetValue("output-prefix")),
                     ("--max-reads", answers.GetValue("max-reads")),
                     ("--min-alignment-score", answers.GetValue("min-alignment-score")),
                     ("--min-seed-len", answers.GetValue("min-seed-len")),
                     ("--max-seed-hits", answers.GetValue("max-seed-hits")),
                     ("--seed-step", answers.GetValue("seed-step")),
                     ("--window-padding", answers.GetValue("window-padding")),
                     ("--max-windows", answers.GetValue("max-windows")),
                     ("--max-cores", answers.GetValue("max-cores"))))
         ];
    }

    private static IReadOnlyList<string> BuildAnalysisArguments(InteractiveAnswerStore answers)
    {
        var readsArgumentName = string.Equals(
            answers.GetRequiredValue(VariantCallInputModeKey),
            "fasta",
            StringComparison.OrdinalIgnoreCase)
            ? "--fasta"
            : "--fastq";

        return BuildStandardArguments(
            "variantcall",
            ("--reference", answers.GetRequiredValue("reference")),
            (readsArgumentName, answers.GetRequiredValue("reads-path")),
            ("--output", answers.GetValue("output")),
            ("--output-prefix", answers.GetValue("output-prefix")),
            ("--max-reads", answers.GetValue("max-reads")),
            ("--reference-id-contains", answers.GetValue("reference-id-contains")),
            ("--chromosome", answers.GetValue("chromosome")),
            ("--min-alignment-score", answers.GetValue("min-alignment-score")),
            ("--min-variant-quality", answers.GetValue("min-variant-quality")),
            ("--min-alternate-observation-count", answers.GetValue("min-alternate-observation-count")),
            ("--min-alternate-fraction", answers.GetValue("min-alternate-fraction")),
            ("--kmer-size", answers.GetValue("kmer-size")),
            ("--min-graph-coverage", answers.GetValue("min-graph-coverage")),
            ("--graph-window-bp", answers.GetValue("graph-window-bp")),
            ("--max-cores", answers.GetValue("max-cores")),
            ("--disable-softclip-realign", answers.GetFlag("disable-softclip-realign")),
            ("--enable-graph-sv", answers.GetFlag("enable-graph-sv")));
    }

    private static IReadOnlyList<string> BuildE2EArguments(InteractiveAnswerStore answers)
    {
        var readsArgumentName = string.Equals(
            answers.GetRequiredValue(E2EInputModeKey),
            "fasta",
            StringComparison.OrdinalIgnoreCase)
            ? "--fasta"
            : "--fastq";

        return BuildStandardArguments(
            "e2e",
            ("--reference", answers.GetRequiredValue("reference")),
            (readsArgumentName, answers.GetRequiredValue("reads-path")),
            ("--database", answers.GetRequiredValue("database")),
            ("--output", answers.GetValue("output")),
            ("--output-prefix", answers.GetValue("output-prefix")),
            ("--max-reads", answers.GetValue("max-reads")),
            ("--reference-id-contains", answers.GetValue("reference-id-contains")),
            ("--chromosome", answers.GetValue("chromosome")),
            ("--min-alignment-score", answers.GetValue("min-alignment-score")),
            ("--min-variant-quality", answers.GetValue("min-variant-quality")),
            ("--min-alternate-observation-count", answers.GetValue("min-alternate-observation-count")),
            ("--min-alternate-fraction", answers.GetValue("min-alternate-fraction")),
            ("--kmer-size", answers.GetValue("kmer-size")),
            ("--min-graph-coverage", answers.GetValue("min-graph-coverage")),
            ("--graph-window-bp", answers.GetValue("graph-window-bp")),
            ("--max-cores", answers.GetValue("max-cores")),
            ("--disable-softclip-realign", answers.GetFlag("disable-softclip-realign")),
            ("--enable-graph-sv", answers.GetFlag("enable-graph-sv")),
            ("--transcript-id", answers.GetValue("transcript-id")),
            ("--min-quality", answers.GetValue("min-quality")));
    }

    internal static IReadOnlyList<string> BuildStandardArguments(
        string commandName,
        params (string Name, object? Value)[] arguments)
    {
        var tokens = new List<string> { commandName };

        foreach (var (name, value) in arguments)
        {
            switch (value)
            {
                case null:
                    continue;
                case bool enabled when enabled:
                    tokens.Add(name);
                    break;
                case bool:
                    continue;
                case string text when string.IsNullOrWhiteSpace(text):
                    continue;
                default:
                    tokens.Add(name);
                    tokens.Add(Convert.ToString(value, CultureInfo.InvariantCulture)!);
                    break;
            }
        }

        return tokens;
    }

    private void RenderWelcome(IReadOnlyList<InteractiveCommandSpec> commands)
    {
        _console.Write(
            new FigletText("Preator")
                .Color(Color.CornflowerBlue));
        _console.Write(
            new Panel("Pick a workflow, answer a few prompts, and Preator will build and run the command for you.")
                .Border(BoxBorder.Rounded)
                .Header("Interactive mode"));

        var table = new Table().Border(TableBorder.Rounded).AddColumn("Command").AddColumn("Description");
        foreach (var command in commands)
        {
            table.AddRow($"[green]{command.Name}[/]", Markup.Escape(command.Description));
        }

        _console.Write(table);
        _console.WriteLine();
    }

    private void PromptOptions(IEnumerable<InteractiveOptionSpec> options, InteractiveAnswerStore answers)
    {
        foreach (var option in options)
        {
            switch (option.Kind)
            {
                case PromptValueKind.Flag:
                    answers.SetFlag(option.Key, PromptForConfirmation($"{option.Label}: {option.Description}", option.DefaultFlagValue ?? false));
                    break;
                case PromptValueKind.Choice:
                    answers.SetValue(option.Key, PromptForChoice(option));
                    break;
                default:
                    var value = PromptForValue(option);
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        answers.SetValue(option.Key, value);
                    }
                    break;
            }
        }
    }

    private string PromptForChoice(InteractiveOptionSpec option)
    {
        var prompt = new SelectionPrompt<string>()
            .Title($"{Markup.Escape(option.Label)}: [grey]{Markup.Escape(option.Description)}[/]")
            .PageSize(Math.Max(option.Choices.Count, 3))
            .AddChoices(option.Choices);

        return _console.Prompt(prompt);
    }

    private string PromptForValue(InteractiveOptionSpec option)
    {
        var prompt = new TextPrompt<string>($"{Markup.Escape(option.Label)}: [grey]{Markup.Escape(option.Description)}[/]");
        if (!option.IsRequired)
        {
            prompt.AllowEmpty();
        }

        if (!string.IsNullOrWhiteSpace(option.DefaultValue))
        {
            prompt.DefaultValue(option.DefaultValue!);
            prompt.ShowDefaultValue();
        }

        prompt.Validate(value =>
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return option.IsRequired
                    ? ValidationResult.Error($"[red]{Markup.Escape(option.Label)} is required.[/]")
                    : ValidationResult.Success();
            }

            return option.Kind switch
            {
                PromptValueKind.Integer when !int.TryParse(value, out _) =>
                    ValidationResult.Error("[red]Enter a whole number.[/]"),
                PromptValueKind.Double when !double.TryParse(value, out _) =>
                    ValidationResult.Error("[red]Enter a valid number.[/]"),
                _ => ValidationResult.Success()
            };
        });

        return _console.Prompt(prompt).Trim();
    }

    private bool PromptForConfirmation(string title, bool defaultValue)
    {
        var yesLabel = defaultValue ? "Yes (default)" : "Yes";
        var noLabel = defaultValue ? "No" : "No (default)";
        var orderedChoices = defaultValue
            ? new[] { yesLabel, noLabel }
            : new[] { noLabel, yesLabel };

        var result = _console.Prompt(
            new SelectionPrompt<string>()
                .Title(title)
                .AddChoices(orderedChoices));

        return result.StartsWith("Yes", StringComparison.Ordinal);
    }

    private static string QuoteIfNeeded(string token)
    {
        return token.Contains(' ', StringComparison.Ordinal)
            ? $"\"{token}\""
            : token;
    }
}

internal sealed record InteractiveCommandSpec(
    string Name,
    string Description,
    IReadOnlyList<InteractiveOptionSpec> BasicOptions,
    IReadOnlyList<InteractiveOptionSpec> AdvancedOptions,
    Func<InteractiveAnswerStore, IReadOnlyList<string>> BuildArguments);

internal sealed record InteractiveOptionSpec(
    string Key,
    string Label,
    string Description,
    PromptValueKind Kind,
    bool IsRequired = false,
    string? DefaultValue = null,
    bool? DefaultFlagValue = null,
    IReadOnlyList<string>? choices = null)
{
    public IReadOnlyList<string> Choices { get; } = choices ?? [];
}

internal enum PromptValueKind
{
    Text,
    Integer,
    Double,
    Flag,
    Choice
}
