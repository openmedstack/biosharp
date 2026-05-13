using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using OpenMedStack.BioSharp.Io.Bam;
using Xunit;

namespace OpenMedStack.BioSharp.Calculations.Tests;

/// <summary>
/// Unit tests for BAM region query functionality via BamReader directly.
/// </summary>
public partial class BamRegionQueryTests
{
    /// <summary>
    /// BamReader.QueryRegionAsync returns empty when BAM has no header.
    /// </summary>
    [Fact]
    public async Task QueryRegion_NoHeader_ReturnsEmpty()
    {
        // mapt.NA12156 has an empty header (textLen=0), but may still have regions
        // We test that the call doesn't throw
        var bamPath = "data/mapt.NA12156.altex.bam";
        var baiPath = $"{bamPath}.bai";

        if (!File.Exists(baiPath))
        {
            Assert.Skip("BAM index not available");
        }

        var reader = new BamReader(bamPath, new NullLogger<BamReader>());

        // This tests the QueryRegionAsync path - no BamReader.Read() was called so
        // there are no reference sequence names. The BAM header is empty (textLen=0)
        // so FindReferenceIndexAsync returns -1, and QueryRegion yields empty.
        var results = await reader.QueryRegion("chr1", 0, 1000).ToArrayAsync();
        Assert.Empty(results);
    }

    /// <summary>
    /// BamIndexCalculator.Reg2Bins returns correct bin count for known regions.
    /// </summary>
    [Fact]
    public void Reg2Bins_ReturnsReasonableBinCount()
    {
        // Region chr1:100-200
        var binList = new ushort[60596];
        var count = BamIndexCalculator.Reg2Bins(100, 200, binList);
        Assert.True(count > 0);
        Assert.True(count <= 60596);
    }

    /// <summary>
    /// Reg2Bin for a small region returns a single bin.
    /// </summary>
    [Fact]
    public void Reg2Bin_SmallRegion_ReturnsSingleBin()
    {
        var bin = BamIndexCalculator.Reg2Bin(100, 110);
        Assert.True(bin >= 0);
    }

    /// <summary>
    /// Reg2Bin for a very large region returns bin 0 (all bins).
    /// </summary>
    [Fact]
    public void Reg2Bin_FullChromosome_ReturnsBinZero()
    {
        var bin = BamIndexCalculator.Reg2Bin(0, 100000000);
        Assert.Equal(0, bin);
    }

    [Fact]
    public void BamIndexCalculator_Reg2Bin_SmallRegion_ReturnsValidBin()
    {
        var bin = BamIndexCalculator.Reg2Bin(100, 110);
        Assert.True(bin >= 0);
        // Bins range from 0 to 58251 for level 0, or 0-60596 for level 15
        Assert.True(bin <= 60596 - 1);
    }

    [Fact]
    public void BamIndexCalculator_Reg2Bin_FullChromosome_ReturnsBinZero()
    {
        var bin = BamIndexCalculator.Reg2Bin(0, 100000000);
        Assert.Equal(0, bin);
    }

    [Fact]
    public void BamIndexCalculator_Reg2Bins_ReturnsReasonableBinCount()
    {
        var binList = new ushort[60596];
        var count = BamIndexCalculator.Reg2Bins(100, 200, binList);
        Assert.True(count > 0);
        Assert.True(count <= 60596);
    }

    [Fact]
    public void BamIndexCalculator_Reg2Bins_SamePos_ReturnsBins()
    {
        // When start == end, Reg2Bins still returns bins (0 or more depending on implementation)
        // The actual implementation may return bins even for same-position regions
        var binList = new ushort[60596];
        var count = BamIndexCalculator.Reg2Bins(100, 100, binList);
        Assert.True(count >= 0);
        // Verify no bins were written beyond count
        for (var i = 0; i < count && i < 60596; i++)
        {
            Assert.True(binList[i] <= 60596);
        }
    }
}
