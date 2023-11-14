namespace OpenMedStack.BioSharp.Io.Tests.Bcl
{
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Threading.Tasks;
    using Divergic.Logging.Xunit;
    using Io.Bcl;
    using Model.Bcl;
    using Xunit;
    using Xunit.Abstractions;

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
            _reader = BclReader.Create(
                fileInfos,
                new[]
                {
                    new Read { IsIndexedRead = "N", NumCycles = 26, Number = 1, Type = ReadType.T },
                    new Read { IsIndexedRead = "Y", NumCycles = 8, Number = 2, Type = ReadType.B },
                    new Read { IsIndexedRead = "N", NumCycles = 98, Number = 3, Type = ReadType.T }
                },
                new TileIndexRecord(1, int.MaxValue, 0, 0),
                new BclQualityEvaluationStrategy(2),
                new TestOutputLogger(nameof(CompressBclReaderTests), outputHelper)).GetAwaiter().GetResult();
        }

        [Fact]
        public async Task CanRead()
        {
            IAsyncEnumerable<ReadData[]> reader = _reader;

            var reads = await reader.ToListAsync();

            Assert.True(reads.Count > 0);
        }
    }
}
