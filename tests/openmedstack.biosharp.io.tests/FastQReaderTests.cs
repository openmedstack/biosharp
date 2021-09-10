namespace OpenMedStack.BioSharp.Io.Tests
{
    using System.IO;
    using System.Linq;
    using System.Threading.Tasks;
    using FastQ;
    using Microsoft.Extensions.Logging.Abstractions;
    using Xunit;
    using Xunit.Abstractions;

    public class FastQReaderTests
    {
        private const string FastQERR = "ERR164409.fastq.gz";
        private readonly ITestOutputHelper _outputHelper;

        public FastQReaderTests(ITestOutputHelper outputHelper)
        {
            _outputHelper = outputHelper;
        }

        [Fact]
        public async Task CanCreateSequence()
        {
            var parser = new FastQReader();
            await foreach (var sequence in parser.Read(FastQERR).ConfigureAwait(false))
            {
                Assert.NotEmpty(sequence);
            }
        }

        [Fact]
        public async Task CanConvertToString()
        {
            var parser = new FastQReader();
            var sequence = await parser.Read(FastQERR).FirstAsync().ConfigureAwait(false);
            Assert.NotEmpty(sequence);

            _outputHelper.WriteLine(sequence.ToString());
        }

        [Fact]
        public async Task CanWrite()
        {
            var reader = new FastQReader();
            var writer = new FastQWriter(new NullLogger<FastQWriter>());

            var output = new MemoryStream();
            var sequence = await reader.Read(FastQERR).FirstAsync().ConfigureAwait(false);
            await writer.Write(sequence, output).ConfigureAwait(false);

            Assert.True(output.Length > 0);

            await output.DisposeAsync().ConfigureAwait(false);
        }
    }
}
