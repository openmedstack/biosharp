﻿namespace OpenMedStack.BioSharp.Io.Tests.Bcl
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
                var blockOffset = await reader.Get(i).ConfigureAwait(false);
                Assert.True(pow > blockOffset.BlockOffset);
            }
        }

        [Fact]
        public async Task CanReadIndexFile2()
        {
            var file = new FileInfo(@"N:\sequencing\200129_NB551214_0127_AH7CMYBGXF\Data\Intensities\BaseCalls\L001\0001.bcl.bgzf");
            var reader = new BclIndexReader(file);
            var record = await reader.Get(200).ConfigureAwait(false);
            Assert.True(record.BlockAddress < file.Length);
            var fileStream =File.Open(
                file.FullName,
                new FileStreamOptions
                {
                    Access = FileAccess.Read,
                    Mode = FileMode.Open,
                    Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
                    Share = FileShare.Read
                });
            fileStream.Seek(record.BlockAddress, SeekOrigin.Begin);
            await using var archive = new GZipStream(fileStream, CompressionMode.Decompress);
            var ms = new MemoryStream();
            await using var _ = ms.ConfigureAwait(false);
            //var entryStream = archive.Entries.First().Open();
            await archive.CopyToAsync(ms).ConfigureAwait(false);
            var data = Array.ConvertAll(ms.ToArray().AsMemory(record.BlockOffset).ToArray(), b => (char)b);
        }
    }
}