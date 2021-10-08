namespace OpenMedStack.BioSharp.Io.Tests.Bcl
{
    using System.IO;
    using System.Linq;
    using System.Threading.Tasks;
    using Io.Bcl;
    using Microsoft.Extensions.Logging.Abstractions;
    using Model.Bcl;
    using Xunit;

    public class IlluminaRunDataReaderTests
    {
        private readonly IlluminaDataReader _reader;

        public IlluminaRunDataReaderTests()
        {
            _reader = new IlluminaDataReader(
                new DirectoryInfo("data/illumina/25T8B25T"),
                new ReadStructure(
                    new Read { IsIndexedRead = "N", Number = 1, NumCycles = 25, Type = ReadType.T },
                    new Read { IsIndexedRead = "Y", Number = 2, NumCycles = 8, Type = ReadType.B },
                    new Read { IsIndexedRead = "N", Number = 3, NumCycles = 25, Type = ReadType.T }));
        }

        [Fact]
        public async Task CanRead()
        {
            var sequences = _reader.ReadClusterData();

            var count = await sequences.CountAsync().ConfigureAwait(false);
            Assert.Equal(180 * 3, count);
        }

        [Fact]
        public async Task CanGroup()
        {
            var sequences = await _reader.ReadClusterData()
                .Select(x => x.Barcode)
                .Distinct()
                .CountAsync()
                .ConfigureAwait(false);
            Assert.Equal(40, sequences);
        }

        [Fact]
        public async Task CanWriteDemultiplexed()
        {
            var tempPath = Path.GetTempPath();
            var runInfo = _reader.RunInfo();
            var demuxWriter = new DemultiplexFastQWriter(
                c => Path.Combine(tempPath, $"{c.Barcode}_{c.Lane,3}.fastq.gz"),
                runInfo,
                NullLogger.Instance);
            await using (demuxWriter.ConfigureAwait(false))
            {
                await demuxWriter.WriteDemultiplexed(_reader.ReadClusterData()
                    .Where(c => c.Type == ReadType.T)).ConfigureAwait(false);
            }

            await demuxWriter.DisposeAsync().ConfigureAwait(false);
        }
    }
}