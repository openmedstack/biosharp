namespace OpenMedStack.Preator.Tests;

using Xunit;

public sealed class RepeatMaskCommandOptionTests
{
    [Fact]
    public void CreateRepeatMaskOptions_ParsesRequiredFasta()
    {
        var parseResult = Program.CreateRootCommand().Parse([
            "repeatmask",
            "--fasta", "reference.fa",
            "--output", "output_dir"
        ]);

        Assert.Empty(parseResult.Errors);

        var options = RepeatMaskCommand.CreateOptions(parseResult);

        Assert.Equal("reference.fa", options.FastaPath);
        Assert.Equal("output_dir", options.OutputPath);
    }

    [Fact]
    public void CreateRepeatMaskOptions_DefaultOutputPrefix()
    {
        var parseResult = Program.CreateRootCommand().Parse([
            "repeatmask",
            "--fasta", "reference.fa"
        ]);

        var options = RepeatMaskCommand.CreateOptions(parseResult);

        // OutputPrefixOption default is "variants"; the ?? "masked" fallback is never reached
        Assert.Equal("variants", options.OutputPrefix);
    }

    [Fact]
    public void CreateRepeatMaskOptions_CustomOutputPrefix()
    {
        var parseResult = Program.CreateRootCommand().Parse([
            "repeatmask",
            "--fasta", "reference.fa.gz",
            "--output-prefix", "mymasked"
        ]);

        var options = RepeatMaskCommand.CreateOptions(parseResult);

        Assert.Equal("mymasked", options.OutputPrefix);
    }

    [Fact]
    public void CreateRepeatMaskOptions_LibraryPathIsNullByDefault()
    {
        var parseResult = Program.CreateRootCommand().Parse([
            "repeatmask",
            "--fasta", "reference.fa"
        ]);

        var options = RepeatMaskCommand.CreateOptions(parseResult);

        Assert.Null(options.LibraryPath);
    }

    [Fact]
    public void CreateRepeatMaskOptions_ParsesLibraryPath()
    {
        var parseResult = Program.CreateRootCommand().Parse([
            "repeatmask",
            "--fasta", "reference.fa",
            "--library", "repeats.json"
        ]);

        Assert.Empty(parseResult.Errors);

        var options = RepeatMaskCommand.CreateOptions(parseResult);

        Assert.Equal("repeats.json", options.LibraryPath);
    }

    [Fact]
    public void CreateRepeatMaskOptions_MinMotifLengthIsNullByDefault()
    {
        var parseResult = Program.CreateRootCommand().Parse([
            "repeatmask",
            "--fasta", "reference.fa"
        ]);

        var options = RepeatMaskCommand.CreateOptions(parseResult);

        Assert.Null(options.MinMotifLength);
    }

    [Fact]
    public void CreateRepeatMaskOptions_ParsesMinMotifLength()
    {
        var parseResult = Program.CreateRootCommand().Parse([
            "repeatmask",
            "--fasta", "reference.fa",
            "--min-motif-length", "8"
        ]);

        Assert.Empty(parseResult.Errors);

        var options = RepeatMaskCommand.CreateOptions(parseResult);

        Assert.Equal(8, options.MinMotifLength);
    }

    [Fact]
    public void CreateRepeatMaskOptions_MissingFastaProducesError()
    {
        var parseResult = Program.CreateRootCommand().Parse([
            "repeatmask"
        ]);

        Assert.NotEmpty(parseResult.Errors);
    }
}
