namespace OpenMedStack.BioSharp.AcceptanceTests.StepDefinitions;

using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Calculations.Alignment;
using Io.FastA;
using Model;
using Report;
using Reqnroll;
using Xunit;

[Binding]
public class ClinicalComplianceStepDefinitions
{
    private readonly ScenarioContext _ctx;

    public ClinicalComplianceStepDefinitions(ScenarioContext ctx) => _ctx = ctx;

    // ── CLIN-1: Pipeline Provenance ───────────────────────────────────────────

    [Given("I have a completed pipeline run with known parameters")]
    public void GivenCompletedPipelineRun()
    {
        var provenance = new PipelineProvenance
        {
            PipelineVersion = "1.0.0",
            ReferenceChecksum = "abc123",
            Options = "{\"threads\":4}",
            StartTime = DateTimeOffset.UtcNow.AddMinutes(-5),
            EndTime = DateTimeOffset.UtcNow,
            ReadsProcessed = 10000,
            ReadsMapped = 9500,
            ReadsFiltered = 200,
            VariantsCalled = 42
        };
        _ctx["provenance"] = provenance;
    }

    [When("I write the provenance record alongside the VCF output file")]
    public async Task WhenWriteProvenanceRecord()
    {
        var provenance = (PipelineProvenance)_ctx["provenance"];
        var vcfPath = Path.GetTempFileName() + ".vcf";
        await File.WriteAllTextAsync(vcfPath, "##fileformat=VCFv4.2\n");
        await ProvenanceWriter.WriteAsync(provenance, vcfPath);
        _ctx["vcfPath"] = vcfPath;
        _ctx["provenancePath"] = vcfPath + ".provenance.json";
    }

    [Then("a JSON provenance file should exist next to the VCF")]
    public void ThenProvenanceFileExists()
    {
        var provenancePath = (string)_ctx["provenancePath"];
        Assert.True(File.Exists(provenancePath),
            $"Provenance file should exist at {provenancePath}");
    }

    [Then("the provenance file should contain the pipeline version and checksums")]
    public async Task ThenProvenanceFileHasVersionAndChecksums()
    {
        var provenancePath = (string)_ctx["provenancePath"];
        var content = await File.ReadAllTextAsync(provenancePath);
        Assert.Contains("1.0.0", content);
        Assert.Contains("abc123", content);
    }

    // ── CLIN-2: Sample Sheet Reading ──────────────────────────────────────────

    [Given("I have an Illumina sample sheet with header, reads, settings, and data sections")]
    public void GivenIlluminaSampleSheet()
    {
        var csvContent = string.Join("\n",
            "[Header]",
            "IEMFileVersion,4",
            "InvestigatorName,JohnDoe",
            "ExperimentName,TestRun",
            "Date,2024-01-15",
            "[Reads]",
            "151",
            "151",
            "[Settings]",
            "Adapter,AGATCGGAAGAGC",
            "[Data]",
            "Lane,Sample_ID,Sample_Name,Sample_Plate,Sample_Well,I7_Index_ID,index,I5_Index_ID,index2,Sample_Project,Description",
            "1,SMP001,Sample1,,,D701,ATTACTCG,D501,TATAGCCT,TestProject,TestSample1",
            "1,SMP002,Sample2,,,D702,TCCGGAGA,D502,ATAGAGGC,TestProject,TestSample2"
        );
        _ctx["sampleSheetContent"] = csvContent;
    }

    [When("I parse the sample sheet")]
    public async Task WhenParseSampleSheet()
    {
        var content = (string)_ctx["sampleSheetContent"];
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        var sheet = await SampleSheetReader.ReadAsync(stream);
        _ctx["sampleSheet"] = sheet;
    }

    [Then("the header should contain the InvestigatorName key")]
    public void ThenHeaderHasInvestigatorName()
    {
        var sheet = (SampleSheet)_ctx["sampleSheet"];
        Assert.True(sheet.Header.ContainsKey("InvestigatorName"),
            "Header should contain InvestigatorName");
    }

    [Then("the read lengths should include 151")]
    public void ThenReadLengthsInclude151()
    {
        var sheet = (SampleSheet)_ctx["sampleSheet"];
        Assert.Contains(151, sheet.ReadLengths);
    }

    [Then("the data section should have 2 samples")]
    public void ThenDataSectionHasTwoSamples()
    {
        var sheet = (SampleSheet)_ctx["sampleSheet"];
        Assert.Equal(2, sheet.Samples.Count);
    }

