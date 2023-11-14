namespace OpenMedStack.BioSharp.Io.Tests.Bcl
{
    using System.IO;
    using System.Linq;
    using System.Threading.Tasks;
    using Io.Bcl;
    using Xunit;

    public class TileIndexTests
    {
        [Fact]
        public async Task CanRead()
        {
            var tileIndex = new TileIndex(new FileInfo(@"..\..\..\..\..\..\200129_NB551214_0127_AH7CMYBGXF\Data\Intensities\BaseCalls\L001\s_1.bci"));
            var tiles = await tileIndex.ToArrayAsync();

            Assert.Equal(216, tiles.Length);
            Assert.Equal(121773386, tiles.Sum(x => x.NumClustersInTile));
        }
    }
}
