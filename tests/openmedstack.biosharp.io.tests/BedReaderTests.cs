namespace OpenMedStack.BioSharp.Io.Tests;

using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;

/// <summary>
/// Tests for IO-5: BED/BEDGraph interval file reader.
/// </summary>
public class BedReaderTests
{
    [Fact]
    public async Task Reads3ColumnBed()
    {
        const string content = "chr1\t100\t200\n" +
                               "chr2\t0\t500\n";
        var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));

        var intervals = await BedReader.Read(stream, TestContext.Current.CancellationToken)
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, intervals.Count);
        Assert.Equal("chr1", intervals[0].Chrom);
        Assert.Equal(100, intervals[0].Start);
        Assert.Equal(200, intervals[0].End);
        Assert.False(intervals[0].IsBedGraph);
    }

    [Fact]
    public async Task Reads6ColumnBed()
    {
        const string content = "chr1\t100\t200\tfeature1\t500\t+\n" +
                               "chrX\t300\t400\tfeature2\t0\t-\n";
        var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));

        var intervals = await BedReader.Read(stream, TestContext.Current.CancellationToken)
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, intervals.Count);
        Assert.Equal("feature1", intervals[0].Name);
        Assert.Equal(500f, intervals[0].Score);
        Assert.Equal('+', intervals[0].Strand);
        Assert.Equal('-', intervals[1].Strand);
    }

    [Fact]
    public async Task Reads12ColumnBed()
    {
        const string content = "chr1\t0\t1000\tblock_feat\t900\t+\t100\t900\t0\t3\t200,200,200,\t0,300,600,\n";
        var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));

        var intervals = await BedReader.Read(stream, TestContext.Current.CancellationToken)
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Single(intervals);
        Assert.Equal(3, intervals[0].BlockCount);
        Assert.Equal("200,200,200,", intervals[0].BlockSizes);
        Assert.Equal("0,300,600,", intervals[0].BlockStarts);
    }

    [Fact]
    public async Task ReadsBedGraph()
    {
        const string content = "chr1\t100\t200\t1.5\n" +
                               "chr1\t200\t300\t3.14\n";
        var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));

        var intervals = await BedReader.Read(stream, TestContext.Current.CancellationToken)
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, intervals.Count);
        Assert.True(intervals[0].IsBedGraph);
        Assert.Equal(1.5f, intervals[0].Value!.Value, precision: 5);
        Assert.Equal(3.14f, intervals[1].Value!.Value, precision: 4);
    }

    [Fact]
    public async Task SkipsCommentAndTrackLines()
    {
        const string content =
            "track name=\"test\"\n" +
            "# comment line\n" +
            "browser position chr1:1-1000\n" +
            "chr1\t0\t100\n";
        var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));

        var intervals = await BedReader.Read(stream, TestContext.Current.CancellationToken)
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Single(intervals);
        Assert.Equal("chr1", intervals[0].Chrom);
    }

    [Fact]
    public async Task ReturnsZeroBasedCoordinates()
    {
        const string content = "chr1\t0\t100\n";
        var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));

        var interval = await BedReader.Read(stream, TestContext.Current.CancellationToken)
            .FirstAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0, interval.Start);
        Assert.Equal(100, interval.End);
    }

    [Fact]
    public async Task ReadsMultiChromosomeFile()
    {
        var lines = new StringBuilder();
        for (var i = 1; i <= 22; i++)
        {
            lines.AppendLine($"chr{i}\t{i * 100}\t{i * 200}");
        }

        var stream = new MemoryStream(Encoding.UTF8.GetBytes(lines.ToString()));

        var intervals = await BedReader.Read(stream, TestContext.Current.CancellationToken)
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(22, intervals.Count);
        Assert.Equal("chr1", intervals[0].Chrom);
        Assert.Equal("chr22", intervals[21].Chrom);
    }
}