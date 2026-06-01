using System;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace OpenMedStack.BioSharp.AnnotationDb.Tests;

/// <summary>
/// Verifies that Ensembl-style imports succeed even when the GTF carries an unversioned
/// <c>transcript_id</c> (e.g. "ENST00000511072") while the cDNA FASTA uses a versioned
/// header (e.g. ">ENST00000511072.5 cdna …").
/// </summary>
public sealed class EnsemblVersionNormalizationTests : IAsyncDisposable
{
    private readonly string _tmpGtf;
    private readonly string _tmpFa;

    public EnsemblVersionNormalizationTests()
    {
        _tmpGtf = Path.Combine(Path.GetTempPath(), $"ensembl_test_{Guid.NewGuid():N}.gtf");
        _tmpFa  = Path.Combine(Path.GetTempPath(), $"ensembl_test_{Guid.NewGuid():N}.fa");

        // GTF: unversioned transcript_id  (Ensembl GTF style)
        // Has two "tag" entries to exercise the comma-concatenation fix for duplicate GTF keys.
        File.WriteAllText(_tmpGtf, string.Join("\n",
            "#!genome-build GRCh38.p14",
            "1\thavana\ttranscript\t3069168\t3434342\t.\t+\t.\t" +
                "gene_id \"ENSG00000142611\"; transcript_id \"ENST00000511072\"; " +
                "gene_name \"PRDM16\"; tag \"gencode_basic\"; tag \"Ensembl_canonical\";",
            "1\thavana\texon\t3069168\t3069296\t.\t+\t.\t" +
                "gene_id \"ENSG00000142611\"; transcript_id \"ENST00000511072\"; gene_name \"PRDM16\";",
            "1\thavana\tCDS\t3069260\t3069296\t.\t+\t0\t" +
                "gene_id \"ENSG00000142611\"; transcript_id \"ENST00000511072\"; gene_name \"PRDM16\";",
            "1\thavana\texon\t3186125\t3186474\t.\t+\t.\t" +
                "gene_id \"ENSG00000142611\"; transcript_id \"ENST00000511072\"; gene_name \"PRDM16\";",
            "1\thavana\tCDS\t3186125\t3186474\t.\t+\t2\t" +
                "gene_id \"ENSG00000142611\"; transcript_id \"ENST00000511072\"; gene_name \"PRDM16\";",
            ""));

        // FASTA: versioned ID  (Ensembl cDNA FASTA style)
        File.WriteAllText(_tmpFa, string.Join("\n",
            ">ENST00000511072.5 cdna chromosome:GRCh38:1:3069168:3434342:1 gene:ENSG00000142611.17",
            "ATGCATGCATGCATGCATGCATGCATGCATGCATGCATGCATGCATGCATGCATGCATGC",
            "ATGCATGCATGCATGCATGCATGCATGCATGCATGCATGCATGCATGCATGCATGCATGC",
            ""));
    }

    public async ValueTask DisposeAsync()
    {
        await Task.Yield();
        foreach (var f in new[] { _tmpGtf, _tmpFa })
        {
            try { File.Delete(f); } catch { /* ignore */ }
        }
    }

    private static TranscriptAnnotationDatabase CreateInMemoryDatabase(out TranscriptAnnotationDbContext ctx)
    {
        ctx = new TranscriptAnnotationDbContext("Data Source=:memory:");
        return new TranscriptAnnotationDatabase(ctx);
    }

    [Fact]
    public async Task Import_EnsemblStyleWithVersionedFasta_ImportsTranscript()
    {
        var db = CreateInMemoryDatabase(out var ctx);
        await using var _ = ctx;
        await db.Initialize(TestContext.Current.CancellationToken);

        var result = await db.Import(
            new EnsemblTranscriptDatabaseImporter(),
            new TranscriptImportRequest(
                AnnotationPath: _tmpGtf,
                SequencePath: _tmpFa,
                Assembly: "GRCh38",
                SourceVersion: "test"),
            TestContext.Current.CancellationToken);

        Assert.Equal(1, result.TranscriptCount);

        var stored = await db.GetTranscript("ENST00000511072", TestContext.Current.CancellationToken);
        Assert.NotNull(stored);
        Assert.Equal("PRDM16", stored!.GeneName);
        Assert.Equal("ENSG00000142611", stored.GeneId);
        Assert.True(stored.IsCanonical);   // tag "Ensembl_canonical" must have been detected
        Assert.Equal(2, stored.Exons.Count);
        Assert.Single(stored.Introns);
    }

    [Fact]
    public async Task Import_GencodeStyleWithVersionedFastaAndVersionedGtfId_ImportsTranscript()
    {
        var gencodeGtf = Path.Combine(Path.GetTempPath(), $"gencode_test_{Guid.NewGuid():N}.gtf");
        var gencodeFa  = Path.Combine(Path.GetTempPath(), $"gencode_test_{Guid.NewGuid():N}.fa");

        try
        {
            // GENCODE GTF: versioned transcript_id
            File.WriteAllText(gencodeGtf, string.Join("\n",
                "##description: GENCODE",
                "chr1\tHAVANA\ttranscript\t11121\t14413\t.\t+\t.\t" +
                    "gene_id \"ENSG00000290825.2\"; transcript_id \"ENST00000832824.1\"; " +
                    "gene_name \"DDX11L16\"; tag \"basic\";",
                "chr1\tHAVANA\texon\t11121\t11211\t.\t+\t.\t" +
                    "gene_id \"ENSG00000290825.2\"; transcript_id \"ENST00000832824.1\"; gene_name \"DDX11L16\";",
                "chr1\tHAVANA\texon\t12010\t12227\t.\t+\t.\t" +
                    "gene_id \"ENSG00000290825.2\"; transcript_id \"ENST00000832824.1\"; gene_name \"DDX11L16\";",
                ""));

            // GENCODE FASTA: versioned pipe-separated header
            File.WriteAllText(gencodeFa, string.Join("\n",
                ">ENST00000832824.1|ENSG00000290825.2|-|-|DDX11L16-260|DDX11L16|1379|lncRNA|",
                "GCATGCATGCATGCATGCATGCATGCATGCAT",
                ""));

            var db = CreateInMemoryDatabase(out var ctx);
            await using var _ = ctx;
            await db.Initialize(TestContext.Current.CancellationToken);

            var result = await db.Import(
                new GencodeTranscriptDatabaseImporter(),
                new TranscriptImportRequest(
                    AnnotationPath: gencodeGtf,
                    SequencePath: gencodeFa,
                    Assembly: "GRCh38",
                    SourceVersion: "v48"),
                TestContext.Current.CancellationToken);

            Assert.Equal(1, result.TranscriptCount);

            // The stored key is the version-stripped ID
            var stored = await db.GetTranscript("ENST00000832824", TestContext.Current.CancellationToken);
            Assert.NotNull(stored);
            Assert.Equal("DDX11L16", stored!.GeneName);
        }
        finally
        {
            foreach (var f in new[] { gencodeGtf, gencodeFa })
            {
                try { File.Delete(f); } catch { /* ignore */ }
            }
        }
    }
}





