namespace OpenMedStack.Preator.Tests;

using Xunit;

public sealed class CoverageCommandOptionTests
{
    [Fact]
    public void CreateCoverageOptions_ParsesRequiredBam()
    {
        var parseResult = Program.CreateRootCommand().Parse([
            "coverage",
            "--bam", "reads.bam",
            "--output", "output_dir"
        ]);

        Assert.Empty(parseResult.Errors);

        var options = CoverageCommand.CreateOptions(parseResult);

        // BamRequiredOption stores the raw string path (not expanded via FileInfo.FullName)
        Assert.Equal("reads.bam", options.BamPath);
        Assert.Equal("output_dir", options.OutputPath);
    }

    [Fact]
    public void CreateCoverageOptions_DefaultOutputPrefix()
    {
        var parseResult = Program.CreateRootCommand().Parse([
            "coverage",
            "--bam", "reads.bam"
        ]);

        var options = CoverageCommand.CreateOptions(parseResult);

        // OutputPrefixOption default is "variants"; the ?? "coverage" fallback is never reached
        Assert.Equal("variants", options.OutputPrefix);
    }

    [Fact]
    public void CreateCoverageOptions_CustomOutputPrefix()
    {
        var parseResult = Program.CreateRootCommand().Parse([
            "coverage",
            "--bam", "reads.bam",
            "--output-prefix", "depth_run1"
        ]);

        var options = CoverageCommand.CreateOptions(parseResult);

        Assert.Equal("depth_run1", options.OutputPrefix);
    }

    [Fact]
    public void CreateCoverageOptions_BedPathIsNullByDefault()
    {
        var parseResult = Program.CreateRootCommand().Parse([
            "coverage",
            "--bam", "reads.bam"
        ]);

        var options = CoverageCommand.CreateOptions(parseResult);

        Assert.Null(options.BedPath);
    }

    [Fact]
    public void CreateCoverageOptions_ParsesBedPath()
    {
        var parseResult = Program.CreateRootCommand().Parse([
            "coverage",
            "--bam", "reads.bam",
            "--bed", "targets.bed"
        ]);

        Assert.Empty(parseResult.Errors);

        var options = CoverageCommand.CreateOptions(parseResult);

        Assert.Equal("targets.bed", options.BedPath);
    }

    [Fact]
    public void CreateCoverageOptions_WriteDepthTsvIsFalseByDefault()
    {
        var parseResult = Program.CreateRootCommand().Parse([
            "coverage",
            "--bam", "reads.bam"
        ]);

        var options = CoverageCommand.CreateOptions(parseResult);

        Assert.False(options.WritePerPositionTsv);
    }

    [Fact]
    public void CreateCoverageOptions_ParsesWriteDepthTsvFlag()
    {
        var parseResult = Program.CreateRootCommand().Parse([
            "coverage",
            "--bam", "reads.bam",
            "--write-depth-tsv"
        ]);

        Assert.Empty(parseResult.Errors);

        var options = CoverageCommand.CreateOptions(parseResult);

        Assert.True(options.WritePerPositionTsv);
    }

    [Fact]
    public void CreateCoverageOptions_AllOptionsRoundtrip()
    {
        var parseResult = Program.CreateRootCommand().Parse([
            "coverage",
            "--bam", "reads.bam",
            "--output", "out",
            "--output-prefix", "run1",
            "--bed", "capture.bed",
            "--write-depth-tsv"
        ]);

        Assert.Empty(parseResult.Errors);

        var options = CoverageCommand.CreateOptions(parseResult);

        Assert.Equal("reads.bam", options.BamPath);
        Assert.Equal("out", options.OutputPath);
        Assert.Equal("run1", options.OutputPrefix);
        Assert.Equal("capture.bed", options.BedPath);
        Assert.True(options.WritePerPositionTsv);
    }

    [Fact]
    public void CreateCoverageOptions_MissingBamProducesError()
    {
        var parseResult = Program.CreateRootCommand().Parse([
            "coverage"
        ]);

        Assert.NotEmpty(parseResult.Errors);
    }
}
