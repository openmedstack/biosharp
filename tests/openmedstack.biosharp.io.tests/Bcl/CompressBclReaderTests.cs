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
                new[] { 26, 8, 98 },
                new BclQualityEvaluationStrategy(2),
                false);
        }

        [Fact]
        public async Task CanRead()
        {
            IAsyncEnumerable<BclData> reader = _reader;

            var reads = await reader.ToListAsync().ConfigureAwait(false);

            Assert.True(reads.Count > 0);
        }
    }
}