namespace OpenMedStack.Preator.Tests;

using System.Threading;
using System.Threading.Tasks;
using Xunit;

public sealed class ProgramInteractiveModeTests
{
    [Fact]
    public async Task InvokeAsync_WithoutArguments_UsesInteractiveUi()
    {
        var ui = new FakeInteractiveUi(37);

        var exitCode = await Program.Invoke([], CancellationToken.None, ui);

        Assert.Equal(37, exitCode);
        Assert.Equal(1, ui.CallCount);
    }

    [Fact]
    public async Task InvokeAsync_WithExplicitHelp_DoesNotUseInteractiveUi()
    {
        var ui = new FakeInteractiveUi(99);

        var exitCode = await Program.Invoke(["--help"], CancellationToken.None, ui);

        Assert.Equal(0, exitCode);
        Assert.Equal(0, ui.CallCount);
    }

    [Fact]
    public void BuildAnalysisArguments_UsesSelectedReadInputMode()
    {
        var spec = GetSpec("analysis");
        var answers = new InteractiveAnswerStore();
        answers.SetValue("analysis-input-mode", "fasta");
        answers.SetValue("reference", "reference.fa.gz");
        answers.SetValue("reads-path", "reads.fa.gz");
        answers.SetValue("output", "output");
        answers.SetValue("output-prefix", "variants");
        answers.SetValue("max-reads", "10");

        var arguments = spec.BuildArguments(answers);

        Assert.Equal(
            ["analysis", "--reference", "reference.fa.gz", "--fasta", "reads.fa.gz", "--output", "output", "--output-prefix", "variants", "--max-reads", "10"],
            arguments);
    }

    [Fact]
    public void BuildE2EArguments_IncludesDisableSoftclipFlagWhenEnabled()
    {
        var spec = GetSpec("e2e");
        var answers = new InteractiveAnswerStore();
        answers.SetValue("e2e-input-mode", "fastq");
        answers.SetValue("reference", "reference.fa.gz");
        answers.SetValue("reads-path", "reads.fastq.gz");
        answers.SetValue("database", "transcripts.db");
        answers.SetValue("output", "output");
        answers.SetValue("output-prefix", "variants");
        answers.SetFlag("disable-softclip-realign", true);

        var arguments = spec.BuildArguments(answers);

        Assert.Contains("--disable-softclip-realign", arguments);
        Assert.Contains("--fastq", arguments);
        Assert.Contains("--database", arguments);
    }

    [Fact]
    public void CreateCommandSpecs_ContainsTrimCommand()
    {
        var spec = GetSpec("trim");

        Assert.Equal("trim", spec.Name);
        Assert.NotEmpty(spec.BasicOptions);
        Assert.Contains(spec.BasicOptions, o => o.Label == "--fastq");
        Assert.Contains(spec.BasicOptions, o => o.Label == "--adapter");
        Assert.Contains(spec.BasicOptions, o => o.Label == "--min-length");
        Assert.Contains(spec.BasicOptions, o => o.Label == "--max-mismatches");
    }

    [Fact]
    public void BuildTrimArguments_IncludesAllRequiredOptions()
    {
        var spec = GetSpec("trim");
        var answers = new InteractiveAnswerStore();
        answers.SetValue("fastq", "reads.fastq.gz");
        answers.SetValue("adapter", "AGATCGGAAGAGCACACGTCTGAACTCCAGTCA");
        answers.SetValue("min-length", "20");
        answers.SetValue("max-mismatches", "2");
        answers.SetValue("output", "output");
        answers.SetValue("output-prefix", "trimmed");

        var arguments = spec.BuildArguments(answers);

        Assert.Equal(
            ["trim", "--fastq", "reads.fastq.gz", "--adapter", "AGATCGGAAGAGCACACGTCTGAACTCCAGTCA",
             "--min-length", "20", "--max-mismatches", "2",
             "--output", "output", "--output-prefix", "trimmed"],
            arguments);
    }

    [Fact]
    public void BuildTrimArguments_OmitsOptionalMaxReadsWhenNotSet()
    {
        var spec = GetSpec("trim");
        var answers = new InteractiveAnswerStore();
        answers.SetValue("fastq", "reads.fastq.gz");
        answers.SetValue("adapter", "CTGTCTCTTATACACATCT");

        var arguments = spec.BuildArguments(answers);

        Assert.DoesNotContain("--max-reads", arguments);
    }

    [Fact]
    public void CreateCommandSpecs_ContainsQcCommand()
    {
        var spec = GetSpec("qc");

        Assert.Equal("qc", spec.Name);
        Assert.Contains(spec.BasicOptions, o => o.Label == "--fastq");
        Assert.Contains(spec.BasicOptions, o => o.Label == "--output-dir");
        // adapter is an advanced option
        Assert.Contains(spec.AdvancedOptions, o => o.Label == "--adapter");
    }

    [Fact]
    public void BuildQcArguments_IncludesBasicOptions()
    {
        var spec = GetSpec("qc");
        var answers = new InteractiveAnswerStore();
        answers.SetValue("fastq", "reads.fastq.gz");
        answers.SetValue("output-dir", "output");
        answers.SetValue("output-prefix", "qc-report");

        var arguments = spec.BuildArguments(answers);

        Assert.Equal(
            ["qc", "--fastq", "reads.fastq.gz", "--output-dir", "output", "--output-prefix", "qc-report"],
            arguments);
    }

