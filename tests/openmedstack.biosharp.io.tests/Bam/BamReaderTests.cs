namespace OpenMedStack.BioSharp.Io.Tests.Bam
{
    using System.Threading;
    using System.Threading.Tasks;
    using Io.Bam;
    using Microsoft.Extensions.Logging.Abstractions;
    using Xunit;

    public class BamReaderTests
    {
        private readonly BamReader _reader;

        public BamReaderTests()
        {
            _reader = new BamReader(NullLogger.Instance);
        }

        [Theory]
        [InlineData("GSM409307_UCSD.H3K4me1.bam", 8038434)]
        [InlineData("mapt.NA12156.altex.bam", 326618)]
        public async Task CanRead(string filename, int expectedAlignments)
        {
            var result = await _reader.Read(filename, CancellationToken.None);

            Assert.Equal(expectedAlignments, result.AlignmentSections.Length);
        }
    }
}
