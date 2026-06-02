namespace OpenMedStack.Preator.Tests;

using System.IO;
using Xunit;

public sealed class IndexCommandOptionTests
{
    static string BamPath => new FileInfo("reads.bam").FullName;

    // ── FASTA input ──────────────────────────────────────────────

    [Fact]
    public void CreateIndexOptions_ParsesFastaInput()
    {
        var parseResult = Program.CreateRootCommand().Parse([
            "index",
            "--fasta", "reference.fa",
            "--output", "output_dir"
        ]);

        Assert.Empty(parseResult.Errors);

        var options = IndexCommand.CreateOptions(parseResult);

        Assert.Equal("reference.fa", options.FastaPath);
        Assert.Null(options.BamPath);
        Assert.Equal("output_dir", options.OutputPath);
    }

    [Fact]
    public void CreateIndexOptions_ParsesBamInput()
    {
        var parseResult = Program.CreateRootCommand().Parse([
            "index",
            "--bam", "reads.bam",
            "--output", "output_dir"
        ]);

        Assert.Empty(parseResult.Errors);

        var options = IndexCommand.CreateOptions(parseResult);

        Assert.Null(options.FastaPath);
        Assert.Equal(BamPath, options.BamPath);
        Assert.Equal("output_dir", options.OutputPath);
    }

    [Fact]
    public void CreateIndexOptions_DefaultOutputPrefix()
    {
        var parseResult = Program.CreateRootCommand().Parse([
            "index",
            "--fasta", "reference.fa"
        ]);

        var options = IndexCommand.CreateOptions(parseResult);

        // OutputPrefixOption default is "variants"; the ?? "index" fallback is never reached
        Assert.Equal("variants", options.OutputPrefix);
    }

    [Fact]
    public void CreateIndexOptions_CustomOutputPrefix()
    {
        var parseResult = Program.CreateRootCommand().Parse([
            "index",
            "--fasta", "reference.fa",
            "--output-prefix", "hg38"
        ]);

        var options = IndexCommand.CreateOptions(parseResult);

        Assert.Equal("hg38", options.OutputPrefix);
    }

    [Fact]
    public void CreateIndexOptions_DefaultFmSampleRate()
    {
        var parseResult = Program.CreateRootCommand().Parse([
            "index",
            "--fasta", "reference.fa"
        ]);

        var options = IndexCommand.CreateOptions(parseResult);

        Assert.Equal(64, options.FmSampleRate);
    }

    [Fact]
    public void CreateIndexOptions_CustomFmSampleRate()
    {
        var parseResult = Program.CreateRootCommand().Parse([
            "index",
            "--fasta", "reference.fa",
            "--fm-sample-rate", "128"
        ]);

        Assert.Empty(parseResult.Errors);

        var options = IndexCommand.CreateOptions(parseResult);

        Assert.Equal(128, options.FmSampleRate);
    }

    [Fact]
    public void CreateIndexOptions_AlsoBuildReferenceIndexIsFalseByDefault()
    {
        var parseResult = Program.CreateRootCommand().Parse([
            "index",
            "--fasta", "reference.fa"
        ]);

        var options = IndexCommand.CreateOptions(parseResult);

        Assert.False(options.AlsoBuildReferenceIndex);
    }

    [Fact]
    public void CreateIndexOptions_ParsesAlsoBuildReferenceIndexFlag()
    {
        var parseResult = Program.CreateRootCommand().Parse([
            "index",
            "--fasta", "reference.fa",
            "--also-build-reference-index"
        ]);

        Assert.Empty(parseResult.Errors);

        var options = IndexCommand.CreateOptions(parseResult);

        Assert.True(options.AlsoBuildReferenceIndex);
    }

    [Fact]
    public void CreateIndexOptions_ParsesReferenceIdContains()
    {
        var parseResult = Program.CreateRootCommand().Parse([
            "index",
            "--fasta", "reference.fa",
            "--reference-id-contains", "chr1"
        ]);

        Assert.Empty(parseResult.Errors);

        var options = IndexCommand.CreateOptions(parseResult);

        Assert.Equal("chr1", options.ReferenceIdContains);
    }

    [Fact]
    public void CreateIndexOptions_ReferenceIdContainsIsNullByDefault()
    {
        var parseResult = Program.CreateRootCommand().Parse([
            "index",
            "--fasta", "reference.fa"
        ]);

        var options = IndexCommand.CreateOptions(parseResult);

        Assert.Null(options.ReferenceIdContains);
    }

    [Fact]
    public void CreateIndexOptions_AllOptionsRoundtrip()
    {
        var parseResult = Program.CreateRootCommand().Parse([
            "index",
            "--fasta", "reference.fa.gz",
            "--output", "out",
            "--output-prefix", "hg38",
            "--fm-sample-rate", "32",
            "--also-build-reference-index",
            "--reference-id-contains", "NC_000913"
        ]);

        Assert.Empty(parseResult.Errors);

        var options = IndexCommand.CreateOptions(parseResult);

        Assert.Equal("reference.fa.gz", options.FastaPath);
        Assert.Null(options.BamPath);
        Assert.Equal("out", options.OutputPath);
        Assert.Equal("hg38", options.OutputPrefix);
        Assert.Equal(32, options.FmSampleRate);
        Assert.True(options.AlsoBuildReferenceIndex);
        Assert.Equal("NC_000913", options.ReferenceIdContains);
    }

    [Fact]
    public void CreateIndexOptions_MissingBothFastaAndBamProducesError()
    {
        var parseResult = Program.CreateRootCommand().Parse([
            "index"
        ]);

        Assert.NotEmpty(parseResult.Errors);
    }

    // ── align --index pre-load ────────────────────────────────────

    [Fact]
    public void CreateAlignmentOptions_ParsesPreloadIndexPath()
    {
        var parseResult = Program.CreateRootCommand().Parse([
            "align",
            "--reference", "reference.fa",
            "--fastq", "reads.fastq.gz",
            "--index", "prebuilt.fmi"
        ]);

        Assert.Empty(parseResult.Errors);

        var options = AlignmentCommand.CreateOptions(parseResult);

        Assert.Equal("prebuilt.fmi", options.IndexPath);
    }

    [Fact]
    public void CreateAlignmentOptions_IndexPathIsNullByDefault()
    {
        var parseResult = Program.CreateRootCommand().Parse([
            "align",
            "--reference", "reference.fa",
            "--fastq", "reads.fastq.gz"
        ]);

        var options = AlignmentCommand.CreateOptions(parseResult);

        Assert.Null(options.IndexPath);
    }
}
