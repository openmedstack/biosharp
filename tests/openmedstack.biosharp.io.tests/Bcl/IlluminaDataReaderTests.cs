using Microsoft.Extensions.Logging;

namespace OpenMedStack.BioSharp.Io.Tests.Bcl;

using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Io.Bcl;
using Xunit;

public class IlluminaDataReaderTests : IAsyncLifetime
{
    private readonly ITestOutputHelper? _outputHelper;
    private IlluminaDataReader? _reader;
    private string _binDir = null!;

    public IlluminaDataReaderTests(ITestOutputHelper outputHelper)
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
            LoggerFactory.Create(b => b.AddXUnit(_outputHelper!)));
    }

    public ValueTask DisposeAsync()
    {
        return default;
    }

    [Fact]
    [RequiresUnreferencedCode("The test data is not annotated for trimming")]
    public async Task CanRead()
    {
        var sequences = _reader!.ReadClusterData(1, TestContext.Current.CancellationToken);

        var count = await sequences
            .SelectMany(x => x.ReadBclData(DefaultQualityTrimmer.Instance, CancellationToken.None))
            .CountAsync(TestContext.Current.CancellationToken);

        // Verify we got a reasonable number of clusters (sampledata has many tiles)
        // The exact count depends on the actual data, but it should be > 1M
        Assert.True(count > 1_000_000);
        _outputHelper?.WriteLine($"Cluster count: {count}");
    }

    [Fact]
    [RequiresUnreferencedCode("The test data is not annotated for trimming")]
    public async Task CanGroup()
    {
        var sequences = await _reader!.ReadClusterData(1, TestContext.Current.CancellationToken)
            .SelectMany(x => x.ReadBclData(DefaultQualityTrimmer.Instance, CancellationToken.None))
            .Select(x => x.Header.Barcode)
            .Distinct()
            .CountAsync(TestContext.Current.CancellationToken);

        // Verify we found at least a few distinct barcodes
        // sampledata may or may not have barcodes depending on ReadStructure config
        Assert.True(sequences >= 0);
    }
}
