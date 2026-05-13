using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using OpenMedStack.BioSharp.Model;
using OpenMedStack.BioSharp.Model.Vcf;
using Xunit;

namespace OpenMedStack.BioSharp.AnnotationDb.Tests;

public sealed class DatabaseVariantAnnotationEngineSpecTests : IAsyncDisposable
{
    private readonly List<string> _inputPaths = [];

    public ValueTask DisposeAsync()
    {
        foreach (var inputPath in _inputPaths)
        {
            try
            {
                File.Delete(inputPath);
            }
            catch
            {
                // ignore test cleanup failures
            }
        }

        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task InitializeAsync_CreatesStoreForConfiguredProvider()
    {
        await using var scope = await CreateDatabaseScope();

        await scope.Database.Initialize(TestContext.Current.CancellationToken);

        scope.Context.TranscriptSources.Add(new TranscriptSourceEntity
        {
            SourceId = "source",
            SourceName = "test",
            Assembly = "GRCh38",
            SourceVersion = "v1",
            AnnotationPath = "annotations",
            SequencePath = "sequences",
            ImportedAtUtc = DateTimeOffset.UtcNow
        });
        await scope.Context.SaveChangesAsync(TestContext.Current.CancellationToken);

        Assert.Single(scope.Context.TranscriptSources);
        Assert.NotNull(scope.Context.Model.FindEntityType(typeof(TranscriptSourceEntity)));
        Assert.NotNull(scope.Context.Model.FindEntityType(typeof(TranscriptEntity)));
        Assert.NotNull(scope.Context.Model.FindEntityType(typeof(TranscriptExonEntity)));
        Assert.NotNull(scope.Context.Model.FindEntityType(typeof(TranscriptIntronEntity)));
    }

    [Fact]
    public async Task EnsemblImporter_StoresTranscriptSequenceAndContext()
    {
        var annotationPath = await CreateInputFile("ensembl.gtf",
            "chr1\ttest\tgene\t1\t9\t.\t+\t.\tgene_id \"GENE1\"; gene_name \"GeneOne\";",
            "chr1\ttest\ttranscript\t1\t9\t.\t+\t.\tgene_id \"GENE1\"; transcript_id \"ENST000001\"; gene_name \"GeneOne\"; tag \"Ensembl_canonical\";",
            "chr1\ttest\texon\t1\t9\t.\t+\t.\tgene_id \"GENE1\"; transcript_id \"ENST000001\";",
            "chr1\ttest\tCDS\t1\t9\t.\t+\t0\tgene_id \"GENE1\"; transcript_id \"ENST000001\";");
        var sequencePath = await CreateInputFile("ensembl.fa",
            ">ENST000001",
            "ATGGCCATT");

        await using var scope = await CreateDatabaseScope();
        await scope.Database.Initialize(TestContext.Current.CancellationToken);

        var result = await scope.Database.Import(
            new EnsemblTranscriptDatabaseImporter(),
            new TranscriptImportRequest(annotationPath, sequencePath, "GRCh38", "110"),
            TestContext.Current.CancellationToken);

        var stored = await scope.Database.GetTranscript("ENST000001", TestContext.Current.CancellationToken);

        Assert.Equal("Ensembl", result.SourceName);
        Assert.NotNull(stored);
        Assert.Equal("ENST000001", stored!.TranscriptId);
        Assert.Equal("GENE1", stored.GeneId);
        Assert.Equal("GeneOne", stored.GeneName);
        Assert.Equal("chr1", stored.Chromosome);
        Assert.True(stored.IsCanonical);
        Assert.Equal("ATGGCCATT", stored.Sequence);
        Assert.Equal(1, stored.Context.CdsStart);
        Assert.Equal(9, stored.Context.CdsEnd);
        Assert.Single(stored.Exons);
        Assert.Empty(stored.Introns);
    }

    [Fact]
    public async Task RefSeqImporter_StoresTranscriptMetadataFromGff3()
    {
        var annotationPath = await CreateInputFile("refseq.gff3",
            "##gff-version 3",
            "chr2\tRefSeq\tgene\t1\t9\t.\t+\t.\tID=gene-GENE2;Name=GeneTwo;Dbxref=GeneID:GENE2",
            "chr2\tRefSeq\tmRNA\t1\t9\t.\t+\t.\tID=rna-NM_000002;Parent=gene-GENE2;Dbxref=GeneID:GENE2;gene=GeneTwo",
            "chr2\tRefSeq\texon\t1\t9\t.\t+\t.\tParent=rna-NM_000002",
            "chr2\tRefSeq\tCDS\t1\t9\t.\t+\t0\tParent=rna-NM_000002");
        var sequencePath = await CreateInputFile("refseq.fa",
            ">NM_000002",
            "ATGGGACGT");

        await using var scope = await CreateDatabaseScope();
        await scope.Database.Initialize(TestContext.Current.CancellationToken);

        await scope.Database.Import(
            new RefSeqTranscriptDatabaseImporter(),
            new TranscriptImportRequest(annotationPath, sequencePath, "GRCh38", "RefSeq-2026-05"),
            TestContext.Current.CancellationToken);

        var stored = await scope.Database.GetTranscript("NM_000002", TestContext.Current.CancellationToken);

        Assert.NotNull(stored);
        Assert.Equal("GENE2", stored!.GeneId);
        Assert.Equal("GeneTwo", stored.GeneName);
        Assert.Equal("chr2", stored.Chromosome);
        Assert.Equal("ATGGGACGT", stored.Sequence);
        Assert.Equal(1, stored.Context.CdsStart);
        Assert.Equal(9, stored.Context.CdsEnd);
    }

    [Fact]
    public async Task AnnotateVariantAsync_AnnotatesStoredTranscript()
    {
        await using var scope = await BuildSingleTranscriptDatabase();
        var engine = new DatabaseVariantAnnotationEngine(scope.Database);

        var annotations = await engine.AnnotateVariant(
            new VcfVariant
            {
                Chromosome = "chr1",
                Position = 4,
                Reference = "G",
                Alternate = "A",
                ErrorProbabilities = [30],
                FailedFilter = []
            },
            "ENST000001",
            cancellationToken: TestContext.Current.CancellationToken);

        var annotation = Assert.Single(annotations);
        Assert.Equal("ENST000001", annotation.AffectedGene);
        Assert.Equal(VariantConsequence.Missense, annotation.Consequence);
        Assert.Equal("c.4G>A", annotation.HgvsCoding);
        Assert.Equal("p.A2T", annotation.HgvsProtein);
    }

    [Fact]
    public async Task AnnotateVariantAsync_UsesStoredIntronBoundariesForNonCodingAnnotation()
    {
        var annotationPath = await CreateInputFile("intronic.gtf",
            "chr5\ttest\tgene\t1\t300\t.\t+\t.\tgene_id \"GENE5\"; gene_name \"GeneFive\";",
            "chr5\ttest\ttranscript\t1\t300\t.\t+\t.\tgene_id \"GENE5\"; transcript_id \"ENST_INTRON\"; gene_name \"GeneFive\"; tag \"Ensembl_canonical\";",
            "chr5\ttest\texon\t1\t100\t.\t+\t.\tgene_id \"GENE5\"; transcript_id \"ENST_INTRON\";",
            "chr5\ttest\tCDS\t1\t98\t.\t+\t0\tgene_id \"GENE5\"; transcript_id \"ENST_INTRON\";",
            "chr5\ttest\texon\t201\t300\t.\t+\t.\tgene_id \"GENE5\"; transcript_id \"ENST_INTRON\";",
            "chr5\ttest\tCDS\t201\t298\t.\t+\t0\tgene_id \"GENE5\"; transcript_id \"ENST_INTRON\";");
        var sequencePath = await CreateInputFile("intronic.fa",
            ">ENST_INTRON",
            new string('A', 300));

        await using var scope = await CreateDatabaseScope();
        await scope.Database.Initialize(TestContext.Current.CancellationToken);
        await scope.Database.Import(
            new EnsemblTranscriptDatabaseImporter(),
            new TranscriptImportRequest(annotationPath, sequencePath, "GRCh38", "110"),
            TestContext.Current.CancellationToken);

        var engine = new DatabaseVariantAnnotationEngine(scope.Database);
        var annotations = await engine.AnnotateVariant(
            new VcfVariant
            {
                Chromosome = "chr5",
                Position = 150,
                Reference = "A",
                Alternate = "G",
                ErrorProbabilities = [30],
                FailedFilter = []
            },
            "ENST_INTRON",
            cancellationToken: TestContext.Current.CancellationToken);

        var annotation = Assert.Single(annotations);
        Assert.Equal(VariantConsequence.Intronic, annotation.Consequence);
        Assert.Equal("p.?", annotation.HgvsProtein);
    }

    [Fact]
    public async Task AnnotateVariantAsync_WithoutTranscriptId_OnlyUsesMatchingStoredTranscript()
    {
        var annotationPath = await CreateInputFile("multi.gtf",
            "chr7\ttest\tgene\t1\t9\t.\t+\t.\tgene_id \"GENE7\"; gene_name \"GeneSeven\";",
            "chr7\ttest\ttranscript\t1\t9\t.\t+\t.\tgene_id \"GENE7\"; transcript_id \"ENST_MATCH\"; gene_name \"GeneSeven\"; tag \"Ensembl_canonical\";",
            "chr7\ttest\texon\t1\t9\t.\t+\t.\tgene_id \"GENE7\"; transcript_id \"ENST_MATCH\";",
            "chr7\ttest\tCDS\t1\t9\t.\t+\t0\tgene_id \"GENE7\"; transcript_id \"ENST_MATCH\";",
            "chr8\ttest\tgene\t1\t9\t.\t+\t.\tgene_id \"GENE8\"; gene_name \"GeneEight\";",
            "chr8\ttest\ttranscript\t1\t9\t.\t+\t.\tgene_id \"GENE8\"; transcript_id \"ENST_OTHER\"; gene_name \"GeneEight\";",
            "chr8\ttest\texon\t1\t9\t.\t+\t.\tgene_id \"GENE8\"; transcript_id \"ENST_OTHER\";",
            "chr8\ttest\tCDS\t1\t9\t.\t+\t0\tgene_id \"GENE8\"; transcript_id \"ENST_OTHER\";");
        var sequencePath = await CreateInputFile("multi.fa",
            ">ENST_MATCH",
            "ATGGCCATT",
            ">ENST_OTHER",
            "ATGGCCATT");

        await using var scope = await CreateDatabaseScope();
        await scope.Database.Initialize(TestContext.Current.CancellationToken);
        await scope.Database.Import(
            new EnsemblTranscriptDatabaseImporter(),
            new TranscriptImportRequest(annotationPath, sequencePath, "GRCh38", "110"),
            TestContext.Current.CancellationToken);

        var engine = new DatabaseVariantAnnotationEngine(scope.Database);
        var annotations = await engine.AnnotateVariant(
            new VcfVariant
            {
                Chromosome = "chr7",
                Position = 4,
                Reference = "G",
                Alternate = "A",
                ErrorProbabilities = [30],
                FailedFilter = []
            },
            cancellationToken: TestContext.Current.CancellationToken);

        var annotation = Assert.Single(annotations);
        Assert.Equal("ENST_MATCH", annotation.AffectedGene);
    }

    private async Task<TestDatabaseScope> BuildSingleTranscriptDatabase()
    {
        var annotationPath = await CreateInputFile("single.gtf",
            "chr1\ttest\tgene\t1\t9\t.\t+\t.\tgene_id \"GENE1\"; gene_name \"GeneOne\";",
            "chr1\ttest\ttranscript\t1\t9\t.\t+\t.\tgene_id \"GENE1\"; transcript_id \"ENST000001\"; gene_name \"GeneOne\"; tag \"Ensembl_canonical\";",
            "chr1\ttest\texon\t1\t9\t.\t+\t.\tgene_id \"GENE1\"; transcript_id \"ENST000001\";",
            "chr1\ttest\tCDS\t1\t9\t.\t+\t0\tgene_id \"GENE1\"; transcript_id \"ENST000001\";");
        var sequencePath = await CreateInputFile("single.fa",
            ">ENST000001",
            "ATGGCCATT");

        var scope = await CreateDatabaseScope();
        await scope.Database.Initialize(TestContext.Current.CancellationToken);
        await scope.Database.Import(
            new EnsemblTranscriptDatabaseImporter(),
            new TranscriptImportRequest(annotationPath, sequencePath, "GRCh38", "110"),
            TestContext.Current.CancellationToken);

        return scope;
    }

    private async Task<string> CreateInputFile(string fileName, params string[] lines)
    {
        var path = Path.Combine(Path.GetTempPath(), $"annotationdb_{Guid.NewGuid():N}_{fileName}");
        _inputPaths.Add(path);
        await File.WriteAllTextAsync(path, string.Join("\n", lines), TestContext.Current.CancellationToken);
        return path;
    }

    private static async Task<TestDatabaseScope> CreateDatabaseScope()
    {
        var databaseName = $"annotationdb-tests-{Guid.NewGuid():N}";
        var options = new DbContextOptionsBuilder<TranscriptAnnotationDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;

        var context = new TranscriptAnnotationDbContext(options);
        var database = new TranscriptAnnotationDatabase(context);
        return await Task.FromResult(new TestDatabaseScope(context, database));
    }

    private sealed class TestDatabaseScope : IAsyncDisposable
    {
        public TestDatabaseScope(
            TranscriptAnnotationDbContext context,
            TranscriptAnnotationDatabase database)
        {
            Context = context;
            Database = database;
        }

        public TranscriptAnnotationDbContext Context { get; }

        public TranscriptAnnotationDatabase Database { get; }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
        }
    }
}
