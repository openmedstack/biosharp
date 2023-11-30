namespace OpenMedStack.BioSharp.Io.Tests.Bcl
{
    using System;
    using System.IO;
    using System.IO.Compression;
    using System.Threading.Tasks;
    using Io.Bcl;
    using Xunit;

    public class BclIndexReaderTests
    {
        [Fact]
        public async Task CanReadIndexFile()
        {
            var pow = Math.Pow(2, 16);
            var reader = new BclIndexReader(new FileInfo("illumina_run/0001.bcl.bgzf"));
            for (var i = 0; i < reader.NumTiles; i++)
            {
                var blockOffset = await reader.Get(i);
                Assert.True(pow > blockOffset.BlockOffset);
            }
        }

        [Fact]
        public async Task CanReadIndexFile2()
        {
            var file = new FileInfo(@"illumina_run/0001.bcl.bgzf");
            var reader = new BclIndexReader(file);
            var record = await reader.Get(200);
            Assert.True(record.BlockAddress < (ulong)file.Length);
            var fileStream =File.Open(
                file.FullName,
                new FileStreamOptions
                {
                    Access = FileAccess.Read,
                    Mode = FileMode.Open,
                    Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
                    Share = FileShare.Read
                });
            fileStream.Seek((long)record.BlockAddress, SeekOrigin.Begin);
            var archive = new GZipStream(fileStream, CompressionMode.Decompress);
            await using var __ = archive;
            var ms = new MemoryStream();
            await using var _ = ms;
            //var entryStream = archive.Entries.First().Open();
            await archive.CopyToAsync(ms);
            var data = Array.ConvertAll(ms.ToArray().AsMemory(record.BlockOffset).ToArray(), b => (char)b);
        }
    }
}
