namespace OpenMedStack.BioSharp.Io.Tests.Bcl
{
    using System.IO;
    using System.Linq;
    using System.Threading.Tasks;
    using Divergic.Logging.Xunit;
    using Io.Bcl;
    using Microsoft.Extensions.Logging.Abstractions;
    using Model.Bcl;
    using Xunit;
    using Xunit.Abstractions;

    public class IlluminaRunDataReaderTests
    {
        private readonly IlluminaDataReader _reader;

        public IlluminaRunDataReaderTests(ITestOutputHelper outputHelper)
        {
            _reader = new IlluminaDataReader(
                //new DirectoryInfo(@"Z:\sequencing\200129_NB551214_0127_AH7CMYBGXF"),
                //new TestOutputLogger(nameof(IlluminaRunDataReaderTests), outputHelper),
                //ReadStructure.Parse("151T8B8B151T"));
                new DirectoryInfo(@"data/illumina/25T8B25T"),
                new TestOutputLogger(nameof(IlluminaRunDataReaderTests), outputHelper),
                ReadStructure.Parse("25T8B25T"));
        }

        [Fact]
        public async Task CanRead()
        {
            var sequences = _reader.ReadClusterData(1);

            var count = await sequences.CountAsync().ConfigureAwait(false);
            Assert.Equal(180 * 3, count);
        }

        [Fact]
        public async Task CanGroup()
        {
            var sequences = await _reader.ReadClusterData(1)
                .Select(x => x.Barcode)
                .Distinct()
                .CountAsync()
                .ConfigureAwait(false);
            Assert.Equal(40, sequences);
        }

        [Fact(Skip = "IO")]
        public async Task CanWriteDemultiplexed()
        {
            var tempPath = Path.GetTempPath();
            var runInfo = _reader.RunInfo();
            var demuxWriter = new DemultiplexFastQWriter(
                c => Path.Combine(tempPath, $"{c.Barcode}_L{c.Lane.ToString().PadLeft(3, '0')}_{c.Tile}.fastq.gz"),
                runInfo,
                NullLogger.Instance);
            await using (demuxWriter.ConfigureAwait(false))
            {
                await demuxWriter.WriteDemultiplexed(_reader.ReadClusterData(1)
                    .Where(c => c.Type == ReadType.T)).ConfigureAwait(false);
            }

            await demuxWriter.DisposeAsync().ConfigureAwait(false);
        }
    }
}