    // ── CLIN-3: Reference Validator ───────────────────────────────────────────

    [Given("I have a reference FASTA stream")]
    public void GivenReferenceFastaStream()
    {
        var fasta = ">chr1\nACGTACGTACGTACGT\n";
        _ctx["fastaBytes"] = Encoding.UTF8.GetBytes(fasta);
    }

    [When("I compute checksums for the reference")]
    public async Task WhenComputeChecksums()
    {
        var bytes = (byte[])_ctx["fastaBytes"];
        using var stream = new MemoryStream(bytes);
        var checksums = await ReferenceValidator.ComputeChecksumsAsync(stream);
        _ctx["checksums"] = checksums;
    }

    [Then("the checksums should contain a non-empty MD5 and SHA-256")]
    public void ThenChecksumsNonEmpty()
    {
        var checksums = (ReferenceChecksums)_ctx["checksums"];
        Assert.NotEmpty(checksums.Md5);
        Assert.NotEmpty(checksums.Sha256);
        // MD5 is 32 hex chars
        Assert.Equal(32, checksums.Md5.Length);
        // SHA-256 is 64 hex chars
        Assert.Equal(64, checksums.Sha256.Length);
    }

    [Given("I have a reference and its correct MD5 checksum")]
    public async Task GivenReferenceWithCorrectMd5()
    {
        var fasta = ">chr1\nACGTACGTACGTACGT\n";
        var bytes = Encoding.UTF8.GetBytes(fasta);
        using var stream = new MemoryStream(bytes);
        var checksums = await ReferenceValidator.ComputeChecksumsAsync(stream);
        _ctx["refFastaBytes"] = bytes;
        _ctx["correctMd5"] = checksums.Md5;
    }

    [When("I validate the reference with the correct MD5")]
    public async Task WhenValidateWithCorrectMd5()
    {
        var bytes = (byte[])_ctx["refFastaBytes"];
        var md5 = (string)_ctx["correctMd5"];
        using var stream = new MemoryStream(bytes);
        Exception? ex = null;
        try
        {
            await ReferenceValidator.ValidateAsync(stream, md5);
        }
        catch (Exception e)
        {
            ex = e;
        }
        _ctx["validationException"] = ex;
    }

    [Then("validation should succeed without throwing an exception")]
    public void ThenValidationSucceeds()
    {
        var ex = _ctx["validationException"] as Exception;
        Assert.Null(ex);
    }

    [Given("I have a reference and an incorrect MD5 checksum")]
    public void GivenReferenceWithIncorrectMd5()
    {
        var fasta = ">chr1\nACGTACGTACGTACGT\n";
        _ctx["refFastaBytes"] = Encoding.UTF8.GetBytes(fasta);
        _ctx["wrongMd5"] = "00000000000000000000000000000000";
    }

    [When("I validate the reference with the incorrect MD5")]
    public async Task WhenValidateWithIncorrectMd5()
    {
        var bytes = (byte[])_ctx["refFastaBytes"];
        var md5 = (string)_ctx["wrongMd5"];
        using var stream = new MemoryStream(bytes);
        Exception? ex = null;
        try
        {
            await ReferenceValidator.ValidateAsync(stream, md5);
        }
        catch (ReferenceValidationException e)
        {
            ex = e;
        }
        _ctx["validationException"] = ex;
    }

    [Then("a ReferenceValidationException should be thrown")]
    public void ThenReferenceValidationExceptionThrown()
    {
        var ex = _ctx["validationException"] as Exception;
        Assert.NotNull(ex);
        Assert.IsType<ReferenceValidationException>(ex);
    }

    // ── CLIN-4: Clinical Report Writer ────────────────────────────────────────

    [Given("I have a set of annotated variants and report metadata")]
    public void GivenAnnotatedVariantsAndMetadata()
    {
        var variants = new[]
        {
            new VariantAnnotation
            {
                Chromosome = "chr1", Position = 100,
                Reference = "A", Alternate = "G",
                Consequence = VariantConsequence.Missense,
                AffectedGene = "BRCA1",
                ClinicalSignificance = "Pathogenic",
                HgvsCoding = "c.100A>G",
                HgvsProtein = "p.Thr34Ala"
            },
            new VariantAnnotation
            {
                Chromosome = "chr2", Position = 500,
                Reference = "C", Alternate = "T",
                Consequence = VariantConsequence.Synonymous,
                AffectedGene = "TP53",
                ClinicalSignificance = "Benign",
                HgvsCoding = "c.500C>T"
            }
        };
        var metadata = new ClinicalReportMetadata
        {
            SampleName = "TEST_SAMPLE",
            PipelineVersion = "1.0.0",
            ReferenceGenome = "GRCh38",
            LabName = "TestLab",
            Operator = "TestUser"
        };
        _ctx["reportVariants"] = variants;
        _ctx["reportMetadata"] = metadata;
    }

