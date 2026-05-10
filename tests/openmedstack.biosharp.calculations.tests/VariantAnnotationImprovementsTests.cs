// Tests for ANN-1 through ANN-4 (Variant Annotation improvements)

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OpenMedStack.BioSharp.Calculations;
using OpenMedStack.BioSharp.Model;
using OpenMedStack.BioSharp.Model.Vcf;
using Xunit;

namespace OpenMedStack.BioSharp.Calculations.Tests;

// ─── ANN-1: GTF/GFF-based transcript model loading ─────────────────────────

public class Ann1GtfTranscriptLoadingTests : IDisposable
{
    private readonly string _tempDir;

    public Ann1GtfTranscriptLoadingTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"ann1_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { /* ignore */ }
    }

    private static string GtfPath(string dir, string name) => Path.Combine(dir, name + ".gtf");

    // ─── Minimal single-exon gene ────────────────────────────────────────────

    /// chr1 1-300: one transcript, one exon (1-300), CDS 1-297.
    private static string SingleExonGtf() =>
        string.Join("\n",
            "chr1\ttest\tgene\t1\t300\t.\t+\t.\tgene_id \"GENE1\"; gene_name \"G1\";",
            "chr1\ttest\ttranscript\t1\t300\t.\t+\t.\tgene_id \"GENE1\"; transcript_id \"TX1\"; tag \"Ensembl_canonical\";",
            "chr1\ttest\texon\t1\t300\t.\t+\t.\tgene_id \"GENE1\"; transcript_id \"TX1\"; exon_id \"E1\";",
            "chr1\ttest\tCDS\t1\t297\t.\t+\t0\tgene_id \"GENE1\"; transcript_id \"TX1\";");

    /// Two-exon gene: exon1 1-100, intron 101-200, exon2 201-300.
    /// CDS: 1-98 (exon1 CDS), 201-298 (exon2 CDS).
    private static string TwoExonGtf() =>
        string.Join("\n",
            "chr1\ttest\tgene\t1\t300\t.\t+\t.\tgene_id \"GENE1\"; gene_name \"G1\";",
            "chr1\ttest\ttranscript\t1\t300\t.\t+\t.\tgene_id \"GENE1\"; transcript_id \"TX1\"; tag \"Ensembl_canonical\";",
            "chr1\ttest\texon\t1\t100\t.\t+\t.\tgene_id \"GENE1\"; transcript_id \"TX1\"; exon_id \"E1\";",
            "chr1\ttest\tCDS\t1\t98\t.\t+\t0\tgene_id \"GENE1\"; transcript_id \"TX1\";",
            "chr1\ttest\texon\t201\t300\t.\t+\t.\tgene_id \"GENE1\"; transcript_id \"TX1\"; exon_id \"E2\";",
            "chr1\ttest\tCDS\t201\t298\t.\t+\t0\tgene_id \"GENE1\"; transcript_id \"TX1\";");

    /// Two transcripts, only TX_CANONICAL has the Ensembl_canonical tag.
    private static string TwoTranscriptGtf() =>
        string.Join("\n",
            "chr1\ttest\tgene\t1\t300\t.\t+\t.\tgene_id \"GENE1\"; gene_name \"G1\";",
            "chr1\ttest\ttranscript\t1\t300\t.\t+\t.\tgene_id \"GENE1\"; transcript_id \"TX_CANONICAL\"; tag \"Ensembl_canonical\";",
            "chr1\ttest\texon\t1\t300\t.\t+\t.\tgene_id \"GENE1\"; transcript_id \"TX_CANONICAL\";",
            "chr1\ttest\tCDS\t1\t297\t.\t+\t0\tgene_id \"GENE1\"; transcript_id \"TX_CANONICAL\";",
            "chr1\ttest\ttranscript\t50\t280\t.\t+\t.\tgene_id \"GENE1\"; transcript_id \"TX_OTHER\";",
            "chr1\ttest\texon\t50\t280\t.\t+\t.\tgene_id \"GENE1\"; transcript_id \"TX_OTHER\";",
            "chr1\ttest\tCDS\t50\t277\t.\t+\t0\tgene_id \"GENE1\"; transcript_id \"TX_OTHER\";");

    [Fact]
    public async Task LoadTranscriptsFromGtfAsync_SingleExon_PopulatesContext()
    {
        var gtf = GtfPath(_tempDir, "single");
        await File.WriteAllTextAsync(gtf, SingleExonGtf());

        using var engine = new VariantAnnotationEngine();
        await engine.LoadTranscriptsFromGtfAsync(gtf, TestContext.Current.CancellationToken);

        Assert.True(engine.TranscriptContexts.ContainsKey("TX1"), "TX1 should be loaded");
        var ctx = engine.TranscriptContexts["TX1"];
        Assert.Equal(1, ctx.CdsStart);
        Assert.Equal(297, ctx.CdsEnd);
    }

    [Fact]
    public async Task LoadTranscriptsFromGtfAsync_TwoExon_PopulatesIntrons()
    {
        var gtf = GtfPath(_tempDir, "two_exon");
        await File.WriteAllTextAsync(gtf, TwoExonGtf());

        using var engine = new VariantAnnotationEngine();
        await engine.LoadTranscriptsFromGtfAsync(gtf, TestContext.Current.CancellationToken);

        Assert.True(engine.TranscriptContexts.TryGetValue("TX1", out var ctx));
        Assert.NotNull(ctx.Introns);
        Assert.Single(ctx.Introns);
        var (intronStart, intronEnd) = ctx.Introns![0];
        Assert.Equal(101, intronStart);
        Assert.Equal(200, intronEnd);
    }

    [Fact]
    public async Task LoadTranscriptsFromGtfAsync_TwoExon_PopulatesExonBoundaries()
    {
        var gtf = GtfPath(_tempDir, "two_exon_b");
        await File.WriteAllTextAsync(gtf, TwoExonGtf());

        using var engine = new VariantAnnotationEngine();
        await engine.LoadTranscriptsFromGtfAsync(gtf, TestContext.Current.CancellationToken);

        Assert.True(engine.TranscriptContexts.TryGetValue("TX1", out var ctx));
        Assert.NotNull(ctx.ExonBoundaries);
        Assert.Equal(2, ctx.ExonBoundaries!.Count);
    }

    [Fact]
    public async Task LoadTranscriptsFromGtfAsync_TwoTranscripts_SelectsCanonical()
    {
        var gtf = GtfPath(_tempDir, "two_tx");
        await File.WriteAllTextAsync(gtf, TwoTranscriptGtf());

        using var engine = new VariantAnnotationEngine();
        await engine.LoadTranscriptsFromGtfAsync(gtf, TestContext.Current.CancellationToken);

        Assert.Equal("TX_CANONICAL", engine.CanonicalTranscriptId);
        Assert.True(engine.TranscriptContexts.ContainsKey("TX_CANONICAL"));
        Assert.True(engine.TranscriptContexts.ContainsKey("TX_OTHER"));
    }

    [Fact]
    public async Task LoadTranscriptsFromGtfAsync_NonExistentFile_Throws()
    {
        using var engine = new VariantAnnotationEngine();
        await Assert.ThrowsAsync<FileNotFoundException>(
            () => engine.LoadTranscriptsFromGtfAsync("/nonexistent/path.gtf"));
    }

    [Fact]
    public async Task AnnotateVariantFromContexts_PositionInCds_ReturnsNullConsequence()
    {
        // Variant inside CDS → not a non-coding consequence
        var gtf = GtfPath(_tempDir, "cds_pos");
        await File.WriteAllTextAsync(gtf, SingleExonGtf());

        using var engine = new VariantAnnotationEngine();
        await engine.LoadTranscriptsFromGtfAsync(gtf, TestContext.Current.CancellationToken);

        var variant = new VcfVariant
        {
            Chromosome = "chr1",
            Position = 50,   // inside CDS (1-297)
            Reference = "A",
            Alternate = "G",
            ErrorProbabilities = [30],
            FailedFilter = [],
            MarkerIdentifiers = ".",
            AdditionalInformation = "."
        };

        var anns = engine.AnnotateVariantFromContexts(variant).ToList();
        // CDS positions have no non-coding consequence → no context-only annotations
        Assert.Empty(anns);
    }

    [Fact]
    public async Task AnnotateVariantFromContexts_SpliceSitePosition_ReturnsSpliceSite()
    {
        // Position 298-300 is after CDS end (298) within splice window (CdsEnd+3=301)
        var gtf = GtfPath(_tempDir, "splice_ctx");
        await File.WriteAllTextAsync(gtf, SingleExonGtf());

        using var engine = new VariantAnnotationEngine();
        await engine.LoadTranscriptsFromGtfAsync(gtf, TestContext.Current.CancellationToken);

        var variant = new VcfVariant
        {
            Chromosome = "chr1",
            Position = 299,  // CdsEnd(297)+2 → within splice window
            Reference = "A",
            Alternate = "G",
            ErrorProbabilities = [30],
            FailedFilter = [],
            MarkerIdentifiers = ".",
            AdditionalInformation = "."
        };

        var anns = engine.AnnotateVariantFromContexts(variant).ToList();
        Assert.NotEmpty(anns);
        Assert.All(anns, a => Assert.Equal(VariantConsequence.SpliceSite, a.Consequence));
    }

    [Fact]
    public async Task AnnotateVariantFromContexts_IntronicPosition_ReturnsIntronic()
    {
        var gtf = GtfPath(_tempDir, "intronic");
        await File.WriteAllTextAsync(gtf, TwoExonGtf());

        using var engine = new VariantAnnotationEngine();
        await engine.LoadTranscriptsFromGtfAsync(gtf, TestContext.Current.CancellationToken);

        // Position 150 is in the intron (101-200)
        var variant = new VcfVariant
        {
            Chromosome = "chr1",
            Position = 150,
            Reference = "A",
            Alternate = "G",
            ErrorProbabilities = [30],
            FailedFilter = [],
            MarkerIdentifiers = ".",
            AdditionalInformation = "."
        };

        var anns = engine.AnnotateVariantFromContexts(variant).ToList();
        Assert.NotEmpty(anns);
        Assert.All(anns, a => Assert.Equal(VariantConsequence.Intronic, a.Consequence));
    }

    [Fact]
    public async Task LoadTranscriptsFromGtfAsync_AnnotationEquivalentToManualContext()
    {
        // Create a GTF where CDS is 1-90 with one exon spanning 1-100 and gene 1-100
        var gtfContent = string.Join("\n",
            "chr1\ttest\tgene\t1\t100\t.\t+\t.\tgene_id \"G\"; gene_name \"G\";",
            "chr1\ttest\ttranscript\t1\t100\t.\t+\t.\tgene_id \"G\"; transcript_id \"TX_EQ\"; tag \"Ensembl_canonical\";",
            "chr1\ttest\texon\t1\t100\t.\t+\t.\tgene_id \"G\"; transcript_id \"TX_EQ\";",
            "chr1\ttest\tCDS\t1\t90\t.\t+\t0\tgene_id \"G\"; transcript_id \"TX_EQ\";");
        var gtf = GtfPath(_tempDir, "equiv");
        await File.WriteAllTextAsync(gtf, gtfContent);

        using var gtfEngine = new VariantAnnotationEngine();
        await gtfEngine.LoadTranscriptsFromGtfAsync(gtf, TestContext.Current.CancellationToken);

        // Manually-created equivalent context
        var manualCtx = AnnotationContext.FromCdsAndGeneBoundaries(
            cdsStart: 1, cdsEnd: 90, geneStart: 1, geneEnd: 100);
        using var manualEngine = new VariantAnnotationEngine(manualCtx);

        // Annotate a variant at position 93 (splice site: CdsEnd+3 = 93 ≤ position)
        // Actually CdsEnd+3 = 90+3 = 93 inclusive → SpliceSite
        var variant = new VcfVariant
        {
            Chromosome = "chr1",
            Position = 92,  // CdsEnd(90)+2 → within splice window [91,93]
            Reference = "A",
            Alternate = "G",
            ErrorProbabilities = [30],
            FailedFilter = [],
            MarkerIdentifiers = ".",
            AdditionalInformation = "."
        };

        // GTF-based annotation
        var gtfAnns = gtfEngine.AnnotateVariantFromContexts(variant).ToList();
        Assert.NotEmpty(gtfAnns);
        Assert.Equal(VariantConsequence.SpliceSite, gtfAnns[0].Consequence);

        // Manual context classification
        var manualConsequence = manualCtx.ClassifyPosition(92);
        Assert.Equal(VariantConsequence.SpliceSite, manualConsequence);
    }
}

