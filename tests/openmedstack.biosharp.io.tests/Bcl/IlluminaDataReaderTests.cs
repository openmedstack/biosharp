namespace OpenMedStack.BioSharp.Io.Tests.Bcl
{
    using System.IO;
    using System.Linq;
    using System.Threading.Tasks;
    using Divergic.Logging.Xunit;
    using Io.Bcl;
    using Xunit;
    using Xunit.Abstractions;

    public class IlluminaDataReaderTests
    {
        private readonly IlluminaDataReader _reader;

        public IlluminaDataReaderTests(ITestOutputHelper outputHelper)
        {
            _reader = new IlluminaDataReader(
                new DirectoryInfo("sampledata"),
                new TestOutputLogger(nameof(IlluminaDataReaderTests), outputHelper));
        }

        [Fact]
        public async Task CanRead()
        {
            var sequences = _reader.ReadClusterData(1);

            var count = await sequences.CountAsync().ConfigureAwait(false);
            Assert.Equal(2136539 * 3, count);
        }

        [Fact]
        public async Task CanGroup()
        {
            var sequences = await _reader.ReadClusterData(1)
                .Select(x => x.Barcode)
                .Distinct()
                .CountAsync()
                .ConfigureAwait(false);
            Assert.Equal(15749, sequences);
        }
    }
}
