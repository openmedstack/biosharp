namespace OpenMedStack.BioSharp.Io.Tests.Bcl
{
    using System;
    using System.IO;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;
    using Io.Bcl;
    using Microsoft.Extensions.Logging.Abstractions;
    using Model;
    using Model.Bcl;
    using Xunit;

    public class IlluminaDataReaderTests
    {
        private readonly IlluminaDataReader _reader;

        public IlluminaDataReaderTests()
        {
            _reader = new IlluminaDataReader(new DirectoryInfo("sampledata"));
        }

        [Fact]
        public async Task CanRead()
        {
            var sequences = _reader.ReadClusterData();

            var count = await sequences.CountAsync().ConfigureAwait(false);
            Assert.Equal(2136539 * 3, count);
        }

        [Fact]
        public async Task CanGroup()
        {
            var sequences = await _reader.ReadClusterData()
                .Select(x => x.Barcode)
                .Distinct()
                .CountAsync()
                .ConfigureAwait(false);
            Assert.Equal(15749, sequences);
        }
    }
}
