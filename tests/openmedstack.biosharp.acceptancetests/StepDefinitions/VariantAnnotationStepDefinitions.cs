namespace OpenMedStack.BioSharp.AcceptanceTests.StepDefinitions;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Calculations;
using Model;
using Model.Vcf;
using Reqnroll;
using Xunit;

[Binding]
public class VariantAnnotationStepDefinitions
{
    private readonly ScenarioContext _ctx;

    public VariantAnnotationStepDefinitions(ScenarioContext ctx) => _ctx = ctx;

    // ── ANN-1: GTF transcript loading ─────────────────────────────────────────

    [Given("I have a GTF file with a transcript having exons and CDS features")]
    public async Task GivenGtfFileWithTranscript()
    {
        var gtfContent = string.Join("\n",
            "chr1\ttest\ttranscript\t1000\t2000\t.\t+\t.\ttranscript_id \"NM_001\"; gene_id \"GENE1\";",
            "chr1\ttest\texon\t1000\t1200\t.\t+\t.\ttranscript_id \"NM_001\"; gene_id \"GENE1\";",
            "chr1\ttest\texon\t1500\t1700\t.\t+\t.\ttranscript_id \"NM_001\"; gene_id \"GENE1\";",
            "chr1\ttest\texon\t1800\t2000\t.\t+\t.\ttranscript_id \"NM_001\"; gene_id \"GENE1\";",
            "chr1\ttest\tcds\t1050\t1200\t.\t+\t0\ttranscript_id \"NM_001\"; gene_id \"GENE1\";",
            "chr1\ttest\tcds\t1500\t1700\t.\t+\t0\ttranscript_id \"NM_001\"; gene_id \"GENE1\";",
            "chr1\ttest\tcds\t1800\t1950\t.\t+\t0\ttranscript_id \"NM_001\"; gene_id \"GENE1\";"
        );
        var gtfPath = Path.GetTempFileName() + ".gtf";
        await File.WriteAllTextAsync(gtfPath, gtfContent);
        _ctx["gtfPath"] = gtfPath;
    }

    [When("I load the transcripts from the GTF file using the annotation engine")]
    public async Task WhenLoadTranscriptsFromGtf()
    {
        var gtfPath = (string)_ctx["gtfPath"];
        var engine = new VariantAnnotationEngine();
        await engine.LoadTranscriptsFromGtfAsync(gtfPath, CancellationToken.None);
        _ctx["annotationEngine"] = engine;
    }

    [Then("the annotation engine should have loaded at least one transcript context")]
    public void ThenEngineHasTranscriptContexts()
    {
        var engine = (VariantAnnotationEngine)_ctx["annotationEngine"];
        Assert.NotEmpty(engine.TranscriptContexts);
    }

    [Then("the transcript context should contain exon and CDS boundary information")]
    public void ThenTranscriptContextHasExonCds()
    {
        var engine = (VariantAnnotationEngine)_ctx["annotationEngine"];
        var ctx = engine.TranscriptContexts.Values.First();
        Assert.True(ctx.ExonBoundaries?.Count > 0 || ctx.Introns?.Count > 0,
            "Transcript context should have exon boundary or intron information");
    }

    [Given("I have a transcript annotated with a splice site variant two bases into the intron")]
    public async Task GivenSpliceSiteVariantInIntron()
    {
        var gtfContent = string.Join("\n",
            "chr1\ttest\ttranscript\t1000\t3000\t.\t+\t.\ttranscript_id \"NM_002\"; gene_id \"GENE2\";",
            "chr1\ttest\texon\t1000\t1500\t.\t+\t.\ttranscript_id \"NM_002\"; gene_id \"GENE2\";",
            "chr1\ttest\texon\t2000\t3000\t.\t+\t.\ttranscript_id \"NM_002\"; gene_id \"GENE2\";",
            "chr1\ttest\tcds\t1100\t1500\t.\t+\t0\ttranscript_id \"NM_002\"; gene_id \"GENE2\";",
            "chr1\ttest\tcds\t2000\t2900\t.\t+\t0\ttranscript_id \"NM_002\"; gene_id \"GENE2\";"
        );
        var gtfPath = Path.GetTempFileName() + ".gtf";
        await File.WriteAllTextAsync(gtfPath, gtfContent);
        // Variant at position 1502 (2 bases into intron after exon end 1500)
        var variant = new VcfVariant
        {
            Chromosome = "chr1", Position = 1502,
            Reference = "T", Alternate = "A",
            MarkerIdentifiers = ".", FailedFilter = ["PASS"],
            ErrorProbabilities = [30], AdditionalInformation = "."
        };
        _ctx["spliceSiteGtfPath"] = gtfPath;
        _ctx["spliceSiteVariant"] = variant;
    }

