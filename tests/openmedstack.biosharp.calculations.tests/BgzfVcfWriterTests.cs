namespace OpenMedStack.BioSharp.Calculations.Tests;

using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Alignment;
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

        var plainPath = $"{Path.GetTempFileName()}.vcf";
        var compressedPath = $"{Path.GetTempFileName()}.vcf.gz";

        try
        {
            await VcfWriter.Write(plainPath, variants, "chr1",
                compress: false, cancellationToken: TestContext.Current.CancellationToken);
            await VcfWriter.Write(compressedPath, variants, "chr1",
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
        var path = $"{Path.GetTempFileName()}.vcf.gz";

        try
        {
            await VcfWriter.Write(path, variants, "chr1",
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

        var path = $"{Path.GetTempFileName()}.vcf.gz";

        try
        {
            await VcfWriter.Write(path, variants, "chr2",
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

        var plainPath = $"{Path.GetTempFileName()}.vcf";
        var compressedPath = $"{Path.GetTempFileName()}.vcf.gz";

        try
        {
            await VcfWriter.Write(plainPath, variants, "chr3",
                compress: false, cancellationToken: TestContext.Current.CancellationToken);
            await VcfWriter.Write(compressedPath, variants, "chr3",
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