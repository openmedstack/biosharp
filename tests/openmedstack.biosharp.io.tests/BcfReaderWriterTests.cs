using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using OpenMedStack.BioSharp.Io.Bgzf;
using OpenMedStack.BioSharp.Io.Vcf;
using OpenMedStack.BioSharp.Model.Vcf;
using Xunit;

namespace OpenMedStack.BioSharp.Io.Tests;

/// <summary>
/// Tests for IO-7: BGZF-compressed VCF writing (VcfWriter in calculations).
/// </summary>
public class BcfReaderWriterTests
{
    private static VcfVariant MakeVariant(string chrom, int pos, string @ref, string alt, int qual = 30)
        => new()
        {
            Chromosome = chrom,
            Position = pos,
            MarkerIdentifiers = ".",
            Reference = @ref,
            Alternate = alt,
            ErrorProbabilities = [qual],
            FailedFilter = ["PASS"],
            AdditionalInformation = "."
        };

    [Fact]
    public async Task RoundTripSingleVariant()
    {
        var ms = new MemoryStream();
        await using var writer = new BcfWriter(ms, leaveOpen: true);

        var variant = MakeVariant("chr1", 1000, "A", "T");
        await writer.WriteHeader(["chr1"], cancellationToken: TestContext.Current.CancellationToken);
        await writer.WriteVariant(variant, TestContext.Current.CancellationToken);
        await writer.DisposeAsync();

        ms.Position = 0;
        var bgzfRead = new BgzfStream(ms, CompressionMode.Decompress, leaveOpen: true);
        await using var reader = new BcfReader(bgzfRead, leaveOpen: false);
        var header = await reader.ReadHeader(TestContext.Current.CancellationToken);

        Assert.NotEmpty(header);

        var variants = await reader.ReadVariants(TestContext.Current.CancellationToken)
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Single(variants);
        Assert.Equal("chr1", variants[0].Chromosome);
        Assert.Equal(1000, variants[0].Position);
        Assert.Equal("A", variants[0].Reference);
        Assert.Equal("T", variants[0].Alternate);
    }

    [Fact]
    public async Task RoundTripMultipleVariants()
    {
        var variants = new[]
        {
            MakeVariant("chr1", 100, "A", "C"),
            MakeVariant("chr1", 200, "G", "T"),
            MakeVariant("chr1", 300, "ATCG", "A"),  // deletion
            MakeVariant("chr1", 400, "A", "ATCG"),  // insertion
        };

        var ms = new MemoryStream();
        await using var writer = new BcfWriter(ms, leaveOpen: true);
        await writer.WriteHeader(["chr1"], cancellationToken: TestContext.Current.CancellationToken);
        await writer.WriteVariants(variants, TestContext.Current.CancellationToken);
        await writer.DisposeAsync();

        ms.Position = 0;
        var bgzfRead = new BgzfStream(ms, CompressionMode.Decompress, leaveOpen: true);
        await using var reader = new BcfReader(bgzfRead, leaveOpen: false);
        await reader.ReadHeader(TestContext.Current.CancellationToken);
        var readBack = await reader.ReadVariants(TestContext.Current.CancellationToken)
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(4, readBack.Count);
        Assert.Equal("C", readBack[0].Alternate);
        Assert.Equal("T", readBack[1].Alternate);
        Assert.Equal("A", readBack[2].Alternate);
        Assert.Equal("ATCG", readBack[3].Alternate);
    }

    [Fact]
    public async Task AllFieldsPreserved()
    {
        var variant = MakeVariant("chr7", 55_249_071, "ACT", "A", 50);

        var ms = new MemoryStream();
        await using var writer = new BcfWriter(ms, leaveOpen: true);
        await writer.WriteHeader(["chr7"], cancellationToken: TestContext.Current.CancellationToken);
        await writer.WriteVariant(variant, TestContext.Current.CancellationToken);
        await writer.DisposeAsync();

        ms.Position = 0;
        var bgzfRead = new BgzfStream(ms, CompressionMode.Decompress, leaveOpen: true);
        await using var reader = new BcfReader(bgzfRead, leaveOpen: false);
        await reader.ReadHeader(TestContext.Current.CancellationToken);
        var readBack = await reader.ReadVariants(TestContext.Current.CancellationToken)
            .FirstAsync(TestContext.Current.CancellationToken);

        Assert.Equal("chr7", readBack.Chromosome);
        Assert.Equal(55_249_071, readBack.Position);
        Assert.Equal("ACT", readBack.Reference);
        Assert.Equal("A", readBack.Alternate);
    }
}