    [Fact]
    public void BuildQcArguments_IncludesOptionalAdapterWhenSet()
    {
        var spec = GetSpec("qc");
        var answers = new InteractiveAnswerStore();
        answers.SetValue("fastq", "reads.fastq.gz");
        answers.SetValue("adapter", "AGATCGGAAGAGCACACGTCTGAACTCCAGTCA");

        var arguments = spec.BuildArguments(answers);

        Assert.Contains("--adapter", arguments);
        Assert.Contains("AGATCGGAAGAGCACACGTCTGAACTCCAGTCA", arguments);
    }

    [Fact]
    public void CreateCommandSpecs_ContainsVariantCallCommand()
    {
        var spec = GetSpec("variantcall");

        Assert.Equal("variantcall", spec.Name);
        Assert.Contains(spec.BasicOptions, o => o.Label == "--bam");
        Assert.Contains(spec.BasicOptions, o => o.Label == "--reference");
        // advanced options are the shared variant-calling set
        Assert.Contains(spec.AdvancedOptions, o => o.Label == "--min-alignment-score");
        Assert.Contains(spec.AdvancedOptions, o => o.Label == "--min-variant-quality");
    }

    [Fact]
    public void BuildVariantCallArguments_IncludesRequiredOptions()
    {
        var spec = GetSpec("variantcall");
        var answers = new InteractiveAnswerStore();
        answers.SetValue("bam", "sorted.bam");
        answers.SetValue("reference", "ref.fa.gz");
        answers.SetValue("output", "output");
        answers.SetValue("output-prefix", "variants");

        var arguments = spec.BuildArguments(answers);

        Assert.Contains("variantcall", arguments);
        Assert.Contains("--bam", arguments);
        Assert.Contains("sorted.bam", arguments);
        Assert.Contains("--reference", arguments);
        Assert.Contains("ref.fa.gz", arguments);
    }

    [Fact]
    public void BuildVariantCallArguments_IncludesEnableGraphSvFlagWhenEnabled()
    {
        var spec = GetSpec("variantcall");
        var answers = new InteractiveAnswerStore();
        answers.SetValue("bam", "sorted.bam");
        answers.SetValue("reference", "ref.fa.gz");
        answers.SetFlag("enable-graph-sv", true);

        var arguments = spec.BuildArguments(answers);

        Assert.Contains("--enable-graph-sv", arguments);
    }

    [Fact]
    public void BuildVariantCallArguments_OmitsDisableSoftclipFlagWhenFalse()
    {
        var spec = GetSpec("variantcall");
        var answers = new InteractiveAnswerStore();
        answers.SetValue("bam", "sorted.bam");
        answers.SetValue("reference", "ref.fa.gz");
        answers.SetFlag("disable-softclip-realign", false);

        var arguments = spec.BuildArguments(answers);

        Assert.DoesNotContain("--disable-softclip-realign", arguments);
    }

    [Fact]
    public void CreateCommandSpecs_ContainsAlignmentCommand()
    {
        var spec = GetSpec("align");

        Assert.Equal("align", spec.Name);
        Assert.NotEmpty(spec.BasicOptions);
        Assert.Contains(spec.BasicOptions, o => o.Label == "--reference");
        Assert.Contains(spec.BasicOptions, o => o.Label == "--fastq");
        Assert.Contains(spec.BasicOptions, o => o.Label == "--output");
        Assert.Contains(spec.AdvancedOptions, o => o.Label == "--min-seed-len");
        Assert.Contains(spec.AdvancedOptions, o => o.Label == "--max-windows");
    }

    [Fact]
    public void BuildAlignmentArguments_IncludesRequiredOptions()
    {
        var spec = GetSpec("align");
        var answers = new InteractiveAnswerStore();
        answers.SetValue("reference", "ref.fa.gz");
        answers.SetValue("reads", "reads.fastq.gz");
        answers.SetValue("output", "output");
        answers.SetValue("output-prefix", "aligned");

        var arguments = spec.BuildArguments(answers);

        Assert.Contains("align", arguments);
        Assert.Contains("--reference", arguments);
        Assert.Contains("ref.fa.gz", arguments);
        Assert.Contains("--fastq", arguments);
        Assert.Contains("reads.fastq.gz", arguments);
    }

    [Fact]
    public void BuildAlignmentArguments_IncludesAdvancedOptionsWhenSet()
    {
        var spec = GetSpec("align");
        var answers = new InteractiveAnswerStore();
        answers.SetValue("reference", "ref.fa.gz");
        answers.SetValue("reads", "reads.fastq.gz");
        answers.SetValue("output", "output");
        answers.SetValue("output-prefix", "aligned");
        answers.SetValue("min-seed-len", "25");
        answers.SetValue("max-windows", "16");

        var arguments = spec.BuildArguments(answers);

        Assert.Contains("--min-seed-len", arguments);
        Assert.Contains("25", arguments);
        Assert.Contains("--max-windows", arguments);
        Assert.Contains("16", arguments);
    }

    [Fact]
    public void BuildAlignmentArguments_OmitsOptionalMaxReadsWhenNotSet()
    {
        var spec = GetSpec("align");
        var answers = new InteractiveAnswerStore();
        answers.SetValue("reference", "ref.fa.gz");
        answers.SetValue("reads", "reads.fastq.gz");
        answers.SetValue("output", "output");

        var arguments = spec.BuildArguments(answers);

        Assert.DoesNotContain("--max-reads", arguments);
    }

    private static InteractiveCommandSpec GetSpec(string name)
    {
        return Assert.Single(PreatorInteractiveUi.CreateCommandSpecs(), spec => spec.Name == name);
    }

    private sealed class FakeInteractiveUi(int exitCode) : IPreatorInteractiveUi
    {
        public int CallCount { get; private set; }

        public Task<int> Run(System.CommandLine.RootCommand rootCommand, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(exitCode);
        }
    }
}