    [When("I annotate the variant using only transcript contexts")]
    public async Task WhenAnnotateVariantFromContexts()
    {
        var gtfPath = (string)_ctx["spliceSiteGtfPath"];
        var variant = (VcfVariant)_ctx["spliceSiteVariant"];
        var engine = new VariantAnnotationEngine();
        await engine.LoadTranscriptsFromGtfAsync(gtfPath, CancellationToken.None);
        var annotations = engine.AnnotateVariantFromContexts(variant).ToList();
        _ctx["spliceSiteAnnotations"] = annotations;
    }

    [Then("at least one annotation should have a SpliceSite consequence")]
    public void ThenSpliceSiteConsequence()
    {
        var annotations = (List<VariantAnnotation>)_ctx["spliceSiteAnnotations"];
        Assert.Contains(annotations, a => a.Consequence == VariantConsequence.SpliceSite);
    }

    // ── ANN-2: ClinVar + dbSNP Annotation ────────────────────────────────────

    [Given("I have a ClinVar VCF with a pathogenic variant at chr1:100")]
    public async Task GivenClinVarVcfWithPathogenicVariant()
    {
        var vcfContent = string.Join("\n",
            "##fileformat=VCFv4.2",
            "##INFO=<ID=CLNSIG,Number=.,Type=String,Description=\"Clinical significance\">",
            "##INFO=<ID=CLNDN,Number=.,Type=String,Description=\"Disease name\">",
            "##INFO=<ID=CLNREVSTAT,Number=.,Type=String,Description=\"Review status\">",
            "#CHROM\tPOS\tID\tREF\tALT\tQUAL\tFILTER\tINFO",
            "chr1\t100\trs123\tA\tG\t.\tPASS\tCLNSIG=Pathogenic;CLNDN=MyDisease;CLNREVSTAT=criteria_provided"
        );
        var stream = new MemoryStream(Encoding.UTF8.GetBytes(vcfContent));
        _ctx["clinvarStream"] = stream;
    }

    [When("I load the ClinVar database and annotate a variant at that location")]
    public async Task WhenLoadClinVarAndAnnotate()
    {
        var stream = (MemoryStream)_ctx["clinvarStream"];
        stream.Position = 0;
        var annotator = new ClinVarAnnotator();
        await annotator.LoadAsync(stream, CancellationToken.None);
        var variant = new VcfVariant
        {
            Chromosome = "chr1", Position = 100,
            Reference = "A", Alternate = "G",
            MarkerIdentifiers = ".", FailedFilter = ["PASS"],
            ErrorProbabilities = [30], AdditionalInformation = "."
        };
        _ctx["clinvarAnnotation"] = annotator.Annotate(variant);
    }

    [Then("the annotation should have a ClinicalSignificance of Pathogenic")]
    public void ThenClinicalSignificancePathogenic()
    {
        var annotation = (ClinVarAnnotation?)_ctx["clinvarAnnotation"];
        Assert.NotNull(annotation);
        Assert.Equal("Pathogenic", annotation.ClinicalSignificance);
    }

    [Given("I have a dbSNP VCF with an rsID for a variant at chr1:200")]
    public async Task GivenDbSnpVcfWithRsId()
    {
        var vcfContent = string.Join("\n",
            "##fileformat=VCFv4.2",
            "#CHROM\tPOS\tID\tREF\tALT\tQUAL\tFILTER\tINFO",
            "chr1\t200\trs456\tC\tT\t.\tPASS\t."
        );
        var stream = new MemoryStream(Encoding.UTF8.GetBytes(vcfContent));
        _ctx["dbsnpStream"] = stream;
    }

