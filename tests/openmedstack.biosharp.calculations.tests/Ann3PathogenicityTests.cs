using System;
using System.IO;
using System.Threading.Tasks;
using OpenMedStack.BioSharp.Model;
using OpenMedStack.BioSharp.Model.Vcf;
using Xunit;

namespace OpenMedStack.BioSharp.Calculations.Tests;

public class Ann3PathogenicityTests : IDisposable
{
    private readonly string _tempDir;

    public Ann3PathogenicityTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"ann3_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_tempDir, true);
        }
        catch
        {
            /* ignore */
        }
    }

    // dbNSFP-format TSV: header + data rows
    // Key columns: #chr, pos(1-based), ref, alt, SIFT_score, SIFT_pred, Polyphen2_HDIV_score, Polyphen2_HDIV_pred
    private static string DbNsfpTsv() =>
        string.Join("\n",
            "#chr\tpos(1-based)\tref\talt\tSIFT_score\tSIFT_pred\tPolyphen2_HDIV_score\tPolyphen2_HDIV_pred",
            "chr1\t100\tA\tT\t0.02\tD\t0.95\tD",
            "chr1\t200\tG\tC\t0.45\tT\t0.15\tB",
            "chr1\t300\tC\tA\t.\t.\t.\t.");

    private static VcfVariant MakeMissense(string chrom, int pos, string @ref, string alt) =>
        new()
        {
            Chromosome = chrom,
            Position = pos,
            Reference = @ref,
            Alternate = alt,
            ErrorProbabilities = [30],
            FailedFilter = [],
            MarkerIdentifiers = ".",
            AdditionalInformation = "."
        };

    [Fact]
    public async Task PathogenicityAnnotator_MissenseVariant_ReturnsSiftAndPolyPhen()
    {
        var tsvPath = Path.Combine(_tempDir, "dbnsfp.tsv");
        await File.WriteAllTextAsync(tsvPath, DbNsfpTsv());

        var annotator = new PathogenicityAnnotator();
        await using var stream = File.OpenRead(tsvPath);
        await annotator.Load(stream, TestContext.Current.CancellationToken);

        var ann = new VariantAnnotation
        {
            AffectedGene = "GENE1",
            Consequence = VariantConsequence.Missense,
            HgvsCoding = "c.100A>T",
            HgvsNotation = "GENE1:c.100A>T"
        };

        var result = annotator.Annotate(MakeMissense("chr1", 100, "A", "T"), ann);
        Assert.NotNull(result);
        Assert.Equal(0.02, result.SiftScore!.Value, precision: 4);
        Assert.Equal("D", result.SiftPrediction);
        Assert.Equal(0.95, result.PolyPhen2HdivScore!.Value, precision: 4);
        Assert.Equal("D", result.PolyPhen2HdivPrediction);
    }

    [Fact]
    public async Task PathogenicityAnnotator_MissingScore_ReturnsDot()
    {
        var tsvPath = Path.Combine(_tempDir, "dbnsfp2.tsv");
        await File.WriteAllTextAsync(tsvPath, DbNsfpTsv());

        var annotator = new PathogenicityAnnotator();
        await using var stream = File.OpenRead(tsvPath);
        await annotator.Load(stream, TestContext.Current.CancellationToken);

        var ann = new VariantAnnotation
        {
            AffectedGene = "GENE1",
            Consequence = VariantConsequence.Missense,
            HgvsCoding = "c.300C>A",
            HgvsNotation = "GENE1:c.300C>A"
        };

        var result = annotator.Annotate(MakeMissense("chr1", 300, "C", "A"), ann);
        Assert.NotNull(result);
        Assert.Null(result.SiftScore);
        Assert.Equal(".", result.SiftPrediction);
        Assert.Null(result.PolyPhen2HdivScore);
        Assert.Equal(".", result.PolyPhen2HdivPrediction);
    }

    [Fact]
    public async Task PathogenicityAnnotator_NonMissenseVariant_ReturnsNull()
    {
        var tsvPath = Path.Combine(_tempDir, "dbnsfp3.tsv");
        await File.WriteAllTextAsync(tsvPath, DbNsfpTsv());

        var annotator = new PathogenicityAnnotator();
        await using var stream = File.OpenRead(tsvPath);
        await annotator.Load(stream, TestContext.Current.CancellationToken);

        // Synonymous variant → should not be annotated
        var ann = new VariantAnnotation
        {
            AffectedGene = "GENE1",
            Consequence = VariantConsequence.Synonymous,
            HgvsCoding = "c.100A>T",
            HgvsNotation = "GENE1:c.100A>T"
        };

        var result = annotator.Annotate(MakeMissense("chr1", 100, "A", "T"), ann);
        Assert.Null(result);
    }

    [Fact]
    public async Task PathogenicityAnnotator_VariantNotInDb_ReturnsNull()
    {
        var tsvPath = Path.Combine(_tempDir, "dbnsfp4.tsv");
        await File.WriteAllTextAsync(tsvPath, DbNsfpTsv());

        var annotator = new PathogenicityAnnotator();
        await using var stream = File.OpenRead(tsvPath);
        await annotator.Load(stream, TestContext.Current.CancellationToken);

        var ann = new VariantAnnotation
        {
            AffectedGene = "GENE1",
            Consequence = VariantConsequence.Missense,
            HgvsCoding = "c.999A>T",
            HgvsNotation = "GENE1:c.999A>T"
        };

        var result = annotator.Annotate(MakeMissense("chr1", 9999, "A", "T"), ann);
        Assert.Null(result);
    }
}
