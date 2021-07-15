namespace OpenMedStack.BioSharp.Io.Tests
{
    using System.Linq;
    using System.Threading.Tasks;
    using FastA;
    using Xunit;

    public class FastAReaderTests
    {
        [Theory]
        [InlineData("Homo_sapiens.GRCh38.dna.chromosome.1.fa.gz")]
        public async Task CanReadSingleSequence(string path)
        {
            var parser = new FastAReader();
            var sequence = await parser.ReadGz(path, true).SingleAsync().ConfigureAwait(false);
            Assert.NotEmpty(sequence);
        }
    }
}