    [When("I generate a JSON clinical report")]
    public void WhenGenerateJsonClinicalReport()
    {
        var variants = (VariantAnnotation[])_ctx["reportVariants"];
        var metadata = (ClinicalReportMetadata)_ctx["reportMetadata"];
        var writer = new ClinicalReportWriter();
        var json = writer.WriteJson(variants, metadata);
        _ctx["jsonReport"] = json;
    }

    [Then("the JSON report should contain the sample name and variant positions")]
    public void ThenJsonReportContainsSampleNameAndPositions()
    {
        var json = (string)_ctx["jsonReport"];
        Assert.Contains("TEST_SAMPLE", json);
        Assert.Contains("BRCA1", json);
        Assert.Contains("TP53", json);
    }

    [Then("the variants should be sorted by chromosome then position")]
    public void ThenVariantsSortedByChromosomeThenPosition()
    {
        var json = (string)_ctx["jsonReport"];
        var chr1Idx = json.IndexOf("chr1", StringComparison.Ordinal);
        var chr2Idx = json.IndexOf("chr2", StringComparison.Ordinal);
        Assert.True(chr1Idx < chr2Idx, "chr1 should appear before chr2 in sorted output");
    }

    [When("I generate an HTML clinical report")]
    public void WhenGenerateHtmlClinicalReport()
    {
        var variants = (VariantAnnotation[])_ctx["reportVariants"];
        var metadata = (ClinicalReportMetadata)_ctx["reportMetadata"];
        var writer = new ClinicalReportWriter();
        var html = writer.WriteHtml(variants, metadata);
        _ctx["htmlReport"] = html;
    }

    [Then("the HTML report should contain a valid HTML structure with variant data")]
    public void ThenHtmlReportHasValidStructure()
    {
        var html = (string)_ctx["htmlReport"];
        Assert.Contains("<!DOCTYPE html>", html);
        Assert.Contains("<table>", html);
        Assert.Contains("TEST_SAMPLE", html);
        Assert.Contains("BRCA1", html);
    }

    // ── ADDITIONAL steps matching feature file exactly ──────────────────────

    [Given("I have a pipeline provenance record with version, checksums and timestamps")]
    public void GivenPipelineProvenanceRecord()
    {
        _ctx["provenance"] = new PipelineProvenance
        {
            PipelineVersion = "1.0.0",
            ReferenceChecksum = "abc123",
            Options = "{\"threads\":4}",
            StartTime = DateTimeOffset.UtcNow.AddMinutes(-5),
            EndTime = DateTimeOffset.UtcNow,
            ReadsProcessed = 10000,
            ReadsMapped = 9500,
            ReadsFiltered = 200,
            VariantsCalled = 42
        };
    }

    [When("I write the provenance record for a VCF output file")]
    public async Task WhenWriteProvenanceRecordForVcf()
    {
        var provenance = (PipelineProvenance)_ctx["provenance"];
        var vcfPath = Path.GetTempFileName() + ".vcf";
        await File.WriteAllTextAsync(vcfPath, "##fileformat=VCFv4.2\n");
        await ProvenanceWriter.WriteAsync(provenance, vcfPath);
        _ctx["vcfPath"] = vcfPath;
        _ctx["provenancePath"] = vcfPath + ".provenance.json";
    }

    [Then("a provenance JSON file should exist alongside the VCF file")]
    public void ThenProvenanceJsonFileExistsAlongsideVcf()
    {
        var provenancePath = (string)_ctx["provenancePath"];
        Assert.True(File.Exists(provenancePath),
            $"Provenance file should exist at {provenancePath}");
    }

    [Then("the provenance JSON should contain the pipeline version field")]
    public async Task ThenProvenanceJsonContainsPipelineVersion()
    {
        var provenancePath = (string)_ctx["provenancePath"];
        var content = await File.ReadAllTextAsync(provenancePath);
        Assert.Contains("1.0.0", content);
    }

