namespace OpenMedStack.BioSharp.Io.Tests.FastQ
{
    using System.IO;
    using System.Linq;
    using System.Threading.Tasks;
    using Io.FastQ;
    using Microsoft.Extensions.Logging.Abstractions;
    using Xunit;
    using Xunit.Abstractions;

    public class FastQReaderTests
    {
        private const string FastQerr = "ERR164409.fastq.gz";
        private readonly ITestOutputHelper _outputHelper;

        public FastQReaderTests(ITestOutputHelper outputHelper)
        {
            _outputHelper = outputHelper;
        }

        [Fact]
        public async Task CanCreateSequence()
        {
            var parser = new FastQReader(NullLogger.Instance);
            await foreach (var sequence in parser.Read(FastQerr))
            {
                Assert.NotEmpty(sequence);
            }
        }

        [Fact]
        public async Task CanConvertToString()
        {
            var parser = new FastQReader(NullLogger.Instance);
            var sequence = await parser.Read(FastQerr).FirstAsync();
            Assert.NotEmpty(sequence);

            _outputHelper.WriteLine(sequence.ToString());
        }

        [Fact]
        public async Task CanWrite()
        {
            var output = new MemoryStream();
            var reader = new FastQReader(NullLogger.Instance);
            var writer = new FastQWriter(new NullLogger<FastQWriter>(), output, Stream.Null);

            var sequence = await reader.Read(FastQerr).FirstAsync();
            await writer.Write(sequence);

            Assert.True(output.Length > 0);

            await output.DisposeAsync();
        }
    }
}
