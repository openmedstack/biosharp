using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OpenMedStack.BioSharp.Calculations.Alignment;
using OpenMedStack.BioSharp.Calculations.DeBruijn;
using OpenMedStack.BioSharp.Model.Alignment;
using Xunit;

namespace OpenMedStack.BioSharp.Calculations.Tests;

/// <summary>
/// Tests for IO-8: MAF (Mutation Annotation Format) writer.
/// </summary>
public class MafWriterTests
{
    private static LocalVariantResult MakeVariant(string chrom, int pos, string @ref, string alt, int qual = 30,
        int depth = 20)
        => new()
        {
            Chromosome = chrom,
            Position = pos,
            Reference = @ref,
            Alternate = alt,
            QuantitativeQuality = qual,
            Depth = depth
        };

    private static SomaticVariant MakeSomatic(int pos, string @ref, string alt,
        int tumorCoverage = 20, int normalCoverage = 20)
        => new()
        {
            Position = pos,
            Reference = @ref,
            Alternate = alt,
            TumorCoverage = tumorCoverage,
            NormalCoverage = normalCoverage
        };

    [Fact]
    public async Task HeaderLinePresent()
    {
        var ms = new MemoryStream();
        await MafWriter.Write(ms, [MakeVariant("chr1", 100, "A", "T")], "TUMOR-01");
        var text = Encoding.UTF8.GetString(ms.ToArray());
        Assert.Contains("Hugo_Symbol", text);
        Assert.Contains("Chromosome", text);
    }

    [Fact]
    public async Task GdcCommentLinePresent()
    {
        var ms = new MemoryStream();
        await MafWriter.Write(ms, [MakeVariant("chr1", 100, "A", "T")], "TUMOR-01");
        var text = Encoding.UTF8.GetString(ms.ToArray());
        Assert.True(text.StartsWith("# GDC"), $"Expected GDC comment at start, got: {text[..Math.Min(30, text.Length)]}");
    }

    [Fact]
    public async Task AllRequiredColumnsPresent()
    {
        var ms = new MemoryStream();
        await MafWriter.Write(ms, [MakeVariant("chr1", 100, "A", "T")], "TUMOR-01");
        var text = Encoding.UTF8.GetString(ms.ToArray());

        var requiredCols = new[]
        {
            "Hugo_Symbol", "Chromosome", "Start_Position", "End_Position",
            "Variant_Classification", "Variant_Type", "Reference_Allele",
            "Tumor_Seq_Allele2", "Tumor_Sample_Barcode",
            "t_depth", "t_alt_count"
        };

        foreach (var col in requiredCols)
        {
            Assert.Contains(col, text);
        }
    }

    [Fact]
    public async Task SnpClassifiedCorrectly()
    {
        var ms = new MemoryStream();
        await MafWriter.Write(ms, [MakeVariant("chr1", 100, "A", "T")], "TUMOR-01");
        var text = Encoding.UTF8.GetString(ms.ToArray());
        var dataLines = text.Split('\n').Where(l => !l.StartsWith('#') && !l.StartsWith("Hugo") && !string.IsNullOrWhiteSpace(l)).ToList();
        Assert.NotEmpty(dataLines);
        Assert.Contains("SNP", dataLines[0]);
    }

    [Fact]
    public async Task DeletionClassifiedCorrectly()
    {
        var ms = new MemoryStream();
        await MafWriter.Write(ms, [MakeVariant("chr1", 200, "ACG", "A")], "TUMOR-01");
        var text = Encoding.UTF8.GetString(ms.ToArray());
        var dataLines = text.Split('\n').Where(l => !l.StartsWith('#') && !l.StartsWith("Hugo") && !string.IsNullOrWhiteSpace(l)).ToList();
        Assert.NotEmpty(dataLines);
        Assert.Contains("DEL", dataLines[0]);
    }

