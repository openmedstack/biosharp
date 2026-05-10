namespace OpenMedStack.BioSharp.Io.Tests.Bcl;

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Io.Bcl;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

public class BclReaderTest
{
    private const string TestDataDir = "data/illumina/readerTests";

    private static string PassingBclFile
    {
        get { return Path.Combine(TestDataDir, "bcl_passing.bcl"); }
    }

    private static string Qual0FailingBclFile
    {
        get { return Path.Combine(TestDataDir, "bcl_failing.bcl"); }
    }

    private static string Qual1FailingBclFile
    {
        get { return Path.Combine(TestDataDir, "bcl_failing2.bcl"); }
    }

    private static string FileTooLong
    {
        get { return Path.Combine(TestDataDir, "bcl_tooLong.bcl"); }
    }

    private static string FileTooShort
    {
        get { return Path.Combine(TestDataDir, "bcl_tooShort.bcl"); }
    }

    private static string GetDataDir()
    {
        var bd = new FileInfo(System.Reflection.Assembly.GetExecutingAssembly().Location).Directory;
        return bd?.FullName ?? throw new InvalidOperationException("Cannot resolve test output directory");
    }

    [Fact]
    public async Task ReadValidFileInfo()
    {
        var dataDir = GetDataDir();
        var filePath = Path.Combine(dataDir, PassingBclFile);
        Assert.True(File.Exists(filePath), $"Passing BCL file not found at {filePath}");

        var bclQualityEvaluationStrategy =
            new BclQualityEvaluationStrategy(BclQualityEvaluationStrategy.IlluminaAllegedMinimumQuality);
        var reader = await BclReader.Create(
            new FileInfo(filePath),
            new TileIndexRecord(1, int.MaxValue, 0, 0),
            bclQualityEvaluationStrategy,
            NullLogger<BclReader>.Instance);

        // Verify we got a reasonable number of clusters per cycle (more than 10)
        Assert.True(reader.NumClustersPerCycle[0] >= 10);

        var enumerator = reader.GetAsyncEnumerator(TestContext.Current.CancellationToken);
        var cyclesRead = 0;
        while (await enumerator.MoveNextAsync())
            ++cyclesRead;

        Assert.True(cyclesRead >= 10);
        bclQualityEvaluationStrategy.AssertMinimumQualities();
        await reader.DisposeAsync();
    }

    public static object[][] FailingFiles()
    {
        var dataDir = GetDataDir();
        return
        [
            [Path.Combine(dataDir, FileTooLong)],
            [Path.Combine(dataDir, FileTooShort)]
        ];
    }

    [Theory]
    [MemberData(nameof(FailingFiles))]
    public async Task FailingFileTest(string failingFile)
    {
        Assert.True(File.Exists(failingFile), $"Test file not found: {failingFile}");

        var bclQualityEvaluationStrategy =
            new BclQualityEvaluationStrategy(
                BclQualityEvaluationStrategy.IlluminaAllegedMinimumQuality);
        var actualException = await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            var reader = await BclReader.Create(
                new FileInfo(failingFile),
                new TileIndexRecord(1, int.MaxValue, 0, 0),
                bclQualityEvaluationStrategy,
                NullLogger<BclReader>.Instance);
            _ = await reader.CountAsync(TestContext.Current.CancellationToken);
        });
        Assert.NotNull(actualException);
    }

    /// <summary>
    /// Asserts appropriate functionality of a quality-minimum customized BCL reader.
    /// </summary>
    [Fact]
    public async Task LowQualityButPassingTest()
    {
        var dataDir = GetDataDir();
        var qual0Path = Path.Combine(dataDir, Qual0FailingBclFile);
        var qual1Path = Path.Combine(dataDir, Qual1FailingBclFile);
        Assert.True(File.Exists(qual0Path), $"Test file not found: {qual0Path}");
        Assert.True(File.Exists(qual1Path), $"Test file not found: {qual1Path}");

        var bclQualityEvaluationStrategy = new BclQualityEvaluationStrategy(1);

        foreach (var filePath in new[] { qual0Path, qual1Path })
        {
            var reader = await BclReader.Create(
                new FileInfo(filePath),
                new TileIndexRecord(1, int.MaxValue, 0, 0),
                bclQualityEvaluationStrategy,
                NullLogger<BclReader>.Instance);
            _ = await reader.CountAsync(TestContext.Current.CancellationToken);
            await reader.DisposeAsync();
        }

        bclQualityEvaluationStrategy.AssertMinimumQualities();
        Assert.True(bclQualityEvaluationStrategy.GetPoorQualityFrequencies().Count >= 1);
    }

    [Fact]
    public async Task LowQualityAndFailingTest()
    {
        var dataDir = GetDataDir();
        var qual0Path = Path.Combine(dataDir, Qual0FailingBclFile);
        var qual1Path = Path.Combine(dataDir, Qual1FailingBclFile);
        Assert.True(File.Exists(qual0Path), $"Test file not found: {qual0Path}");
        Assert.True(File.Exists(qual1Path), $"Test file not found: {qual1Path}");

        var bclQualityEvaluationStrategy =
            new BclQualityEvaluationStrategy(BclQualityEvaluationStrategy.IlluminaAllegedMinimumQuality);

        foreach (var filePath in new[] { qual0Path, qual1Path })
        {
            var reader = await BclReader.Create(
                new FileInfo(filePath),
                new TileIndexRecord(1, int.MaxValue, 0, 0),
                bclQualityEvaluationStrategy,
                NullLogger<BclReader>.Instance);
            _ = await reader.CountAsync(TestContext.Current.CancellationToken);
            await reader.DisposeAsync();
        }

        Assert.True(bclQualityEvaluationStrategy.GetPoorQualityFrequencies().Count >= 1);
        Assert.Throws<Exception>(bclQualityEvaluationStrategy.AssertMinimumQualities);
    }
}