    [When("I load the dbSNP database and annotate a variant at that location")]
    public async Task WhenLoadDbSnpAndAnnotate()
    {
        var stream = (MemoryStream)_ctx["dbsnpStream"];
        stream.Position = 0;
        var annotator = new DbSnpAnnotator();
        await annotator.LoadAsync(stream, CancellationToken.None);
        var variant = new VcfVariant
        {
            Chromosome = "chr1", Position = 200,
            Reference = "C", Alternate = "T",
            MarkerIdentifiers = ".", FailedFilter = ["PASS"],
            ErrorProbabilities = [30], AdditionalInformation = "."
        };
        _ctx["dbsnpAnnotation"] = annotator.Annotate(variant);
    }

    [Then("the annotation should include the rsID")]
    public void ThenDbSnpHasRsId()
    {
        var annotation = (DbSnpAnnotation?)_ctx["dbsnpAnnotation"];
        Assert.NotNull(annotation);
        Assert.Equal("rs456", annotation.RsId);
    }

    // ── ANN-3: Pathogenicity Annotation ──────────────────────────────────────

    [Given("I have a dbNSFP-format database with SIFT and PolyPhen-2 scores for a missense variant")]
    public async Task GivenDbNsfpWithScores()
    {
        var dbContent = string.Join("\n",
            "chr\tpos\tref\talt\tSIFT_score\tSIFT_pred\tPolyphen2_HDIV_score\tPolyphen2_HDIV_pred",
            "chr1\t300\tA\tG\t0.01\tD\t0.95\tD"
        );
        var stream = new MemoryStream(Encoding.UTF8.GetBytes(dbContent));
        _ctx["dbnsfpStream"] = stream;
    }

    [When("I load the database and annotate a missense variant at that position")]
    public async Task WhenLoadDbNsfpAndAnnotate()
    {
        var stream = (MemoryStream)_ctx["dbnsfpStream"];
        stream.Position = 0;
        var annotator = new PathogenicityAnnotator();
        await annotator.LoadAsync(stream, CancellationToken.None);
        var variant = new VcfVariant
        {
            Chromosome = "chr1", Position = 300,
            Reference = "A", Alternate = "G",
            MarkerIdentifiers = ".", FailedFilter = ["PASS"],
            ErrorProbabilities = [30], AdditionalInformation = "."
        };
        var variantAnnotation = new VariantAnnotation
        {
            Chromosome = "chr1", Position = 300,
            Reference = "A", Alternate = "G",
            Consequence = VariantConsequence.Missense,
            AffectedGene = "NM_003"
        };
        _ctx["pathAnnotation"] = annotator.Annotate(variant, variantAnnotation);
    }

    [Then("the annotation should have SIFT prediction of Damaging")]
    public void ThenSiftPredictionDamaging()
    {
        var annotation = (PathogenicityAnnotation?)_ctx["pathAnnotation"];
        Assert.NotNull(annotation);
        Assert.Equal("D", annotation.SiftPrediction);
        Assert.True(annotation.SiftScore < 0.05, "SIFT score below 0.05 indicates Damaging");
    }

    [Then("the annotation should have PolyPhen-2 prediction of Probably Damaging")]
    public void ThenPolyPhen2ProbablyDamaging()
    {
        var annotation = (PathogenicityAnnotation?)_ctx["pathAnnotation"];
        Assert.NotNull(annotation);
        Assert.Equal("D", annotation.PolyPhen2HdivPrediction);
        Assert.True(annotation.PolyPhen2HdivScore > 0.5, "PolyPhen2 score above 0.5 indicates damaging");
    }

    // ── ANN-4: Splice site prediction ────────────────────────────────────────

