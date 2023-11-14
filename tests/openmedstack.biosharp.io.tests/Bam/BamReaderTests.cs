using System.IO;
using System.Linq;

namespace OpenMedStack.BioSharp.Io.Tests.Bam
{
    using System.Threading;
    using System.Threading.Tasks;
    using Io.Bam;
    using Microsoft.Extensions.Logging.Abstractions;
    using Xunit;

    public class BamReaderTests
    {
        private readonly BamReader _reader = new(NullLogger.Instance);

        [Theory]
        [InlineData("GSM409307_UCSD.H3K4me1.bam", 8038434)]
        [InlineData("mapt.NA12156.altex.bam", 326618)]
        public async Task CanRead(string filename, int expectedAlignments)
        {
            var result = await _reader.Read(filename, CancellationToken.None);

            Assert.Equal(expectedAlignments, result.AlignmentSections.Length);
        }
    }

    public class BamWriterTests
    {
        [Fact]
        public async Task CanWrite()
        {
            var reader = new BamReader(NullLogger.Instance);
            var result = await reader.Read("mapt.NA12156.altex.bam", CancellationToken.None);

            Assert.All(result.AlignmentSections.SelectMany(x => x.Cigar), x => "MIDNSHP=X".Contains(x.Item2));

            var alignmentCount = result.AlignmentSections.Length;
            await using (var output = new BgzfStream(File.Open("bam_test.bam", FileMode.Create, FileAccess.Write),
                System.IO.Compression.CompressionLevel.Optimal, false))
            {
                var writer = new BamWriter(output, NullLogger<BamWriter>.Instance);
                await writer.Write(result, CancellationToken.None);
            }

            var readBack = await reader.Read("bam_test.bam", CancellationToken.None);

            Assert.Equal(alignmentCount, readBack.AlignmentSections.Length);
        }
    }
}
