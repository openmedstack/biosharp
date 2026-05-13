using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OpenMedStack.BioSharp.Io.Bcl;
using OpenMedStack.BioSharp.Model.Bcl;
using Xunit;

namespace OpenMedStack.BioSharp.Io.Tests.Bcl;

public class CompressBclReaderTests
{
    private readonly BclReader _reader;

    public CompressBclReaderTests(ITestOutputHelper outputHelper)
    {
        var dir = new DirectoryInfo("sampledata/Data/Intensities/BaseCalls/L001/");
        var fileInfos = dir.GetDirectories()
            .OrderBy(x => x.Name)
            .SelectMany(d => d.GetFiles("*.bcl.gz", SearchOption.AllDirectories))
            .ToList();
        var factory = LoggerFactory.Create(b => b.AddXUnit(outputHelper));
        _reader = BclReader.Create(
            fileInfos,
            [
                new Read { IsIndexedRead = "N", NumCycles = 26, Number = 1, Type = ReadType.T },
                new Read { IsIndexedRead = "Y", NumCycles = 8, Number = 2, Type = ReadType.B },
                new Read { IsIndexedRead = "N", NumCycles = 98, Number = 3, Type = ReadType.T }
            ],
            new TileIndexRecord(1, int.MaxValue, 0, 0),
            new BclQualityEvaluationStrategy(2),
            factory.CreateLogger<BclReader>()).GetAwaiter().GetResult();
    }

    [Fact]
    public async Task CanRead()
    {
        IAsyncEnumerable<ReadData[]> reader = _reader;

        var reads = await reader.ToListAsync(TestContext.Current.CancellationToken);

        Assert.True(reads.Count > 0);
    }
}