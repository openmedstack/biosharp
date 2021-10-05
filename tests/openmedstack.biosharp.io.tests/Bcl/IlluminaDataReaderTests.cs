namespace OpenMedStack.BioSharp.Io.Tests.Bcl
{
    using System.IO;
    using System.Linq;
    using System.Threading.Tasks;
    using Io.Bcl;
    using Xunit;

    public class IlluminaDataReaderTests
    {
        private readonly IlluminaDataReader _reader;

        public IlluminaDataReaderTests()
        {
            _reader = new IlluminaDataReader(new DirectoryInfo("sampledata"));
        }

        [Fact]
        public void CanCreateInstance()
        {
            Assert.NotNull(_reader);
        }

        [Fact]
        public async Task CanRead()
        {
            var sequences = _reader.Read();

            var count = await sequences.CountAsync().ConfigureAwait(false);
            Assert.Equal(6409617 ,count);
        }
    }
}