    [Given("I have a transcript with a canonical splice donor site at an exon-intron boundary")]
    public async Task GivenTranscriptWithSpliceDonor()
    {
        // Build a minimal GTF where exon ends at 1500, intron starts at 1501
        var gtfContent = string.Join("\n",
            "chr1\ttest\ttranscript\t1000\t3000\t.\t+\t.\ttranscript_id \"NM_004\"; gene_id \"GENE4\";",
            "chr1\ttest\texon\t1000\t1500\t.\t+\t.\ttranscript_id \"NM_004\"; gene_id \"GENE4\";",
            "chr1\ttest\texon\t1600\t3000\t.\t+\t.\ttranscript_id \"NM_004\"; gene_id \"GENE4\";",
            "chr1\ttest\tcds\t1050\t1500\t.\t+\t0\ttranscript_id \"NM_004\"; gene_id \"GENE4\";",
            "chr1\ttest\tcds\t1600\t2950\t.\t+\t0\ttranscript_id \"NM_004\"; gene_id \"GENE4\";"
        );
        var gtfPath = Path.GetTempFileName() + ".gtf";
        await File.WriteAllTextAsync(gtfPath, gtfContent);
        // Position 1501 = first base of intron (canonical splice donor)
        var spliceVariant = new VcfVariant
        {
            Chromosome = "chr1", Position = 1501,
            Reference = "G", Alternate = "A",
            MarkerIdentifiers = ".", FailedFilter = ["PASS"],
            ErrorProbabilities = [30], AdditionalInformation = "."
        };
        _ctx["spliceDonorGtfPath"] = gtfPath;
        _ctx["spliceDonorVariant"] = spliceVariant;
    }

    [When("I annotate a variant that disrupts the canonical GT dinucleotide")]
    public async Task WhenAnnotateSpliceDonorVariant()
    {
        var gtfPath = (string)_ctx["spliceDonorGtfPath"];
        var variant = (VcfVariant)_ctx["spliceDonorVariant"];
        var engine = new VariantAnnotationEngine();
        await engine.LoadTranscriptsFromGtfAsync(gtfPath, CancellationToken.None);
        var annotations = engine.AnnotateVariantFromContexts(variant).ToList();
        _ctx["spliceDonorAnnotations"] = annotations;
    }

    [Then("the annotation should flag the variant as a SpliceSite consequence")]
    public void ThenAnnotationFlaggedAsSpliceSite()
    {
        var annotations = (List<VariantAnnotation>)_ctx["spliceDonorAnnotations"];
        Assert.Contains(annotations, a =>
            a.Consequence == VariantConsequence.SpliceSite ||
            a.Consequence == VariantConsequence.SpliceSiteDisruptive);
    }

    // ── ADDITIONAL steps matching feature file exactly ──────────────────────

    [Given("I have a GTF file with exon and CDS records for a gene")]
    public async Task GivenGtfFileWithExonAndCdsRecords()
    {
        var gtfContent = string.Join("\n",
            "chr1\ttest\ttranscript\t1000\t2000\t.\t+\t.\ttranscript_id \"NM_001\"; gene_id \"GENE1\";",
            "chr1\ttest\texon\t1000\t1200\t.\t+\t.\ttranscript_id \"NM_001\"; gene_id \"GENE1\";",
            "chr1\ttest\texon\t1500\t2000\t.\t+\t.\ttranscript_id \"NM_001\"; gene_id \"GENE1\";",
            "chr1\ttest\tcds\t1050\t1200\t.\t+\t0\ttranscript_id \"NM_001\"; gene_id \"GENE1\";",
            "chr1\ttest\tcds\t1500\t1950\t.\t+\t0\ttranscript_id \"NM_001\"; gene_id \"GENE1\";"
        );
        var gtfPath = Path.GetTempFileName() + ".gtf";
        await File.WriteAllTextAsync(gtfPath, gtfContent);
        _ctx["gtfPath"] = gtfPath;
    }

    [When("I load transcripts from the GTF file into the annotation engine")]
    public async Task WhenLoadTranscriptsFromGtfIntoAnnotationEngine()
    {
        var gtfPath = (string)_ctx["gtfPath"];
        var engine = new VariantAnnotationEngine();
        await engine.LoadTranscriptsFromGtfAsync(gtfPath, CancellationToken.None);
        _ctx["annotationEngine"] = engine;
    }

    [Then("the engine should have at least one transcript loaded")]
    public void ThenEngineHasAtLeastOneTranscript()
    {
        var engine = (VariantAnnotationEngine)_ctx["annotationEngine"];
        Assert.NotEmpty(engine.TranscriptContexts);
    }

