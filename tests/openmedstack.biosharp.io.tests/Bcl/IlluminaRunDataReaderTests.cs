﻿namespace OpenMedStack.BioSharp.Io.Tests.Bcl
{
    using System.Diagnostics.CodeAnalysis;
    using Microsoft.Extensions.Logging;
    using System.IO;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Io.Bcl;
    using Model.Bcl;
    using Xunit;
    using Xunit.Abstractions;

    public class IlluminaRunDataReaderTests
    {
        private readonly IlluminaDataReader _reader;

        public IlluminaRunDataReaderTests(ITestOutputHelper outputHelper)
        {
            _reader = new IlluminaDataReader(
                new DirectoryInfo(@"data/illumina/25T8B25T"),
                LoggerFactory.Create(b => b.AddXunit(outputHelper)),
                ReadStructure.Parse("25T8B25T"));
        }

        [Fact]
        [RequiresUnreferencedCode("The test data is not annotated for trimming")]
        public async Task CanRead()
        {
            var sequences = _reader.ReadClusterData(1);
            var count = await sequences.SelectAwait(async sequence =>
                    await sequence.ReadBclData(DefaultQualityTrimmer.Instance, CancellationToken.None).CountAsync())
                .SumAsync();

            Assert.Equal(180 * 3, count);
        }

        [Fact]
        [RequiresUnreferencedCode("The test data is not annotated for trimming")]
        public async Task CanGroup()
        {
            var sequences = await _reader.ReadClusterData(1)
                .SelectMany(x => x.ReadBclData(DefaultQualityTrimmer.Instance, CancellationToken.None))
                .Select(x => x.Header.Barcode)
                .Distinct()
                .CountAsync();
            Assert.Equal(40, sequences);
        }

        //[Fact]//(Skip = "IO")]
        //public async Task CanWriteDemultiplexed()
        //{
        //    var tempPath = Path.GetTempPath();
        //    var runInfo = _reader.RunInfo();
        //    var demuxWriter = new DemultiplexFastQWriter(
        //        c => Path.Combine(tempPath, $"{c.Barcode[..1]}.fastq.gz"),
        //        runInfo,
        //        NullLogger.Instance);
        //    await using (demuxWriter)
        //    {
        //        await demuxWriter.WriteDemultiplexed(_reader.ReadClusterData(1)
        //        .SelectMany(x => x.ReadBclData(CancellationToken.None))
        //            .Where(c => c.Type == ReadType.T));
        //    }

        //    await demuxWriter.DisposeAsync();
        //}
    }
}
