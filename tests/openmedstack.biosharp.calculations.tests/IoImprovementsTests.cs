namespace OpenMedStack.BioSharp.Calculations.Tests;

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Alignment;
using DeBruijn;
using Io;
using Xunit;

/// <summary>
/// Tests for IO-7: BGZF-compressed VCF writing.
/// </summary>
public class BgzfVcfWriterTests
{
    private static LocalVariantResult MakeVariant(string chrom, int pos, string @ref, string alt, int qual = 30,
        int depth = 10)
        => new()
        {
            Chromosome = chrom,
            Position = pos,
            Reference = @ref,
            Alternate = alt,
            QuantitativeQuality = qual,
            Depth = depth
        };

    [Fact]
    public async Task CompressedOutputSmallerThanPlain()
    {
        var variants = Enumerable.Range(1, 100)
            .Select(i => MakeVariant("chr1", i * 1000, "A", "T", 60, 30))
            .ToList();

        var plainPath = Path.GetTempFileName() + ".vcf";
        var compressedPath = Path.GetTempFileName() + ".vcf.gz";

        try
        {
            await VcfWriter.WriteAsync(plainPath, variants, "chr1",
                compress: false, cancellationToken: TestContext.Current.CancellationToken);
            await VcfWriter.WriteAsync(compressedPath, variants, "chr1",
                compress: true, cancellationToken: TestContext.Current.CancellationToken);

            var plainSize = new FileInfo(plainPath).Length;
            var compressedSize = new FileInfo(compressedPath).Length;

            Assert.True(compressedSize < plainSize,
                $"Compressed ({compressedSize}) should be smaller than plain ({plainSize})");
        }
        finally
        {
            File.Delete(plainPath);
            File.Delete(compressedPath);
        }
    }