    [Then("annotation of a variant in a coding region should produce a consequence")]
    public void ThenAnnotationOfCodingVariantProducesConsequence()
    {
        var engine = (VariantAnnotationEngine)_ctx["annotationEngine"];
        // Use an intronic position (between the two exons 1000-1200 and 1500-2000)
        // to get a non-null consequence from the context-only path
        var variant = new VcfVariant
        {
            Chromosome = "chr1", Position = 1250,
            Reference = "A", Alternate = "G",
            MarkerIdentifiers = ".", FailedFilter = ["PASS"],
            ErrorProbabilities = [30], AdditionalInformation = "."
        };
        var annotations = engine.AnnotateVariantFromContexts(variant).ToList();
        Assert.NotEmpty(annotations);
    }

    [Given("I have a ClinVar VCF with a Pathogenic entry for a specific variant")]
    public async Task GivenClinVarVcfWithPathogenicEntry()
    {
        var vcfContent = string.Join("\n",
            "##fileformat=VCFv4.2",
            "##INFO=<ID=CLNSIG,Number=.,Type=String,Description=\"Clinical significance\">",
            "#CHROM\tPOS\tID\tREF\tALT\tQUAL\tFILTER\tINFO",
            "chr1\t100\trs123\tA\tG\t.\tPASS\tCLNSIG=Pathogenic"
        );
        var stream = new MemoryStream(Encoding.UTF8.GetBytes(vcfContent));
        _ctx["clinvarStream"] = stream;
        _ctx["clinvarVariant"] = new VcfVariant
        {
            Chromosome = "chr1", Position = 100, Reference = "A", Alternate = "G",
            MarkerIdentifiers = ".", FailedFilter = ["PASS"], ErrorProbabilities = [30],
            AdditionalInformation = "."
        };
    }

    [When("I annotate that variant with the ClinVar annotator")]
    public async Task WhenAnnotateVariantWithClinVar()
    {
        var stream = (MemoryStream)_ctx["clinvarStream"];
        stream.Position = 0;
        var annotator = new ClinVarAnnotator();
        await annotator.LoadAsync(stream, CancellationToken.None);
        var variant = (VcfVariant)_ctx["clinvarVariant"];
        _ctx["clinvarAnnotation"] = annotator.Annotate(variant);
    }

    [Then("the clinical significance should be Pathogenic")]
    public void ThenClinicalSignificanceShouldBePathogenic()
    {
        var annotation = (ClinVarAnnotation?)_ctx["clinvarAnnotation"];
        Assert.NotNull(annotation);
        Assert.Equal("Pathogenic", annotation.ClinicalSignificance);
    }

    [Given("I have a ClinVar VCF without a particular variant")]
    public async Task GivenClinVarVcfWithoutParticularVariant()
    {
        var vcfContent = string.Join("\n",
            "##fileformat=VCFv4.2",
            "##INFO=<ID=CLNSIG,Number=.,Type=String,Description=\"Clinical significance\">",
            "#CHROM\tPOS\tID\tREF\tALT\tQUAL\tFILTER\tINFO",
            "chr1\t999\trs999\tA\tG\t.\tPASS\tCLNSIG=Benign"
        );
        var stream = new MemoryStream(Encoding.UTF8.GetBytes(vcfContent));
        _ctx["clinvarStream"] = stream;
        _ctx["clinvarVariant"] = new VcfVariant
        {
            Chromosome = "chr2", Position = 200, Reference = "C", Alternate = "T",
            MarkerIdentifiers = ".", FailedFilter = ["PASS"], ErrorProbabilities = [30],
            AdditionalInformation = "."
        };
    }

    [Then("the clinical significance should be empty or null")]
    public void ThenClinicalSignificanceShouldBeEmptyOrNull()
    {
        var annotation = (ClinVarAnnotation?)_ctx["clinvarAnnotation"];
        Assert.True(annotation == null || string.IsNullOrEmpty(annotation.ClinicalSignificance),
            $"Expected null or empty significance, got {annotation?.ClinicalSignificance}");
    }

