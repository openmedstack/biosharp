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
