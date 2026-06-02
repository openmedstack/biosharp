namespace OpenMedStack.Preator.Tests;

using Xunit;

public sealed class MarkDupCommandOptionTests
{
    [Fact]
    public void CreateMarkDupOptions_ParsesRequiredBam()
    {
        var parseResult = Program.CreateRootCommand().Parse([
            "markdup",
            "--bam", "reads.bam",
            "--output", "output_dir"
        ]);

        Assert.Empty(parseResult.Errors);

        var options = MarkDupCommand.CreateOptions(parseResult);

        Assert.Equal("reads.bam", options.BamPath);
        Assert.Equal("output_dir", options.OutputPath);
    }

    [Fact]
    public void CreateMarkDupOptions_DefaultOutputPrefix()
    {
        var parseResult = Program.CreateRootCommand().Parse([
            "markdup",
            "--bam", "reads.bam"
        ]);

        var options = MarkDupCommand.CreateOptions(parseResult);

        // OutputPrefixOption default is "variants"; the ?? "markdup" fallback is never reached
        Assert.Equal("variants", options.OutputPrefix);
    }

    [Fact]
    public void CreateMarkDupOptions_CustomOutputPrefix()
    {
        var parseResult = Program.CreateRootCommand().Parse([
            "markdup",
            "--bam", "reads.bam",
            "--output-prefix", "deduped"
        ]);

        var options = MarkDupCommand.CreateOptions(parseResult);

        Assert.Equal("deduped", options.OutputPrefix);
    }

    [Fact]
    public void CreateMarkDupOptions_DefaultOpticalPixelDistance()
    {
        var parseResult = Program.CreateRootCommand().Parse([
            "markdup",
            "--bam", "reads.bam"
        ]);

        var options = MarkDupCommand.CreateOptions(parseResult);

        Assert.Equal(100, options.OpticalPixelDistance);
    }

    [Fact]
    public void CreateMarkDupOptions_CustomOpticalPixelDistance()
    {
        var parseResult = Program.CreateRootCommand().Parse([
            "markdup",
            "--bam", "reads.bam",
            "--optical-pixel-distance", "250"
        ]);

        Assert.Empty(parseResult.Errors);

        var options = MarkDupCommand.CreateOptions(parseResult);

        Assert.Equal(250, options.OpticalPixelDistance);
    }

    [Fact]
    public void CreateMarkDupOptions_MissingBamProducesError()
    {
        var parseResult = Program.CreateRootCommand().Parse([
            "markdup"
        ]);

        Assert.NotEmpty(parseResult.Errors);
    }
}
