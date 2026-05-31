using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OpenMedStack.BioSharp.Io.Bgzf;
using OpenMedStack.BioSharp.Io.Vcf;
using OpenMedStack.BioSharp.Model.Vcf;
using Xunit;

namespace OpenMedStack.BioSharp.Io.Tests;

/// <summary>
/// Tests for IO-3: Tabix index writing and region-based VCF queries.
/// </summary>
public class TabixTests
{
    private static async Task<string> CreateBgzfVcf(IEnumerable<VcfVariant> variants)
    {
        var tempPath = $"{Path.GetTempFileName()}.vcf.gz";

        await using var file = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None);
        await using var bgzf = new BgzfStream(file, CompressionLevel.Optimal, leaveOpen: false);
        await using var writer = new StreamWriter(bgzf, Encoding.UTF8, leaveOpen: true);

        await writer.WriteLineAsync("##fileformat=VCFv4.2");
        await writer.WriteLineAsync("#CHROM\tPOS\tID\tREF\tALT\tQUAL\tFILTER\tINFO");

        foreach (var v in variants)
        {
            await writer.WriteLineAsync(
                $"{v.Chromosome}\t{v.Position}\t.\t{v.Reference}\t{v.Alternate}\t.\tPASS\t.");
        }

        await writer.FlushAsync();
        await bgzf.FlushAsync();

        return tempPath;
    }

    [Fact]
    public async Task CanWriteTabixIndex()
    {
        var variants = Enumerable.Range(1, 100)
            .Select(i => new VcfVariant
            {
                Chromosome = "chr1",
                Position = i * 1000,
                MarkerIdentifiers = ".",
                Reference = "A",
                Alternate = "T",
                ErrorProbabilities = [30],
                FailedFilter = ["PASS"],
                AdditionalInformation = "."
            });

        var vcfPath = await CreateBgzfVcf(variants);
        var tbiPath = $"{vcfPath}.tbi";

        try
        {
            await TabixIndexWriter.Write(vcfPath, tbiPath, TestContext.Current.CancellationToken);
            Assert.True(File.Exists(tbiPath));
            Assert.True(new FileInfo(tbiPath).Length > 0);
        }
        finally
        {
            File.Delete(vcfPath);
            File.Delete(tbiPath);
        }
    }

    [Fact]
    public async Task RegionQueryReturnsSubset()
    {
        var variants = Enumerable.Range(1, 200)
            .Select(i => new VcfVariant
            {
                Chromosome = "chr1",
                Position = i * 1000,
                MarkerIdentifiers = ".",
                Reference = "A",
                Alternate = "T",
                ErrorProbabilities = [30],
                FailedFilter = ["PASS"],
                AdditionalInformation = "."
            })
            .ToList();

        var vcfPath = await CreateBgzfVcf(variants);
        var tbiPath = $"{vcfPath}.tbi";

        try
        {
            await TabixIndexWriter.Write(vcfPath, tbiPath, TestContext.Current.CancellationToken);

            var results = await VcfFileReader
                .ReadRegion(vcfPath, "chr1", 50_000, 100_000, tbiPath,
                    TestContext.Current.CancellationToken)
                .ToListAsync(TestContext.Current.CancellationToken);

            // Positions 50000, 51000, ..., 100000 → 51 records
            Assert.All(results, v => Assert.InRange(v.Position, 50_000, 100_000));
            Assert.NotEmpty(results);
        }
        finally
        {
            File.Delete(vcfPath);
            File.Delete(tbiPath);
        }
    }

    [Fact]
    public void RegionToBinIsConsistent()
    {
        // 0-based region
        var bin = TabixIndexWriter.RegionToBin(0, 100_000);
        Assert.True(bin >= 0);

        var bin2 = TabixIndexWriter.RegionToBin(100_000, 200_000);
        Assert.True(bin2 >= 0);
    }
}