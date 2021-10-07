namespace OpenMedStack.BioSharp.Io.Tests.Bcl
{
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Threading.Tasks;
    using Io.Bcl;
    using Model.Bcl;
    using Xunit;

    public class CompressBclReaderTests
    {
        private readonly BclReader _reader;

        public CompressBclReaderTests()
        {
            var dir = new DirectoryInfo("sampledata/Data/Intensities/BaseCalls/L001/");
            _reader = new BclReader(
                dir.GetDirectories()
                    .OrderBy(x => x)
                    .SelectMany(d => d.GetFiles("*.bcl.gz", SearchOption.AllDirectories))
                    .ToList(),
                new[]
                {
                    new Read { IsIndexedRead = "N", NumCycles = 26, Number = 1, Type = ReadType.Template },
                    new Read { IsIndexedRead = "Y", NumCycles = 8, Number = 2, Type = ReadType.Barcode },
                    new Read { IsIndexedRead = "N", NumCycles = 98, Number = 3, Type = ReadType.Template }
                },
                new BclQualityEvaluationStrategy(2),
                false);
        }

        [Fact]
        public async Task CanRead()
        {
            IAsyncEnumerable<ReadData[]> reader = _reader;

            var reads = await reader.ToListAsync().ConfigureAwait(false);

            Assert.True(reads.Count > 0);
        }
    }
}