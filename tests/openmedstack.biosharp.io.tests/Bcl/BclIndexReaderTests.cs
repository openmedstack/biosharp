namespace OpenMedStack.BioSharp.Io.Tests.Bcl
{
    using System.IO;
    using System.Threading.Tasks;
    using Io.Bcl;
    using Xunit;

    public class BclIndexReaderTests
    {
        [Fact]
        public async Task CanReadIndexFile()
        {
            var reader = new BclIndexReader(new FileInfo("data/illumina/parserTests/bciParser/0001.bcl.bgzf"));
            for (var i = 0; i < reader.NumTiles; i++)
            {
                var index = await reader.Get(i).ConfigureAwait(false);
                Assert.True(index > 0);
            }
        }
    }
}