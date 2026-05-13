namespace OpenMedStack.Preator.Tests;

using Xunit;

public sealed class AnalysisCommandOptionTests
{
    [Fact]
    public void CreateAnalysisOptions_DefaultsPreserveHistoricalAcceptanceThresholds()
    {
        var parseResult = Program.CreateRootCommand().Parse([
            "analysis",
            "--reference", "reference.fa.gz",
            "--fastq", "reads.fastq.gz"
        ]);

        Assert.Empty(parseResult.Errors);

        var options = AnalysisCommand.CreateOptions(parseResult);

        Assert.Equal(1, options.MinAlternateObservationCount);
        Assert.Equal(0.0, options.MinAlternateFraction);
    }

    [Fact]
    public void CreateAnalysisOptions_ParsesFreebayesLikeAcceptanceThresholds()
    {
        var parseResult = Program.CreateRootCommand().Parse([
            "analysis",
            "--reference", "reference.fa.gz",
            "--fastq", "reads.fastq.gz",
            "--min-alternate-observation-count", "2",
            "--min-alternate-fraction", "0.20"
        ]);

        Assert.Empty(parseResult.Errors);

        var options = AnalysisCommand.CreateOptions(parseResult);

        Assert.Equal(2, options.MinAlternateObservationCount);
        Assert.Equal(0.20, options.MinAlternateFraction);
    }
}
