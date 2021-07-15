namespace OpenMedStack.BioSharp.Io.Tests
{
    using System.IO;
    using System.Linq;
    using System.Threading.Tasks;
    using FastQ;
    using Microsoft.Extensions.Logging.Abstractions;
    using Xunit;

    public class FastQReaderTests
    {
        [Fact]
        public async Task CanCreateSequence()
        {
            const string path = "ERR164409.fastq.gz";
            var parser = new FastQReader();
            await foreach (var sequence in parser.Read(path))
            {
                Assert.NotEmpty(sequence);
            }
        }

        [Fact]
        public async Task CanWrite()
        {
            const string path = "ERR164409.fastq.gz";
            var reader = new FastQReader();
            var writer = new FastQWriter(new NullLogger<FastQWriter>());

            var output = new MemoryStream();
            var sequence = await reader.Read(path).FirstAsync().ConfigureAwait(false);
            await writer.Write(sequence, output).ConfigureAwait(false);

            Assert.True(output.Length > 0);
        }
    }
}
