namespace OpenMedStack.BioSharp.Io.Tests.Bcl
{
    using System.IO;
    using System.Linq;
    using Io.Bcl;
    using Xunit;

    public class FilterFilesReaderTests
    {
        private readonly FilterFileReader _reader;

        public FilterFilesReaderTests()
        {
            _reader = new FilterFileReader(new FileInfo(
                @"data/illumina/25T8B8B25T/Data/Intensities/BaseCalls/L001/s_1_1101.filter"));
        }

        [Fact]
        public void CanRead()
        {
            var amount = _reader.Count();

            Assert.Equal(_reader.NumClusters, amount);
        }
    }
}