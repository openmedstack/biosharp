namespace OpenMedStack.BioSharp.Io.Tests.Bcl
{
    using System;
    using System.IO;
    using System.IO.Compression;
    using System.Linq;
    using System.Threading.Tasks;
    using Io.Bcl;
    using Xunit;

    public class BclIndexReaderTests
    {
        [Fact]
        public async Task CanReadIndexFile()
        {
            var pow = Math.Pow(2, 16);
            var reader = new BclIndexReader(new FileInfo("data/illumina/parserTests/bciParser/0001.bcl.bgzf"));
            for (var i = 0; i < reader.NumTiles; i++)
            {
                var (_, blockOffset) = await reader.Get(i).ConfigureAwait(false);
                Assert.True(pow > blockOffset);
            }
        }

        [Fact]
        public async Task CanReadIndexFile2()
        {
            var file = new FileInfo(@"Z:\sequencing\200129_NB551214_0127_AH7CMYBGXF\Data\Intensities\BaseCalls\L001\0001.bcl.bgzf");
            var reader = new BclIndexReader(file);
            var (blockAddress, blockOffset) = await reader.Get(200).ConfigureAwait(false);
            Assert.True(blockAddress < file.Length);
            var fileStream = File.OpenRead(file.FullName);
            fileStream.Seek(blockAddress, SeekOrigin.Begin);
            using var archive = new ZipArchive(fileStream);
            var ms = new MemoryStream();
            await using var _ = ms.ConfigureAwait(false);
            var entryStream = archive.Entries.First().Open();
            await entryStream.CopyToAsync(ms).ConfigureAwait(false);
            var data = Array.ConvertAll(ms.ToArray().AsMemory(blockOffset).ToArray(), b => (char)b);
        }
    }
}