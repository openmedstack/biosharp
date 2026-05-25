using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using OpenMedStack.BioSharp.Calculations.Alignment;
using OpenMedStack.BioSharp.Model;
using OpenMedStack.BioSharp.Model.Alignment;
using Xunit;

namespace OpenMedStack.BioSharp.Calculations.Tests;

public class VcfWriterTests
{
    private static LocalVariantResult CreateVariant(
        string chrom,
        int pos,
        string refB,
        string altB,
        double qual,
        int depth,
        SvType? svType = null,
        bool isSv = false)
    {
        var v = new LocalVariantResult
        {
            Chromosome = chrom,
            Position = pos,
            Reference = refB,
            Alternate = altB,
            QuantitativeQuality = (int)qual,
            Depth = depth,
            SvType = svType,
            IsStructuralVariant = isSv
        };
        return v;
    }

    [Fact]
    public async Task WriteAsync_IncludesHeader()
    {
        var variant = CreateVariant("chr1", 100, "A", "T", 50, 10);
        using var ms = new MemoryStream();
        await VcfWriter.Write(ms, [variant], "chr1");
        var text = System.Text.Encoding.UTF8.GetString(ms.ToArray());
        Assert.Contains("##fileformat=VCFv4.2", text);
        Assert.Contains("#CHROM\tPOS\tID\tREF\tALT\tQUAL\tFILTER\tINFO", text);
    }

    [Fact]
    public async Task WriteAsync_IncludesVariantRecord()
    {
        var variant = CreateVariant("chr1", 100, "A", "T", 50, 10);
        using var ms = new MemoryStream();
        await VcfWriter.Write(ms, [variant], "chr1");
        var text = System.Text.Encoding.UTF8.GetString(ms.ToArray());
        Assert.Contains("chr1", text);
        Assert.Contains("100", text);
        Assert.Contains("A", text);
        Assert.Contains("T", text);
        Assert.Contains("50", text);
        // Check variant line (not header) contains PASS
        var variantLines = text.Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries)
            .Where(l => !l.StartsWith("#")).ToList();
        Assert.NotEmpty(variantLines);
        Assert.Contains("PASS", variantLines[0]);
    }

    [Fact]
    public async Task WriteAsync_LowQuality_ReturnsLowQualFilter()
    {
        var variant = CreateVariant("chr2", 50, "C", "G", 20, 1);
        using var ms = new MemoryStream();
        await VcfWriter.Write(ms, [variant], "chr2");
        var text = System.Text.Encoding.UTF8.GetString(ms.ToArray());
        // Check variant line (not header) contains LOW_QUAL
        var variantLines = text.Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries)
            .Where(l => !l.StartsWith("#")).ToList();
        Assert.NotEmpty(variantLines);
        Assert.Contains("LOW_QUAL", variantLines[0]);
    }

    [Fact]
    public async Task WriteAsync_WithContigLength_IncludesContigHeader()
    {
        var variant = CreateVariant("chr1", 100, "A", "T", 50, 10);
        using var ms = new MemoryStream();
        await VcfWriter.Write(ms, [variant], "chr1", 1000);
        var text = System.Text.Encoding.UTF8.GetString(ms.ToArray());
        Assert.Contains("##contig=", text);
        Assert.Contains("chr1", text);
    }

    [Fact]
    public async Task WriteAsync_StructuralVariant_IncludesSvFields()
    {
        var variant = CreateVariant("chr1", 500, "ACGT", "A", 30, 20, SvType.Deletion, true)
            .WithStructuralVariant(SvType.Deletion, 503, coverage: 20, altPathCount: 1);
        using var ms = new MemoryStream();
        await VcfWriter.Write(ms, [variant], "chr1");
        var text = System.Text.Encoding.UTF8.GetString(ms.ToArray());
        Assert.Contains("SVTYPE=DEL", text);
        Assert.Contains("END=503", text);
    }

    [Fact]
    public async Task WriteAsync_EmptyVariants_StillProducesHeader()
    {
        using var ms = new MemoryStream();
        await VcfWriter.Write(ms, [], "chr1");
        var text = System.Text.Encoding.UTF8.GetString(ms.ToArray());
        Assert.Contains("##fileformat=VCFv4.2", text);
        Assert.Contains("#CHROM\tPOS", text);
        // No variant records after header
        var nonHeaderLines = text.Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries)
            .Where(l => !l.StartsWith("#")).ToList();
        Assert.Empty(nonHeaderLines);
    }

    [Fact]
    public void WithStructuralVariant_ExtendsVariant()
    {
        var variant = new LocalVariantResult
        {
            Chromosome = "chr1",
            Position = 100,
            Reference = "A",
            Alternate = "ACGT",
            QuantitativeQuality = 50,
            Depth = 10
        };
        var extended = variant.WithStructuralVariant(SvType.Insertion, 104, coverage: 15, altPathCount: 2);
        Assert.True(extended.IsStructuralVariant);
        Assert.Equal(SvType.Insertion, extended.SvType);
        Assert.Equal(104, extended.EndPosition);
        Assert.NotNull(extended.AssemblyInfo);
        Assert.Equal(2, extended.AssemblyInfo!.AltPathCount);
    }
}
