using System;
using System.IO;
using System.Threading.Tasks;
using OpenMedStack.BioSharp.Model.Vcf;
using Xunit;

namespace OpenMedStack.BioSharp.Calculations.Tests;

public class Ann2ClinVarDbSnpTests : IDisposable
{
    private readonly string _tempDir;

    public Ann2ClinVarDbSnpTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"ann2_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { /* ignore */ }
    }

    // Minimal ClinVar VCF
    private static string ClinVarVcf() =>
        string.Join("\n",
            "##fileformat=VCFv4.1",
            "##INFO=<ID=CLNSIG,Number=.,Type=String,Description=\"Clinical significance\">",
            "##INFO=<ID=CLNDN,Number=.,Type=String,Description=\"ClinVar disease name\">",
            "##INFO=<ID=CLNREVSTAT,Number=.,Type=String,Description=\"ClinVar review status\">",
            "#CHROM\tPOS\tID\tREF\tALT\tQUAL\tFILTER\tINFO",
            "chr1\t100\trs1234567\tA\tT\t.\t.\tCLNSIG=Pathogenic;CLNDN=Test_disease;CLNREVSTAT=reviewed_by_expert_panel",
            "chr1\t200\trs2\tG\tC\t.\t.\tCLNSIG=Benign;CLNDN=No_disease;CLNREVSTAT=criteria_provided",
            "chr1\t300\trs3\tT\tA\t.\t.\tCLNSIG=Likely_pathogenic;CLNDN=Rare_disease;CLNREVSTAT=criteria_provided",
            "chr1\t400\trs4\tC\tG\t.\t.\tCLNSIG=Likely_benign;CLNDN=;CLNREVSTAT=no_assertion_criteria_provided",
            "chr1\t500\trs5\tA\tC\t.\t.\tCLNSIG=Uncertain_significance;CLNDN=Unknown;CLNREVSTAT=no_assertion");

    // Minimal dbSNP VCF (ID field carries rsID)
    private static string DbSnpVcf() =>
        string.Join("\n",
            "##fileformat=VCFv4.1",
            "##INFO=<ID=RS,Number=1,Type=Integer,Description=\"dbSNP ID\">",
            "#CHROM\tPOS\tID\tREF\tALT\tQUAL\tFILTER\tINFO",
            "chr1\t100\trs1234567\tA\tT\t.\t.\tRS=1234567",
            "chr1\t200\trs9876\tG\tC\t.\t.\tRS=9876",
            "chr2\t50\trs111\tC\tA\t.\t.\tRS=111");

    private static VcfVariant MakeVariant(string chrom, int pos, string @ref, string alt) =>
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

    // ─── ClinVarAnnotator tests ──────────────────────────────────────────────

    [Fact]
    public async Task ClinVarAnnotator_KnownVariant_ReturnsPathogenic()
    {
        var vcfPath = Path.Combine(_tempDir, "clinvar.vcf");
        await File.WriteAllTextAsync(vcfPath, ClinVarVcf());

        var annotator = new ClinVarAnnotator();
        await using var stream = File.OpenRead(vcfPath);
        await annotator.Load(stream, TestContext.Current.CancellationToken);

        var result = annotator.Annotate(MakeVariant("chr1", 100, "A", "T"));

        Assert.NotNull(result);
        Assert.Equal("Pathogenic", result.ClinicalSignificance);
        Assert.Equal("Test_disease", result.DiseaseName);
        Assert.Equal("reviewed_by_expert_panel", result.ReviewStatus);
    }

    [Fact]
    public async Task ClinVarAnnotator_AllClassifications_Parsed()
    {
        var vcfPath = Path.Combine(_tempDir, "clinvar2.vcf");
        await File.WriteAllTextAsync(vcfPath, ClinVarVcf());

        var annotator = new ClinVarAnnotator();
        await using var stream = File.OpenRead(vcfPath);
        await annotator.Load(stream, TestContext.Current.CancellationToken);

        Assert.Equal("Benign", annotator.Annotate(MakeVariant("chr1", 200, "G", "C"))?.ClinicalSignificance);
        Assert.Equal("Likely_pathogenic", annotator.Annotate(MakeVariant("chr1", 300, "T", "A"))?.ClinicalSignificance);
        Assert.Equal("Likely_benign", annotator.Annotate(MakeVariant("chr1", 400, "C", "G"))?.ClinicalSignificance);
        Assert.Equal("Uncertain_significance", annotator.Annotate(MakeVariant("chr1", 500, "A", "C"))?.ClinicalSignificance);
    }

    [Fact]
    public async Task ClinVarAnnotator_MissingVariant_ReturnsNull()
    {
        var vcfPath = Path.Combine(_tempDir, "clinvar3.vcf");
        await File.WriteAllTextAsync(vcfPath, ClinVarVcf());

        var annotator = new ClinVarAnnotator();
        await using var stream = File.OpenRead(vcfPath);
        await annotator.Load(stream, TestContext.Current.CancellationToken);

        var result = annotator.Annotate(MakeVariant("chr1", 9999, "A", "T"));
        Assert.Null(result);
    }

    [Fact]
    public async Task ClinVarAnnotator_WrongAllele_ReturnsNull()
    {
        var vcfPath = Path.Combine(_tempDir, "clinvar4.vcf");
        await File.WriteAllTextAsync(vcfPath, ClinVarVcf());

        var annotator = new ClinVarAnnotator();
        await using var stream = File.OpenRead(vcfPath);
        await annotator.Load(stream, TestContext.Current.CancellationToken);

        // Right position, wrong allele
        var result = annotator.Annotate(MakeVariant("chr1", 100, "A", "C"));
        Assert.Null(result);
    }

    // ─── DbSnpAnnotator tests ────────────────────────────────────────────────

    [Fact]
    public async Task DbSnpAnnotator_KnownVariant_ReturnsRsId()
    {
        var vcfPath = Path.Combine(_tempDir, "dbsnp.vcf");
        await File.WriteAllTextAsync(vcfPath, DbSnpVcf());

        var annotator = new DbSnpAnnotator();
        await using var stream = File.OpenRead(vcfPath);
        await annotator.Load(stream, TestContext.Current.CancellationToken);

        var result = annotator.Annotate(MakeVariant("chr1", 100, "A", "T"));
        Assert.NotNull(result);
        Assert.Equal("rs1234567", result.RsId);
    }

    [Fact]
    public async Task DbSnpAnnotator_MissingVariant_ReturnsNull()
    {
        var vcfPath = Path.Combine(_tempDir, "dbsnp2.vcf");
        await File.WriteAllTextAsync(vcfPath, DbSnpVcf());

        var annotator = new DbSnpAnnotator();
        await using var stream = File.OpenRead(vcfPath);
        await annotator.Load(stream, TestContext.Current.CancellationToken);

        var result = annotator.Annotate(MakeVariant("chr1", 9999, "A", "T"));
        Assert.Null(result);
    }

    [Fact]
    public async Task DbSnpAnnotator_CrossChromosome_MatchesCorrectly()
    {
        var vcfPath = Path.Combine(_tempDir, "dbsnp3.vcf");
        await File.WriteAllTextAsync(vcfPath, DbSnpVcf());

        var annotator = new DbSnpAnnotator();
        await using var stream = File.OpenRead(vcfPath);
        await annotator.Load(stream, TestContext.Current.CancellationToken);

        var result = annotator.Annotate(MakeVariant("chr2", 50, "C", "A"));
        Assert.NotNull(result);
        Assert.Equal("rs111", result.RsId);

        // chr1/200 should not match chr2/200
        var noResult = annotator.Annotate(MakeVariant("chr2", 200, "G", "C"));
        Assert.Null(noResult);
    }
}