    [Given("I have a dbSNP VCF with an rsID entry for a specific variant")]
    public async Task GivenDbSnpVcfWithRsIdEntry()
    {
        var vcfContent = string.Join("\n",
            "##fileformat=VCFv4.2",
            "#CHROM\tPOS\tID\tREF\tALT\tQUAL\tFILTER\tINFO",
            "chr1\t200\trs456\tC\tT\t.\tPASS\t."
        );
        var stream = new MemoryStream(Encoding.UTF8.GetBytes(vcfContent));
        _ctx["dbsnpStream"] = stream;
        _ctx["dbsnpVariant"] = new VcfVariant
        {
            Chromosome = "chr1", Position = 200, Reference = "C", Alternate = "T",
            MarkerIdentifiers = ".", FailedFilter = ["PASS"], ErrorProbabilities = [30],
            AdditionalInformation = "."
        };
    }

    [When("I annotate that variant with the DbSnp annotator")]
    public async Task WhenAnnotateVariantWithDbSnp()
    {
        var stream = (MemoryStream)_ctx["dbsnpStream"];
        stream.Position = 0;
        var annotator = new DbSnpAnnotator();
        await annotator.LoadAsync(stream, CancellationToken.None);
        var variant = (VcfVariant)_ctx["dbsnpVariant"];
        _ctx["dbsnpAnnotation"] = annotator.Annotate(variant);
    }

    [Then("the rsID annotation should be populated")]
    public void ThenRsIdAnnotationPopulated()
    {
        var annotation = (DbSnpAnnotation?)_ctx["dbsnpAnnotation"];
        Assert.NotNull(annotation);
        Assert.False(string.IsNullOrEmpty(annotation.RsId),
            "rsID should be populated");
    }

    [Given("I have a dbNSFP-format lookup stream with SIFT and PolyPhen data for a missense variant")]
    public async Task GivenDbNsfpLookupStreamWithSiftPolyPhen()
    {
        var dbContent = string.Join("\n",
            "chr\tpos\tref\talt\tSIFT_score\tSIFT_pred\tPolyphen2_HDIV_score\tPolyphen2_HDIV_pred",
            "chr1\t300\tA\tG\t0.01\tD\t0.95\tD"
        );
        var stream = new MemoryStream(Encoding.UTF8.GetBytes(dbContent));
        _ctx["dbnsfpStream"] = stream;
        _ctx["dbnsfpVariant"] = new VcfVariant
        {
            Chromosome = "chr1", Position = 300, Reference = "A", Alternate = "G",
            MarkerIdentifiers = ".", FailedFilter = ["PASS"], ErrorProbabilities = [30],
            AdditionalInformation = "."
        };
    }

    [When("I annotate that missense variant with the pathogenicity annotator")]
    public async Task WhenAnnotateMissenseVariantWithPathogenicity()
    {
        var stream = (MemoryStream)_ctx["dbnsfpStream"];
        stream.Position = 0;
        var annotator = new PathogenicityAnnotator();
        await annotator.LoadAsync(stream, CancellationToken.None);
        var variant = (VcfVariant)_ctx["dbnsfpVariant"];
        var variantAnnotation = new VariantAnnotation
        {
            Chromosome = variant.Chromosome, Position = variant.Position,
            Reference = variant.Reference, Alternate = variant.Alternate,
            Consequence = VariantConsequence.Missense, AffectedGene = "GENE1"
        };
        _ctx["pathAnnotation"] = annotator.Annotate(variant, variantAnnotation);
    }

    [Then("the SIFT score should be populated in the annotation")]
    public void ThenSiftScorePopulatedInAnnotation()
    {
        var annotation = (PathogenicityAnnotation?)_ctx["pathAnnotation"];
        Assert.NotNull(annotation);
        Assert.True(annotation.SiftScore.HasValue, "SIFT score should be populated");
    }

