namespace OpenMedStack.BioSharp.Io.Tests
{
    using System.IO;
    using System.IO.Compression;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;
    using FastA;
    using Microsoft.Extensions.Logging.Abstractions;
    using Model;
    using Xunit;

    public class FastAWriterTests
    {
        [Fact]
        public async Task CanWriteSingleLineSequence()
        {
            var sequence = new Sequence("test", Encoding.ASCII.GetBytes("GAC"), new byte[3]);
            var writer = new FastAWriter(new NullLogger<FastAWriter>());
            await using var ms = new MemoryStream();
            await writer.Write(sequence, ms).ConfigureAwait(false);
            ms.Position = 0;
            await using var zip = new GZipStream(ms, CompressionMode.Decompress);
            var reader = new StreamReader(zip);
            var content = await reader.ReadToEndAsync().ConfigureAwait(false);

            Assert.Equal(3, content.Split('\n').Length);
        }
        [Fact]
        public async Task CanWriteMultiLineSequence()
        {
            var sequence = new Sequence(
                "test",
                Encoding.ASCII.GetBytes(Enumerable.Repeat("ACGT", 20).SelectMany(x => x).ToArray()),
                new byte[80]);
            var writer = new FastAWriter(new NullLogger<FastAWriter>());
            await using var ms = new MemoryStream();
            await writer.Write(sequence, ms).ConfigureAwait(false);
            ms.Position = 0;
            await using var zip = new GZipStream(ms, CompressionMode.Decompress);
            var reader = new StreamReader(zip);
            var content = await reader.ReadToEndAsync().ConfigureAwait(false);

            Assert.Equal(4, content.Split('\n').Length);
        }
    }
}