namespace OpenMedStack.BioSharp.Io.Tests.Bcl
{
    using System.IO;
    using System.Linq;
    using System.Threading.Tasks;
    using Io.Bcl;
    using Xunit;

    public class FilterFilesReaderTests:IAsyncLifetime
    {
        private FilterFileReader _reader = null!;

        [Fact]
        public void CanRead()
        {
            var amount = _reader.Count();

            Assert.Equal(_reader.NumClusters, amount);
        }

        /// <inheritdoc />
        public async Task InitializeAsync()
        {
            _reader = await FilterFileReader
                .Create(new FileInfo(@"data/illumina/25T8B8B25T/Data/Intensities/BaseCalls/L001/s_1_1101.filter"))
                ;
        }

        /// <inheritdoc />
        public Task DisposeAsync()
        {
            return Task.CompletedTask;
        }
    }
}