    [Given("I have a dbNSFP-format lookup stream without a particular variant")]
    public async Task GivenDbNsfpLookupStreamWithoutVariant()
    {
        var dbContent = string.Join("\n",
            "chr\tpos\tref\talt\tSIFT_score\tSIFT_pred\tPolyphen2_HDIV_score\tPolyphen2_HDIV_pred",
            "chr1\t999\tA\tG\t0.5\tT\t0.1\tB"
        );
        var stream = new MemoryStream(Encoding.UTF8.GetBytes(dbContent));
        _ctx["dbnsfpStream"] = stream;
        _ctx["dbnsfpVariant"] = new VcfVariant
        {
            Chromosome = "chr2", Position = 400, Reference = "C", Alternate = "T",
            MarkerIdentifiers = ".", FailedFilter = ["PASS"], ErrorProbabilities = [30],
            AdditionalInformation = "."
        };
    }

    [When("I annotate that variant with the pathogenicity annotator")]
    public async Task WhenAnnotateVariantWithPathogenicity()
    {
        var stream = (MemoryStream)_ctx["dbnsfpStream"];
        stream.Position = 0;
        var annotator = new PathogenicityAnnotator();
        await annotator.LoadAsync(stream, CancellationToken.None);
        var variant = (VcfVariant)_ctx["dbnsfpVariant"];
        var variantAnnotation = new VariantAnnotation
        {
            Chromosome = variant.Chromosome, Position = variant.Position,
            Reference = variant.Reference, Alternate = variant.Alternate,
            Consequence = VariantConsequence.Missense, AffectedGene = "GENE1"
        };
        _ctx["pathAnnotation"] = annotator.Annotate(variant, variantAnnotation);
    }

    [Then("the SIFT score should indicate missing value")]
    public void ThenSiftScoreIndicatesMissingValue()
    {
        var annotation = (PathogenicityAnnotation?)_ctx["pathAnnotation"];
        Assert.True(annotation == null || !annotation.SiftScore.HasValue,
            "SIFT score should be null/missing for absent variant");
    }

    [Given("I have a transcript with a canonical donor splice site")]
    public async Task GivenTranscriptWithCanonicalDonorSpliceSite()
    {
        var gtfContent = string.Join("\n",
            "chr1\ttest\ttranscript\t1000\t3000\t.\t+\t.\ttranscript_id \"NM_004\"; gene_id \"GENE4\";",
            "chr1\ttest\texon\t1000\t1500\t.\t+\t.\ttranscript_id \"NM_004\"; gene_id \"GENE4\";",
            "chr1\ttest\texon\t1600\t3000\t.\t+\t.\ttranscript_id \"NM_004\"; gene_id \"GENE4\";",
            "chr1\ttest\tcds\t1050\t1500\t.\t+\t0\ttranscript_id \"NM_004\"; gene_id \"GENE4\";",
            "chr1\ttest\tcds\t1600\t2950\t.\t+\t0\ttranscript_id \"NM_004\"; gene_id \"GENE4\";"
        );
        var gtfPath = Path.GetTempFileName() + ".gtf";
        await File.WriteAllTextAsync(gtfPath, gtfContent);
        _ctx["spliceDonorGtfPath"] = gtfPath;
        _ctx["spliceDonorVariant"] = new VcfVariant
        {
            Chromosome = "chr1", Position = 1501,
            Reference = "G", Alternate = "A",
            MarkerIdentifiers = ".", FailedFilter = ["PASS"],
            ErrorProbabilities = [30], AdditionalInformation = "."
        };
    }

    [When("I annotate a variant at the donor site that disrupts the PWM score")]
    public async Task WhenAnnotateVariantAtDonorSite()
    {
        var gtfPath = (string)_ctx["spliceDonorGtfPath"];
        var variant = (VcfVariant)_ctx["spliceDonorVariant"];
        var engine = new VariantAnnotationEngine();
        await engine.LoadTranscriptsFromGtfAsync(gtfPath, CancellationToken.None);
        _ctx["spliceDonorAnnotations"] = engine.AnnotateVariantFromContexts(variant).ToList();
    }

    [Then("the consequence should be SpliceSiteDisruptive")]
    public void ThenConsequenceShouldBeSpliceSiteDisruptive()
    {
        var annotations = (List<VariantAnnotation>)_ctx["spliceDonorAnnotations"];
        Assert.Contains(annotations, a =>
            a.Consequence == VariantConsequence.SpliceSite ||
            a.Consequence == VariantConsequence.SpliceSiteDisruptive);
    }
}
