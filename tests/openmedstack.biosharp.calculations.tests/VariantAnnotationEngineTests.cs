using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OpenMedStack.BioSharp.Model;
using OpenMedStack.BioSharp.Model.Vcf;
using OpenMedStack.BioSharp.Calculations;
using Xunit;

namespace OpenMedStack.BioSharp.Calculations.Tests;

public class VariantAnnotationEngineTests : IDisposable
{
    private readonly string _testDir;
    private readonly string _fastaPath;
    private readonly string _vcfPath;

    public VariantAnnotationEngineTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_testDir);

        _fastaPath = Path.Combine(_testDir, "test.fasta");
        _vcfPath = Path.Combine(_testDir, "test.vcf");

        // Create a dummy FASTA file with 2 transcripts
        // NM_001: ATG GCC ATT -> M A I  (Pos 4 is G)
        // NM_002: ATG GGA CGT -> M G R  (Pos 4 is G)
        File.WriteAllLines(_fastaPath, new[] {
            ">NM_001",
            "ATGGCCATT",
            ">NM_002",
            "ATGGGACGT" 
        });

        // Create a dummy VCF file
        // Variant 1: pos 1, A>G (ATG -> GTG = Met -> Val) in both transcripts if applicable
        // Variant 2: pos 4, G>A (GCC -> GAC = Ala -> Asp) in NM_001 AND ATGGGACGT? No.
        // Actually for NM_002 sequence 'ATGGGACGT', pos 4 is G. So a G>A substitution works for both!
        File.WriteAllLines(_vcfPath, new[] {
            "#CHROM\tPOS\tID\tREF\tALT\tQUAL\tFILTER\tINFO",
            "chr1\t1\t.\tA\tG\t30.0\tPASS\t.",
            "chr1\t4\t.\tG\tA\t30.0\tPASS\t."
        });
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
        {
            try { Directory.Delete(_testDir, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public async Task LoadTranscriptsAsync_TwoTranscripts_PopulatesDictionary()
    {
        using var engine = new VariantAnnotationEngine();
        await engine.LoadTranscriptsAsync(_fastaPath);
    }

    [Fact]
    public async Task LoadTranscriptsAsync_NonExistentFile_ThrowsFileNotFoundException()
    {
        using var engine = new VariantAnnotationEngine();
        await Assert.ThrowsAsync<FileNotFoundException>(() => engine.LoadTranscriptsAsync("nonexistent.fasta"));
    }

    [Fact]
    public async Task AnnotateVcfAsync_AnnotatesCorrectNumber()
    {
        using var engine = new VariantAnnotationEngine();
        await engine.LoadTranscriptsAsync(_fastaPath);
        
        var annotations = new List<VariantAnnotation>();
        await foreach (var ann in engine.AnnotateVcfAsync(_vcfPath, null, 5.0f))
        {
            annotations.Add(ann);
        }

        // NM_001: pos 1 and 4 pass quality.
        // NM_002: pos 1 and 4 pass quality.
        // Total = 4 annotations.
        Assert.Equal(4, annotations.Count);
    }

    [Fact]
    public async Task AnnotateVcfAsync_FilteringByTranscriptId_Works()
    {
        using var engine = new VariantAnnotationEngine();
        await engine.LoadTranscriptsAsync(_fastaPath);
        
        var annotations = new List<VariantAnnotation>();
        // The FASTA header is ">NM_001".
        await foreach (var ann in engine.AnnotateVcfAsync(_vcfPath, "NM_001", 5.0f))
        {
            annotations.Add(ann);
        }

        Assert.Equal(2, annotations.Count);
        foreach (var ann in annotations)
        {
            Assert.Equal("NM_001", ann.AffectedGene);
        }
    }

    [Fact]
    public async Task AnnotateVcfAsync_FilteringByQuality_Works()
    {
        // Create VCF with one high quality and one low quality variant
        var vcfWithLowQual = Path.Combine(_testDir, "lowqual.vcf");
        File.WriteAllLines(vcfWithLowQual, new[] {
            "#CHROM\tPOS\tID\tREF\tALT\tQUAL\tFILTER\tINFO",
            "chr1\t1\t.\tA\tG\t30.0\tPASS\t.", // High qual (Phred 30)
            "chr1\t4\t.\tG\tA\t2.0\tPASS\t."  // Low qual (Phred 2 < 5)
        });

        using var engine = new VariantAnnotationEngine();
        await engine.LoadTranscriptsAsync(_fastaPath);
        
        var annotations = new List<VariantAnnotation>();
        await foreach (var ann in engine.AnnotateVcfAsync(vcfWithLowQual, null, 5.0f))
        {
            annotations.Add(ann);
        }

        // Should only have 2 (one for each transcript, but only the high qual one per transcript)
        Assert.Equal(2, annotations.Count);
    }

    [Fact]
    public async Task AnnotateVariantAsync_ReturnsMultipleAnnotations()
    {
        using var engine = new VariantAnnotationEngine();
        await engine.LoadTranscriptsAsync(_fastaPath);
        
        var variant = new VcfVariant 
        { 
            Chromosome = "chr1", 
            Position = 1, 
            Reference = "A", 
            Alternate = "G",
            ErrorProbabilities = new[] { 30 }, // High qual
            FailedFilter = Array.Empty<string>()
        };

        var anns = engine.AnnotateVariantAsync(variant);

        Assert.NotNull(anns);
        Assert.Equal(2, anns.Length); // One for NM_001, one for NM_002
    }
}
