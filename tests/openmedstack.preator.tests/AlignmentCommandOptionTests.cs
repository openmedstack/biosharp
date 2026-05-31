namespace OpenMedStack.Preator.Tests;

using Xunit;

public sealed class AlignmentCommandOptionTests
{
    [Fact]
    public void CreateAlignmentOptions_ParsesRequiredOptions()
    {
        var parseResult = Program.CreateRootCommand().Parse([
            "align",
            "--reference", "reference.fa.gz",
            "--fastq", "reads.fastq.gz",
            "--output", "output_dir"
        ]);

        Assert.Empty(parseResult.Errors);

        var options = AlignmentCommand.CreateOptions(parseResult);

        Assert.Equal("reference.fa.gz", options.ReferencePath);
        Assert.Equal("reads.fastq.gz", options.FastqPath);
        Assert.Equal("output_dir", options.OutputPath);
    }

    [Fact]
    public void CreateAlignmentOptions_DefaultsUseCorrectValues()
    {
        var parseResult = Program.CreateRootCommand().Parse([
            "align",
            "--reference", "reference.fa.gz",
            "--fastq", "reads.fastq.gz",
            "--output", "output_dir"
        ]);

        var options = AlignmentCommand.CreateOptions(parseResult);

        Assert.Equal(10, options.MinAlignmentScore);
        Assert.Equal(19, options.MinSeedLen);
        Assert.Equal(64, options.MaxSeedHitsThreshold);      // from PreatorCommandOptions default
        Assert.Equal(64, options.WindowPadding);
        Assert.Equal(8, options.MaxCandidateWindowsPerRead);
        Assert.Equal(1, options.SeedStep);
    }

    [Fact]
    public void CreateAlignmentOptions_ParsesAllAdvancedOptions()
    {
        var parseResult = Program.CreateRootCommand().Parse([
            "align",
            "--reference", "ref.fa.gz",
            "--fastq", "reads.fastq.gz",
            "--output", "out",
            "--min-alignment-score", "15",
            "--min-seed-len", "25",
            "--max-seed-hits", "128",
            "--seed-step", "2",
            "--window-padding", "128",
            "--max-windows", "16",
            "--max-cores", "4",
            "--output-prefix", "my_aligned"
        ]);

        Assert.Empty(parseResult.Errors);

        var options = AlignmentCommand.CreateOptions(parseResult);

        Assert.Equal(15, options.MinAlignmentScore);
        Assert.Equal(25, options.MinSeedLen);
        Assert.Equal(128, options.MaxSeedHitsThreshold);
        Assert.Equal(2, options.SeedStep);
        Assert.Equal(128, options.WindowPadding);
        Assert.Equal(16, options.MaxCandidateWindowsPerRead);
        Assert.Equal(4, options.MaxCores);
        Assert.Equal("my_aligned", options.OutputPrefix);
    }

    [Fact]
    public void CreateAlignmentOptions_UsesDefaultOutputPrefix()
    {
        var parseResult = Program.CreateRootCommand().Parse([
            "align",
            "--reference", "ref.fa.gz",
            "--fastq", "reads.fastq.gz",
            "--output", "out"
        ]);

        var options = AlignmentCommand.CreateOptions(parseResult);

        Assert.Equal("variants", options.OutputPrefix);
    }

    [Fact]
    public void CreateAlignmentOptions_DefaultsPreserveSeedLenAndWindowPadDefaults()
    {
        var parseResult = Program.CreateRootCommand().Parse([
            "align",
            "--reference", "ref.fa.gz",
            "--fastq", "reads.fastq.gz",
            "--output", "out"
        ]);

        var options = AlignmentCommand.CreateOptions(parseResult);

        Assert.Equal(19, options.MinSeedLen);
        Assert.Equal(64, options.WindowPadding);
        Assert.Equal(8, options.MaxCandidateWindowsPerRead);
        Assert.Equal(1, options.SeedStep);
    }

    [Fact]
    public void CreateAlignmentOptions_ParsesMaxReads()
    {
        var parseResult = Program.CreateRootCommand().Parse([
            "align",
            "--reference", "ref.fa.gz",
            "--fastq", "reads.fastq.gz",
            "--output", "out",
            "--max-reads", "500"
        ]);

        var options = AlignmentCommand.CreateOptions(parseResult);

        Assert.Equal(500, options.MaxReads);
    }

    [Fact]
    public void CreateAlignmentOptions_MaxReadsIsNullable()
    {
        var parseResult = Program.CreateRootCommand().Parse([
            "align",
            "--reference", "ref.fa.gz",
            "--fastq", "reads.fastq.gz",
            "--output", "out"
        ]);

        var options = AlignmentCommand.CreateOptions(parseResult);

        Assert.Null(options.MaxReads);
    }
}