    [Fact]
    public async Task InsertionClassifiedCorrectly()
    {
        var ms = new MemoryStream();
        await MafWriter.Write(ms, [MakeVariant("chr1", 300, "A", "ACGT")], "TUMOR-01");
        var text = Encoding.UTF8.GetString(ms.ToArray());
        var dataLines = text.Split('\n').Where(l => !l.StartsWith('#') && !l.StartsWith("Hugo") && !string.IsNullOrWhiteSpace(l)).ToList();
        Assert.NotEmpty(dataLines);
        Assert.Contains("INS", dataLines[0]);
    }

    [Fact]
    public async Task MnpClassifiedCorrectly()
    {
        var ms = new MemoryStream();
        await MafWriter.Write(ms, [MakeVariant("chr1", 400, "ACG", "TTT")], "TUMOR-01");
        var text = Encoding.UTF8.GetString(ms.ToArray());
        var dataLines = text.Split('\n').Where(l => !l.StartsWith('#') && !l.StartsWith("Hugo") && !string.IsNullOrWhiteSpace(l)).ToList();
        Assert.NotEmpty(dataLines);
        // GDC MAF uses DNP for multi-nucleotide variants of the same length
        Assert.Contains("DNP", dataLines[0]);
    }

    [Fact]
    public async Task TumorSampleBarcodeWritten()
    {
        var ms = new MemoryStream();
        await MafWriter.Write(ms, [MakeVariant("chr1", 100, "A", "T")], "MY-TUMOR-SAMPLE");
        var text = Encoding.UTF8.GetString(ms.ToArray());
        Assert.Contains("MY-TUMOR-SAMPLE", text);
    }

    [Fact]
    public async Task SomaticVariantTumorDepthWritten()
    {
        var somatic = MakeSomatic(5000, "C", "G", tumorCoverage: 42, normalCoverage: 10);
        var ms = new MemoryStream();
        await MafWriter.Write(ms, [somatic], "chr7", "TUMOR-01");
        var text = Encoding.UTF8.GetString(ms.ToArray());

        // t_depth = tumor + normal coverage = 52
        var dataLines = text.Split('\n').Where(l => !l.StartsWith('#') && !l.StartsWith("Hugo") && !string.IsNullOrWhiteSpace(l)).ToList();
        Assert.NotEmpty(dataLines);
        // t_alt_count should contain TumorCoverage (42)
        Assert.Contains("42", dataLines[0]);
    }

    [Fact]
    public async Task MultipleVariantsAllWritten()
    {
        var variants = Enumerable.Range(1, 5)
            .Select(i => MakeVariant("chr1", i * 100, "G", "A"))
            .ToList();

        var ms = new MemoryStream();
        await MafWriter.Write(ms, variants, "TUMOR-01");
        var text = Encoding.UTF8.GetString(ms.ToArray());
        var dataLines = text.Split('\n').Where(l => !l.StartsWith('#') && !l.StartsWith("Hugo") && !string.IsNullOrWhiteSpace(l)).ToList();
        Assert.Equal(5, dataLines.Count);
    }

    [Fact]
    public void ClassifyVariantSnp()
    {
        var (variantClass, variantType) = MafWriter.ClassifyVariant("A", "T");
        Assert.Equal("SNP", variantType);
        Assert.NotEmpty(variantClass);
    }

    [Fact]
    public void ClassifyVariantInsertion()
    {
        var (_, variantType) = MafWriter.ClassifyVariant("A", "ATCG");
        Assert.Equal("INS", variantType);
    }

    [Fact]
    public void ClassifyVariantDeletion()
    {
        var (_, variantType) = MafWriter.ClassifyVariant("ATCG", "A");
        Assert.Equal("DEL", variantType);
    }

    [Fact]
    public void ClassifyVariantMnp()
    {
        var (_, variantType) = MafWriter.ClassifyVariant("ACG", "TTT");
        // multi-nucleotide variant returns DNP in GDC MAF spec
        Assert.Equal("DNP", variantType);
    }

    [Fact]
    public void GetPositionsSnp()
    {
        var (start, end) = MafWriter.GetPositions(100, "A", "T");
        Assert.Equal(100, start);
        Assert.Equal(100, end);
    }

    [Fact]
    public void GetPositionsDeletion()
    {
        var (start, end) = MafWriter.GetPositions(100, "ACG", "A");
        Assert.True(end > start);
    }
}