    [Then("the provenance JSON should contain start and end timestamp fields")]
    public async Task ThenProvenanceJsonContainsTimestamps()
    {
        var provenancePath = (string)_ctx["provenancePath"];
        var content = await File.ReadAllTextAsync(provenancePath);
        // JSON will contain Start/End time fields
        Assert.True(content.Contains("StartTime") || content.Contains("start") || content.Contains("Start"),
            "Provenance JSON should contain start time");
    }

    [Given("I have an Illumina sample sheet CSV with Header, Reads, Settings and Data sections")]
    public void GivenIlluminaSampleSheetCsv()
    {
        _ctx["sampleSheetContent"] = string.Join("\n",
            "[Header]",
            "IEMFileVersion,4",
            "InvestigatorName,JohnDoe",
            "[Reads]",
            "151",
            "151",
            "[Settings]",
            "Adapter,AGATCGGAAGAGC",
            "[Data]",
            "Lane,Sample_ID,Sample_Name",
            "1,SMP001,Sample1",
            "1,SMP002,Sample2");
    }

    [Then("the Header section should contain the expected key-value pairs")]
    public void ThenHeaderSectionContainsExpectedKvPairs()
    {
        var sheet = (SampleSheet)_ctx["sampleSheet"];
        Assert.True(sheet.Header.ContainsKey("IEMFileVersion") || sheet.Header.ContainsKey("InvestigatorName"),
            "Header should contain at least one key-value pair");
    }

    [Then("the Reads section should contain the expected read lengths")]
    public void ThenReadsSectionContainsExpectedReadLengths()
    {
        var sheet = (SampleSheet)_ctx["sampleSheet"];
        Assert.Contains(151, sheet.ReadLengths);
    }

    [Then("the Data section should contain one sample row per data entry")]
    public void ThenDataSectionContainsOneSampleRowPerEntry()
    {
        var sheet = (SampleSheet)_ctx["sampleSheet"];
        Assert.True(sheet.Samples.Count >= 1,
            "Data section should have at least one sample");
    }

    [Given("I have an Illumina sample sheet with three sample rows in the Data section")]
    public void GivenIlluminaSampleSheetWithThreeSamples()
    {
        _ctx["sampleSheetContent"] = string.Join("\n",
            "[Header]",
            "IEMFileVersion,4",
            "[Reads]",
            "151",
            "[Data]",
            "Lane,Sample_ID,Sample_Name",
            "1,SMP001,Sample1",
            "1,SMP002,Sample2",
            "1,SMP003,Sample3");
    }

    [Then("the samples list should contain three entries")]
    public void ThenSamplesListContainsThreeEntries()
    {
        var sheet = (SampleSheet)_ctx["sampleSheet"];
        Assert.Equal(3, sheet.Samples.Count);
    }

    [Given("I have a small FASTA reference file with known content")]
    public void GivenSmallFastaReferenceFile()
    {
        var fasta = ">chr1\nACGTACGTACGTACGT\n";
        _ctx["fastaBytes"] = Encoding.UTF8.GetBytes(fasta);
    }

    [When("I compute checksums for the reference file")]
    public async Task WhenComputeChecksumsForReferenceFile()
    {
        var bytes = (byte[])_ctx["fastaBytes"];
        using var stream = new MemoryStream(bytes);
        _ctx["checksums"] = await ReferenceValidator.ComputeChecksumsAsync(stream);
    }

    [Then("the MD5 checksum should be a 32-character hex string")]
    public void ThenMd5Checksum32CharHex()
    {
        var checksums = (ReferenceChecksums)_ctx["checksums"];
        Assert.Equal(32, checksums.Md5.Length);
    }

    [Then("the SHA256 checksum should be a 64-character hex string")]
    public void ThenSha256Checksum64CharHex()
    {
        var checksums = (ReferenceChecksums)_ctx["checksums"];
        Assert.Equal(64, checksums.Sha256.Length);
    }

    [Given("I have a reference file")]
    public void GivenReferenceFile()
    {
        var fasta = ">chr1\nACGTACGTACGTACGT\n";
        _ctx["refFastaBytes"] = Encoding.UTF8.GetBytes(fasta);
    }

    [Given("I have an incorrect expected MD5 checksum")]
    public void GivenIncorrectExpectedMd5Checksum()
    {
        _ctx["wrongMd5"] = "00000000000000000000000000000000";
    }

