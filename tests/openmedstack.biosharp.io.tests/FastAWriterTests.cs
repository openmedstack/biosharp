namespace OpenMedStack.BioSharp.Io.Tests
{
    using System.IO;
    using System.IO.Compression;
    using System.Linq;
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
            var sequence = new Sequence("test", "GAC".ToCharArray(), new char[3]);
            var writer = new FastAWriter(new NullLogger<FastAWriter>());
            var ms = new MemoryStream();
            await using var _ = ms;
            await writer.Write(sequence, ms);
            ms.Position = 0;
            var zip = new GZipStream(ms, CompressionMode.Decompress);
            await using var __ = zip;
            var reader = new StreamReader(zip);
            var content = await reader.ReadToEndAsync();

            Assert.Equal(3, content.Split('\n').Length);
        }
        [Fact]
        public async Task CanWriteMultiLineSequence()
        {
            var sequence = new Sequence(
                "test",
                Enumerable.Repeat("ACGT", 20).SelectMany(x => x).ToArray(),
                new char[80]);
            var writer = new FastAWriter(new NullLogger<FastAWriter>());
            var ms = new MemoryStream();
            await using var _ = ms;
            await writer.Write(sequence, ms);
            ms.Position = 0;
            var zip = new GZipStream(ms, CompressionMode.Decompress);
            await using var __ = zip;
            var reader = new StreamReader(zip);
            var content = await reader.ReadToEndAsync();

            Assert.Equal(4, content.Split('\n').Length);
        }
    }
}