// ─── ANN-2: ClinVar and dbSNP rsID annotation ───────────────────────────────

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
        await annotator.LoadAsync(stream, TestContext.Current.CancellationToken);

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
        await annotator.LoadAsync(stream, TestContext.Current.CancellationToken);

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
        await annotator.LoadAsync(stream, TestContext.Current.CancellationToken);

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
        await annotator.LoadAsync(stream, TestContext.Current.CancellationToken);

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
        await annotator.LoadAsync(stream, TestContext.Current.CancellationToken);

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
        await annotator.LoadAsync(stream, TestContext.Current.CancellationToken);

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
        await annotator.LoadAsync(stream, TestContext.Current.CancellationToken);

        var result = annotator.Annotate(MakeVariant("chr2", 50, "C", "A"));
        Assert.NotNull(result);
        Assert.Equal("rs111", result.RsId);

        // chr1/200 should not match chr2/200
        var noResult = annotator.Annotate(MakeVariant("chr2", 200, "G", "C"));
        Assert.Null(noResult);
    }
}

// ─── ANN-3: Pathogenicity score integration ──────────────────────────────────

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
        try { Directory.Delete(_tempDir, true); } catch { /* ignore */ }
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
        await annotator.LoadAsync(stream, TestContext.Current.CancellationToken);

        var ann = new VariantAnnotation
        {
            AffectedGene = "GENE1",
            Consequence = VariantConsequence.Missense,
            HgvsCoding = "c.100A>T"
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
        await annotator.LoadAsync(stream, TestContext.Current.CancellationToken);

        var ann = new VariantAnnotation
        {
            AffectedGene = "GENE1",
            Consequence = VariantConsequence.Missense,
            HgvsCoding = "c.300C>A"
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
        await annotator.LoadAsync(stream, TestContext.Current.CancellationToken);

        // Synonymous variant → should not be annotated
        var ann = new VariantAnnotation
        {
            AffectedGene = "GENE1",
            Consequence = VariantConsequence.Synonymous,
            HgvsCoding = "c.100A>T"
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
        await annotator.LoadAsync(stream, TestContext.Current.CancellationToken);

        var ann = new VariantAnnotation
        {
            AffectedGene = "GENE1",
            Consequence = VariantConsequence.Missense,
            HgvsCoding = "c.999A>T"
        };

        var result = annotator.Annotate(MakeMissense("chr1", 9999, "A", "T"), ann);
        Assert.Null(result);
    }
}

// ─── ANN-4: Splice site prediction ───────────────────────────────────────────

public class Ann4SpliceSitePredictionTests
{
    // Donor site window: 9 characters, positions -3 to +6 relative to splice boundary
    // Canonical donor: C(-3) A(-2) G(-1) | G(+1) T(+2) A(+3) A(+4) G(+5) T(+6)
    private const string CanonicalDonorRef = "CAGGTAAGT";

    // Acceptor site window: 23 characters, positions -20 to +3
    // Canonical acceptor: TTTTTTTTTTTTTTTTTTAG|GT
    private const string CanonicalAcceptorRef = "TTTTTTTTTTTTTTTTTTTAGT";

    [Fact]
    public void SpliceSitePredictor_CanonicalDonorRef_HasHighScore()
    {
        var score = SpliceSitePredictor.ComputeDonorScore(CanonicalDonorRef.AsSpan());
        // Canonical GT donor should score well above 5.0
        Assert.True(score > 5.0, $"Canonical donor score {score} should be > 5.0");
    }

    [Fact]
    public void SpliceSitePredictor_DonorDisruption_GtAAtPlusOne_IsDisruptive()
    {
        // Change G→A at position +1 (index 3 in 0-indexed 9-char window)
        // "CAGGTAAGT" → "CAGATAAGT"
        const string altSeq = "CAGATAAGT";

        var classification = SpliceSitePredictor.ClassifyDonorVariant(
            refContext: CanonicalDonorRef.AsSpan(),
            altContext: altSeq.AsSpan(),
            threshold: 3.0);

        Assert.Equal(VariantConsequence.SpliceSiteDisruptive, classification);
    }

    [Fact]
    public void SpliceSitePredictor_ExonicPositionChange_NotDisruptive()
    {
        // Change C→A at position -3 (index 0, exonic) — small delta, not disruptive
        // "CAGGTAAGT" → "AAGGTAAGT"
        const string altSeq = "AAGGTAAGT";

        var classification = SpliceSitePredictor.ClassifyDonorVariant(
            refContext: CanonicalDonorRef.AsSpan(),
            altContext: altSeq.AsSpan(),
            threshold: 3.0);

        // Small delta → stays as SpliceSite (not disruptive)
        Assert.Equal(VariantConsequence.SpliceSite, classification);
    }

    [Fact]
    public void SpliceSitePredictor_DeepIntronicCrypticSite_IsActivation()
    {
        // Ref: no splice signal (all A's, low score)
        // Alt: canonical donor sequence in intronic position
        const string intronRef = "AAAAAAAAA"; // 9 A's → very low donor score
        const string intronAlt = "CAGGTAAGT"; // canonical donor → high score

        var classification = SpliceSitePredictor.ClassifyCrypticDonor(
            refContext: intronRef.AsSpan(),
            altContext: intronAlt.AsSpan(),
            crypticThreshold: 5.0);

        Assert.Equal(VariantConsequence.CrypticSpliceActivation, classification);
    }

    [Fact]
    public void SpliceSitePredictor_NonCrypticIntronic_StaysIntronic()
    {
        // Alt still has very low score (no splice signal)
        const string intronRef = "AAAAAAAAA";
        const string intronAlt = "AACAAAAAA"; // weak change, low score

        var classification = SpliceSitePredictor.ClassifyCrypticDonor(
            refContext: intronRef.AsSpan(),
            altContext: intronAlt.AsSpan(),
            crypticThreshold: 5.0);

        Assert.Equal(VariantConsequence.Intronic, classification);
    }

    [Fact]
    public void SpliceSitePredictor_DonorDisruption_TtoAAtPlusTwo_IsDisruptive()
    {
        // Change T→A at position +2 (index 4 in 0-indexed 9-char window): invariant T
        // "CAGGTAAGT" → "CAGGA AAGT" → "CAGGAAAGT"
        const string altSeq = "CAGGAAAGT";

        var classification = SpliceSitePredictor.ClassifyDonorVariant(
            refContext: CanonicalDonorRef.AsSpan(),
            altContext: altSeq.AsSpan(),
            threshold: 3.0);

        Assert.Equal(VariantConsequence.SpliceSiteDisruptive, classification);
    }

    [Fact]
    public void SpliceSitePredictor_EngineIntegration_AnnotatesSpliceSiteDisruptive()
    {
        // Build a transcript with canonical GT donor site at the exon|intron boundary.
        // CDS: positions 1-30 (exon), intron starts at position 31.
        // For a canonical donor: positions 31-32 must be 'G','T' (GT dinucleotide).
        // Variant: position 31, G→A — disrupts GT→AT, breaking the donor site.
        //
        // Transcript: 30-bp exon ending in "CAG" then "GTAAGT..." as intron
        //   position:  1234567890123456789012345678901234567890
        //   sequence:  ATGATGATGATGATGATGATGATGATGATGGTAAGTATG
        //                                             ^ pos 31 = 'G' (donor +1)
        const string transcriptSeq = "ATGATGATGATGATGATGATGATGATGATGGTAAGTATG";

        // CDS: 1-30, Exon: 1-30, Intron: 31-39
        var ctx = new AnnotationContext
        {
            CdsStart = 1,
            CdsEnd = 30,
            TranscriptLength = 40,
            GeneBoundaries = (1, 40),
            Introns = [(31, 39)],
            ExonBoundaries = [(1, 30)]
        };

        var transcript = new Sequence("TX_SPLICE", transcriptSeq.ToCharArray(), new string('I', transcriptSeq.Length).ToCharArray());

        using var engine = new VariantAnnotationEngine(ctx);
        engine.LoadTranscript(transcript);

        var variant = new VcfVariant
        {
            Chromosome = "chr1",
            Position = 31,  // G at +1 of donor site — disrupts GT→AT
            Reference = "G",
            Alternate = "A",
            ErrorProbabilities = [60],
            FailedFilter = [],
            MarkerIdentifiers = ".",
            AdditionalInformation = "."
        };

        var anns = engine.AnnotateVariantAsync(variant);
        Assert.NotNull(anns);
        Assert.NotEmpty(anns);
        Assert.All(anns, a => Assert.Equal(VariantConsequence.SpliceSiteDisruptive, a.Consequence));
    }
}