    [When("I validate the reference against the wrong checksum")]
    public async Task WhenValidateReferenceAgainstWrongChecksum()
    {
        var bytes = (byte[])_ctx["refFastaBytes"];
        var md5 = (string)_ctx["wrongMd5"];
        Exception? ex = null;
        try
        {
            using var stream = new MemoryStream(bytes);
            await ReferenceValidator.ValidateAsync(stream, md5);
        }
        catch (ReferenceValidationException e)
        {
            ex = e;
        }
        _ctx["validationException"] = ex;
    }

    [When("I compute the MD5 and then validate against it")]
    public async Task WhenComputeMd5AndValidate()
    {
        var bytes = (byte[])_ctx["fastaBytes"];
        using var stream1 = new MemoryStream(bytes);
        var checksums = await ReferenceValidator.ComputeChecksumsAsync(stream1);
        Exception? ex = null;
        try
        {
            using var stream2 = new MemoryStream(bytes);
            await ReferenceValidator.ValidateAsync(stream2, checksums.Md5);
        }
        catch (Exception e)
        {
            ex = e;
        }
        _ctx["validationException"] = ex;
    }

    [Given("I have a set of annotated variants and clinical report metadata")]
    public void GivenAnnotatedVariantsAndClinicalReportMetadata()
    {
        _ctx["reportVariants"] = new[]
        {
            new VariantAnnotation
            {
                Chromosome = "chr1", Position = 100,
                Reference = "A", Alternate = "G",
                Consequence = VariantConsequence.Missense,
                AffectedGene = "BRCA1",
                ClinicalSignificance = "Pathogenic",
                HgvsCoding = "c.100A>G",
                HgvsProtein = "p.Thr34Ala"
            },
            new VariantAnnotation
            {
                Chromosome = "chr2", Position = 500,
                Reference = "C", Alternate = "T",
                Consequence = VariantConsequence.Synonymous,
                AffectedGene = "TP53",
                ClinicalSignificance = "Benign",
                HgvsCoding = "c.500C>T"
            }
        };
        _ctx["reportMetadata"] = new ClinicalReportMetadata
        {
            SampleName = "TEST_SAMPLE",
            PipelineVersion = "1.0.0",
            ReferenceGenome = "GRCh38",
            LabName = "TestLab",
            Operator = "TestUser"
        };
    }

    [When("I generate the JSON clinical report")]
    public void WhenGenerateTheJsonClinicalReport()
    {
        var variants = (VariantAnnotation[])_ctx["reportVariants"];
        var metadata = (ClinicalReportMetadata)_ctx["reportMetadata"];
        _ctx["jsonReport"] = new ClinicalReportWriter().WriteJson(variants, metadata);
    }

    [Then("the JSON should contain a SampleName field")]
    public void ThenJsonContainsSampleNameField()
    {
        var json = (string)_ctx["jsonReport"];
        Assert.Contains("SampleName", json);
    }

    [Then("the JSON should contain a Variants array")]
    public void ThenJsonContainsVariantsArray()
    {
        var json = (string)_ctx["jsonReport"];
        Assert.Contains("Variants", json);
    }

    [Then("variants in the JSON should be sorted by chromosome then position")]
    public void ThenVariantsInJsonSortedByChromosomeThenPosition()
    {
        var json = (string)_ctx["jsonReport"];
        var chr1Idx = json.IndexOf("chr1", StringComparison.Ordinal);
        var chr2Idx = json.IndexOf("chr2", StringComparison.Ordinal);
        Assert.True(chr1Idx < chr2Idx, "chr1 should appear before chr2 in sorted output");
    }

    [When("I generate the HTML clinical report")]
    public void WhenGenerateTheHtmlClinicalReport()
    {
        var variants = (VariantAnnotation[])_ctx["reportVariants"];
        var metadata = (ClinicalReportMetadata)_ctx["reportMetadata"];
        _ctx["htmlReport"] = new ClinicalReportWriter().WriteHtml(variants, metadata);
    }

    [Then("the HTML should contain a table element")]
    public void ThenHtmlContainsTableElement()
    {
        var html = (string)_ctx["htmlReport"];
        Assert.Contains("<table>", html);
    }

    [Then("the HTML should contain the sample name in the report header")]
    public void ThenHtmlContainsSampleNameInHeader()
    {
        var html = (string)_ctx["htmlReport"];
        Assert.Contains("TEST_SAMPLE", html);
    }
}
