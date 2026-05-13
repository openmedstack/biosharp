namespace OpenMedStack.Preator.Tests;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using OpenMedStack.BioSharp.AnnotationDb;
using Xunit;

public sealed class AnnotateCommandTests : IAsyncDisposable
{
    private readonly List<string> _pathsToDelete = [];

    public async ValueTask DisposeAsync()
    {
        foreach (var path in _pathsToDelete.OrderByDescending(path => path.Length))
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, recursive: true);
                }
                else if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
                // ignore cleanup failures in test temp paths
            }
        }

        await ValueTask.CompletedTask;
    }

    [Fact]
    public async Task RunAsync_WritesAnnotatedOutputs()
    {
        var databasePath = Track(Path.Combine(Path.GetTempPath(), $"preator_{Guid.NewGuid():N}.sqlite"));
        var inputVcfPath = await CreateInputFile("input.vcf",
            "##fileformat=VCFv4.2",
            "#CHROM\tPOS\tID\tREF\tALT\tQUAL\tFILTER\tINFO",
            "chr1\t4\t.\tG\tA\t30\tPASS\t.");
        var outputDirectory = Track(Path.Combine(Path.GetTempPath(), $"preator_output_{Guid.NewGuid():N}"));

        await BuildTranscriptDatabase(databasePath);

        var exitCode = await AnnotateCommand.Run(
            new AnnotateOptions(
                VcfPath: inputVcfPath,
                TranscriptDatabasePath: databasePath,
                TranscriptId: null,
                MinQuality: 0,
                OutputDirectory: outputDirectory,
                OutputPrefix: "annotated"),
            TestContext.Current.CancellationToken);

        Assert.Equal(0, exitCode);

        var tsvPath = Path.Combine(outputDirectory, "annotated.annotations.tsv");
        var jsonPath = Path.Combine(outputDirectory, "annotated.clinical-report.json");
        var htmlPath = Path.Combine(outputDirectory, "annotated.clinical-report.html");
        var summaryPath = Path.Combine(outputDirectory, "annotated.summary.txt");

        Assert.True(File.Exists(tsvPath));
        Assert.True(File.Exists(jsonPath));
        Assert.True(File.Exists(htmlPath));
        Assert.True(File.Exists(summaryPath));

        var tsv = await File.ReadAllTextAsync(tsvPath, TestContext.Current.CancellationToken);
        Assert.Contains("Transcript", tsv);
        Assert.Contains("ENST000001", tsv);
        Assert.Contains("Missense", tsv);
        Assert.Contains("c.4G>A", tsv);
        Assert.Contains("p.A2T", tsv);

        var summary = await File.ReadAllTextAsync(summaryPath, TestContext.Current.CancellationToken);
        Assert.Contains("AnnotatedVariants\t1", summary);
        Assert.Contains("AnnotationRows\t1", summary);
    }

    [Fact]
    public async Task RunAsync_MissingDatabaseFile_ThrowsFileNotFoundException()
    {
        var inputVcfPath = await CreateInputFile("input.vcf",
            "##fileformat=VCFv4.2",
            "#CHROM\tPOS\tID\tREF\tALT\tQUAL\tFILTER\tINFO",
            "chr1\t4\t.\tG\tA\t30\tPASS\t.");
        var missingDatabasePath = Track(Path.Combine(Path.GetTempPath(), $"missing_{Guid.NewGuid():N}.sqlite"));
        var outputDirectory = Track(Path.Combine(Path.GetTempPath(), $"preator_output_{Guid.NewGuid():N}"));

        var exception = await Assert.ThrowsAsync<FileNotFoundException>(async () =>
            await AnnotateCommand.Run(
                new AnnotateOptions(
                    VcfPath: inputVcfPath,
                    TranscriptDatabasePath: missingDatabasePath,
                    TranscriptId: null,
                    MinQuality: 0,
                    OutputDirectory: outputDirectory,
                    OutputPrefix: "annotated"),
                TestContext.Current.CancellationToken));

        Assert.Equal(missingDatabasePath, exception.FileName);
    }

    private async Task BuildTranscriptDatabase(string databasePath)
    {
        var annotationPath = await CreateInputFile("single.gtf",
            "chr1\ttest\tgene\t1\t9\t.\t+\t.\tgene_id \"GENE1\"; gene_name \"GeneOne\";",
            "chr1\ttest\ttranscript\t1\t9\t.\t+\t.\tgene_id \"GENE1\"; transcript_id \"ENST000001\"; gene_name \"GeneOne\"; tag \"Ensembl_canonical\";",
            "chr1\ttest\texon\t1\t9\t.\t+\t.\tgene_id \"GENE1\"; transcript_id \"ENST000001\";",
            "chr1\ttest\tCDS\t1\t9\t.\t+\t0\tgene_id \"GENE1\"; transcript_id \"ENST000001\";");
        var sequencePath = await CreateInputFile("single.fa",
            ">ENST000001",
            "ATGGCCATT");

        var dbOptions = new DbContextOptionsBuilder<TranscriptAnnotationDbContext>()
            .UseSqlite($"Data Source={databasePath}")
            .Options;

        await using var context = new TranscriptAnnotationDbContext(dbOptions);
        var database = new TranscriptAnnotationDatabase(context);
        await database.Initialize(TestContext.Current.CancellationToken);
        await database.Import(
            new EnsemblTranscriptDatabaseImporter(),
            new TranscriptImportRequest(annotationPath, sequencePath, "GRCh38", "110"),
            TestContext.Current.CancellationToken);
    }

    private async Task<string> CreateInputFile(string fileName, params string[] lines)
    {
        var path = Track(Path.Combine(Path.GetTempPath(), $"preator_{Guid.NewGuid():N}_{fileName}"));
        await File.WriteAllLinesAsync(path, lines, TestContext.Current.CancellationToken);
        return path;
    }

    private string Track(string path)
    {
        _pathsToDelete.Add(path);
        return path;
    }
}
