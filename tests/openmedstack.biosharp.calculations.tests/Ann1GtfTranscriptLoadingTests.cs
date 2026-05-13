// Tests for ANN-1 through ANN-4 (Variant Annotation improvements)

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
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

    private static string GtfPath(string dir, string name) => Path.Combine(dir, $"{name}.gtf");

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
        await engine.LoadTranscriptsFromGtf(gtf, TestContext.Current.CancellationToken);

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
        await engine.LoadTranscriptsFromGtf(gtf, TestContext.Current.CancellationToken);

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
        await engine.LoadTranscriptsFromGtf(gtf, TestContext.Current.CancellationToken);

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
        await engine.LoadTranscriptsFromGtf(gtf, TestContext.Current.CancellationToken);

        Assert.Equal("TX_CANONICAL", engine.CanonicalTranscriptId);
        Assert.True(engine.TranscriptContexts.ContainsKey("TX_CANONICAL"));
        Assert.True(engine.TranscriptContexts.ContainsKey("TX_OTHER"));
    }

    [Fact]
    public async Task LoadTranscriptsFromGtfAsync_NonExistentFile_Throws()
    {
        using var engine = new VariantAnnotationEngine();
        await Assert.ThrowsAsync<FileNotFoundException>(
            () => engine.LoadTranscriptsFromGtf("/nonexistent/path.gtf"));
    }

    [Fact]
    public async Task AnnotateVariantFromContexts_PositionInCds_ReturnsNullConsequence()
    {
        // Variant inside CDS → not a non-coding consequence
        var gtf = GtfPath(_tempDir, "cds_pos");
        await File.WriteAllTextAsync(gtf, SingleExonGtf());

        using var engine = new VariantAnnotationEngine();
        await engine.LoadTranscriptsFromGtf(gtf, TestContext.Current.CancellationToken);

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
        await engine.LoadTranscriptsFromGtf(gtf, TestContext.Current.CancellationToken);

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
        await engine.LoadTranscriptsFromGtf(gtf, TestContext.Current.CancellationToken);

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
        await gtfEngine.LoadTranscriptsFromGtf(gtf, TestContext.Current.CancellationToken);

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

// ─── ANN-3: Pathogenicity score integration ──────────────────────────────────

// ─── ANN-4: Splice site prediction ───────────────────────────────────────────