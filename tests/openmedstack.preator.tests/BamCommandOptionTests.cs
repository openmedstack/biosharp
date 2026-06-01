namespace OpenMedStack.Preator.Tests;

using System.IO;
using Xunit;

public sealed class BamCommandOptionTests
{
     static string BamPath => new FileInfo("reads.bam").FullName;

     // ── Align --bam ──────────────────────────────────────────────

     [Fact]
    public void CreateAlignmentOptions_ParsesBamOption()
     {
        var parseResult = Program.CreateRootCommand().Parse([
             "align",
             "--reference", "reference.fa.gz",
             "--bam", "reads.bam",
             "--output", "output_dir"
         ]);

        Assert.Empty(parseResult.Errors);

        var options = AlignmentCommand.CreateOptions(parseResult);

        Assert.Equal(BamPath, options.BamPath);
     }

     [Fact]
    public void CreateAlignmentOptions_AlternativeToFastq()
     {
        var parseResult = Program.CreateRootCommand().Parse([
             "align",
             "--reference", "reference.fa",
             "--bam", "reads.bam",
             "--output", "output_dir",
             "--max-reads", "1000",
             "--min-alignment-score", "20"
         ]);

        Assert.Empty(parseResult.Errors);

        var options = AlignmentCommand.CreateOptions(parseResult);

        Assert.Equal(BamPath, options.BamPath);
        Assert.Equal(1000, options.MaxReads);
        Assert.Equal(20, options.MinAlignmentScore);
     }

     [Fact]
    public void CreateAlignmentOptions_MaxReadsIsNullableWithBam()
     {
        var parseResult = Program.CreateRootCommand().Parse([
             "align",
             "--reference", "reference.fa",
             "--bam", "reads.bam",
             "--output", "output_dir"
         ]);

        var options = AlignmentCommand.CreateOptions(parseResult);

        Assert.Null(options.MaxReads);
     }

     // ── VariantCall --bam ────────────────────────────────────────

     [Fact]
    public void CreateVariantCallOptions_ParsesBamOption()
     {
        var parseResult = Program.CreateRootCommand().Parse([
             "variantcall",
             "--reference", "reference.fa",
             "--bam", "reads.bam"
         ]);

        Assert.Empty(parseResult.Errors);

        var options = VariantCallCommand.CreateOptions(parseResult);

        Assert.Equal(BamPath, options.BamPath);
     }

     [Fact]
    public void CreateVariantCallOptions_AlternativeToFastq()
     {
        var parseResult = Program.CreateRootCommand().Parse([
             "variantcall",
             "--reference", "reference.fa",
             "--bam", "reads.bam",
             "--min-alternate-observation-count", "3",
             "--min-alternate-fraction", "0.15"
         ]);

        Assert.Empty(parseResult.Errors);

        var options = VariantCallCommand.CreateOptions(parseResult);

        Assert.Equal(BamPath, options.BamPath);
        Assert.Equal(3, options.MinAlternateObservationCount);
        Assert.Equal(0.15, options.MinAlternateFraction);
     }

     [Fact]
    public void CreateVariantCallOptions_MaxReadsIsNullable()
     {
        var parseResult = Program.CreateRootCommand().Parse([
             "variantcall",
             "--reference", "reference.fa",
             "--bam", "reads.bam"
         ]);

        var options = VariantCallCommand.CreateOptions(parseResult);

        Assert.Null(options.MaxReads);
     }

     // ── E2E --bam ────────────────────────────────────────────────

     [Fact]
    public void CreateE2EOptions_ParsesBamOption()
     {
        var parseResult = Program.CreateRootCommand().Parse([
             "e2e",
             "--reference", "reference.fa",
             "--bam", "reads.bam",
             "--database", "transcripts.db"
         ]);

        Assert.Empty(parseResult.Errors);

        var options = E2ECommand.CreateOptions(parseResult);

        Assert.Equal(BamPath, options.BamPath);
     }

     [Fact]
    public void CreateE2EOptions_AlternativeToFastq()
     {
        var parseResult = Program.CreateRootCommand().Parse([
             "e2e",
             "--reference", "reference.fa",
             "--bam", "reads.bam",
             "--database", "transcripts.db",
             "--max-reads", "500",
             "--disable-softclip-realign",
             "--enable-graph-sv"
         ]);

        Assert.Empty(parseResult.Errors);

        var options = E2ECommand.CreateOptions(parseResult);

        Assert.Equal(BamPath, options.BamPath);
        Assert.Equal(500, options.MaxReads);
        Assert.False(options.EnableSoftClipRealignment);
        Assert.True(options.EnableGraphSvDetection);
     }

     [Fact]
    public void CreateE2EOptions_MaxReadsIsNullableWithBam()
     {
        var parseResult = Program.CreateRootCommand().Parse([
             "e2e",
             "--reference", "reference.fa",
             "--bam", "reads.bam",
             "--database", "transcripts.db"
         ]);

        var options = E2ECommand.CreateOptions(parseResult);

        Assert.Null(options.MaxReads);
     }
}