    [Fact]
    public async Task CompressedOutputIsBgzfValid()
    {
        var variants = new[] { MakeVariant("chr1", 100, "A", "T") };
        var path = Path.GetTempFileName() + ".vcf.gz";

        try
        {
            await VcfWriter.WriteAsync(path, variants, "chr1",
                compress: true, cancellationToken: TestContext.Current.CancellationToken);

            // A BGZF file starts with the GZIP magic bytes 1f 8b
            var header = new byte[2];
            await using var f = File.OpenRead(path);
            var read = await f.ReadAsync(header, TestContext.Current.CancellationToken);
            Assert.Equal(2, read);
            Assert.Equal(0x1f, header[0]);
            Assert.Equal(0x8b, header[1]);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task CompressedOutputIsReadableViaBgzfStream()
    {
        var variants = Enumerable.Range(1, 5)
            .Select(i => MakeVariant("chr2", i * 500, "G", "C", 50, 20))
            .ToList();

        var path = Path.GetTempFileName() + ".vcf.gz";

        try
        {
            await VcfWriter.WriteAsync(path, variants, "chr2",
                compress: true, cancellationToken: TestContext.Current.CancellationToken);

            await using var fileStream = File.OpenRead(path);
            await using var bgzf = new BgzfStream(fileStream, CompressionMode.Decompress);
            using var textReader = new StreamReader(bgzf, Encoding.UTF8, leaveOpen: true);

            var content = await textReader.ReadToEndAsync(TestContext.Current.CancellationToken);
            Assert.Contains("##fileformat=VCFv4.2", content);
            Assert.Contains("chr2", content);
            Assert.Contains("G", content);
            Assert.Contains("C", content);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task PlainAndCompressedVariantCountMatch()
    {
        var variants = Enumerable.Range(1, 50)
            .Select(i => MakeVariant("chr3", i * 200, "T", "A", 40, 15))
            .ToList();

        var plainPath = Path.GetTempFileName() + ".vcf";
        var compressedPath = Path.GetTempFileName() + ".vcf.gz";

        try
        {
            await VcfWriter.WriteAsync(plainPath, variants, "chr3",
                compress: false, cancellationToken: TestContext.Current.CancellationToken);
            await VcfWriter.WriteAsync(compressedPath, variants, "chr3",
                compress: true, cancellationToken: TestContext.Current.CancellationToken);

            // Count variant lines in plain VCF
            var plainLines = (await File.ReadAllLinesAsync(plainPath, TestContext.Current.CancellationToken))
                .Count(l => !l.StartsWith('#') && !string.IsNullOrWhiteSpace(l));

            // Count variant lines in BGZF VCF
            await using var fileStream = File.OpenRead(compressedPath);
            await using var bgzf = new BgzfStream(fileStream, CompressionMode.Decompress);
            using var reader = new StreamReader(bgzf, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
            var compressedContent = await reader.ReadToEndAsync(TestContext.Current.CancellationToken);
            var compressedLines = compressedContent.Split('\n')
                .Count(l => !l.StartsWith('#') && !string.IsNullOrWhiteSpace(l));

            Assert.Equal(plainLines, compressedLines);
        }
        finally
        {
            File.Delete(plainPath);
            File.Delete(compressedPath);
        }
    }
}

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
        await MafWriter.WriteAsync(ms, [MakeVariant("chr1", 100, "A", "T")], "TUMOR-01");
        var text = Encoding.UTF8.GetString(ms.ToArray());
        Assert.Contains("Hugo_Symbol", text);
        Assert.Contains("Chromosome", text);
    }

    [Fact]
    public async Task GdcCommentLinePresent()
    {
        var ms = new MemoryStream();
        await MafWriter.WriteAsync(ms, [MakeVariant("chr1", 100, "A", "T")], "TUMOR-01");
        var text = Encoding.UTF8.GetString(ms.ToArray());
        Assert.True(text.StartsWith("# GDC"), $"Expected GDC comment at start, got: {text[..Math.Min(30, text.Length)]}");
    }

    [Fact]
    public async Task AllRequiredColumnsPresent()
    {
        var ms = new MemoryStream();
        await MafWriter.WriteAsync(ms, [MakeVariant("chr1", 100, "A", "T")], "TUMOR-01");
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
        await MafWriter.WriteAsync(ms, [MakeVariant("chr1", 100, "A", "T")], "TUMOR-01");
        var text = Encoding.UTF8.GetString(ms.ToArray());
        var dataLines = text.Split('\n').Where(l => !l.StartsWith('#') && !l.StartsWith("Hugo") && !string.IsNullOrWhiteSpace(l)).ToList();
        Assert.NotEmpty(dataLines);
        Assert.Contains("SNP", dataLines[0]);
    }

    [Fact]
    public async Task DeletionClassifiedCorrectly()
    {
        var ms = new MemoryStream();
        await MafWriter.WriteAsync(ms, [MakeVariant("chr1", 200, "ACG", "A")], "TUMOR-01");
        var text = Encoding.UTF8.GetString(ms.ToArray());
        var dataLines = text.Split('\n').Where(l => !l.StartsWith('#') && !l.StartsWith("Hugo") && !string.IsNullOrWhiteSpace(l)).ToList();
        Assert.NotEmpty(dataLines);
        Assert.Contains("DEL", dataLines[0]);
    }

    [Fact]
    public async Task InsertionClassifiedCorrectly()
    {
        var ms = new MemoryStream();
        await MafWriter.WriteAsync(ms, [MakeVariant("chr1", 300, "A", "ACGT")], "TUMOR-01");
        var text = Encoding.UTF8.GetString(ms.ToArray());
        var dataLines = text.Split('\n').Where(l => !l.StartsWith('#') && !l.StartsWith("Hugo") && !string.IsNullOrWhiteSpace(l)).ToList();
        Assert.NotEmpty(dataLines);
        Assert.Contains("INS", dataLines[0]);
    }

    [Fact]
    public async Task MnpClassifiedCorrectly()
    {
        var ms = new MemoryStream();
        await MafWriter.WriteAsync(ms, [MakeVariant("chr1", 400, "ACG", "TTT")], "TUMOR-01");
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
        await MafWriter.WriteAsync(ms, [MakeVariant("chr1", 100, "A", "T")], "MY-TUMOR-SAMPLE");
        var text = Encoding.UTF8.GetString(ms.ToArray());
        Assert.Contains("MY-TUMOR-SAMPLE", text);
    }

    [Fact]
    public async Task SomaticVariantTumorDepthWritten()
    {
        var somatic = MakeSomatic(5000, "C", "G", tumorCoverage: 42, normalCoverage: 10);
        var ms = new MemoryStream();
        await MafWriter.WriteAsync(ms, [somatic], "chr7", "TUMOR-01");
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
        await MafWriter.WriteAsync(ms, variants, "TUMOR-01");
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
