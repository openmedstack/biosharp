using System;
using OpenMedStack.BioSharp.Calculations.Alignment;
using OpenMedStack.BioSharp.Calculations.Report;
using OpenMedStack.BioSharp.Model;
using Xunit;

namespace OpenMedStack.BioSharp.Calculations.Tests;

public class ClinicalReportWriterTests
{
    private static VariantAnnotation MakeAnnotation(string chrom, int pos, string hgvs, string clinSig)
        => new VariantAnnotation
        {
            Chromosome = chrom,
            Position = pos,
            Reference = "A",
            Alternate = "T",
            HgvsNotation = hgvs,
            ClinicalSignificance = clinSig,
            Consequence = VariantConsequence.Missense
        };

    [Fact]
    public void ClinicalReportWriter_JsonReport_ContainsAllVariants()
    {
        var variants = new[]
        {
            MakeAnnotation("chr1", 100, "NM_001.1:c.100A>T", "Pathogenic"),
            MakeAnnotation("chr1", 200, "NM_001.1:c.200A>T", "Benign"),
        };

        var metadata = new ClinicalReportMetadata
        {
            SampleName = "Sample1",
            RunDate = DateTimeOffset.UtcNow,
            PipelineVersion = "1.1.0",
            ReferenceGenome = "GRCh38"
        };

        var json = ClinicalReportWriter.WriteJson(variants, metadata);

        Assert.Contains("chr1", json);
        Assert.Contains("Pathogenic", json);
        Assert.Contains("Sample1", json);
        Assert.Contains("GRCh38", json);
    }

    [Fact]
    public void ClinicalReportWriter_JsonReport_VariantsSortedByChromPos()
    {
        var variants = new[]
        {
            MakeAnnotation("chr1", 300, "c.300A>T", "VUS"),
            MakeAnnotation("chr1", 100, "c.100A>T", "Pathogenic"),
        };

        var json = ClinicalReportWriter.WriteJson(variants, new ClinicalReportMetadata());

        // Position 100 must come before 300 in JSON
        var idx100 = json.IndexOf("100", StringComparison.Ordinal);
        var idx300 = json.IndexOf("300", StringComparison.Ordinal);
        Assert.True(idx100 < idx300, "Variants should be sorted by position");
    }

    [Fact]
    public void ClinicalReportWriter_HtmlReport_ContainsRequiredElements()
    {
        var variants = new[]
        {
            MakeAnnotation("chr1", 100, "c.100A>T", "Pathogenic")
        };

        var html = ClinicalReportWriter.WriteHtml(variants, new ClinicalReportMetadata { SampleName = "S1" });

        Assert.Contains("<html>", html);
        Assert.Contains("<table>", html);
        Assert.Contains("Pathogenic", html);
        Assert.Contains("S1", html);
        // No JavaScript (renders without JS)
        Assert.DoesNotContain("<script>", html);
    }

    [Fact]
    public void ClinicalReportWriter_HtmlReport_IncludesQcMetrics()
    {
        var variants = Array.Empty<VariantAnnotation>();
        var metadata = new ClinicalReportMetadata { SampleName = "S1" };
        var qcSummary = new QcSummary
        {
            MeanCoverage = 30.5,
            MappingRate = 0.98,
            DuplicateRate = 0.05
        };

        var html = ClinicalReportWriter.WriteHtml(variants, metadata, qcSummary);

        Assert.Contains("30.5", html);
        Assert.Contains("0.98", html);
    }
}