namespace OpenMedStack.BioSharp.Io.Tests.Bcl;

using System;
using Microsoft.Extensions.Logging;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Io.Bcl;
using Model.Bcl;
using Xunit;

public class IlluminaRunDataReaderTests : IAsyncLifetime
{
    private readonly ITestOutputHelper? _outputHelper;
    private IlluminaDataReader? _reader;
    private string _binDir = null!;

    public IlluminaRunDataReaderTests(ITestOutputHelper outputHelper)
    {
        _outputHelper = outputHelper;
    }

    public async ValueTask InitializeAsync()
    {
        var bd = new FileInfo(System.Reflection.Assembly.GetExecutingAssembly().Location).Directory;
        _binDir = bd?.FullName ?? throw new InvalidOperationException("Cannot resolve test output directory");

        var runDir = Path.Combine(_binDir, "sampledata");
        _reader = new IlluminaDataReader(
            new DirectoryInfo(runDir),
            LoggerFactory.Create(b => b.AddXUnit(_outputHelper!)),
            ReadStructure.Parse("26T8B98T"));
    }

    public ValueTask DisposeAsync()
    {
        return default;
    }

    [Fact]
    public async Task CanRead()
    {
        var sequences = _reader!.ReadClusterData(1, TestContext.Current.CancellationToken);
        var count = 0;
        await foreach (var sequence in sequences)
        {
            count += await sequence.ReadBclData(DefaultQualityTrimmer.Instance, CancellationToken.None)
                .CountAsync(TestContext.Current.CancellationToken);
        }

        // The data sample has many tiles across 1 lane. Just verify it reads something.
        Assert.True(count > 0);
    }

    [Fact]
    public async Task CanGroup()
    {
        var sequences = await _reader!.ReadClusterData(1, TestContext.Current.CancellationToken)
            .SelectMany(x => x.ReadBclData(DefaultQualityTrimmer.Instance, CancellationToken.None))
            .Select(x => x.Header.Barcode ?? "")
            .Where(barcode => !string.IsNullOrEmpty(barcode))
            .Distinct()
            .CountAsync(TestContext.Current.CancellationToken);
        // The sample data has at least one barcode
        Assert.True(sequences >= 1);
    }
}
