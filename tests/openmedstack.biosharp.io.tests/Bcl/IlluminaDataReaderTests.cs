﻿namespace OpenMedStack.BioSharp.Io.Tests.Bcl
{
    using System.IO;
    using System.Linq;
    using System.Threading.Tasks;
    using Io.Bcl;
    using Xunit;

    public class IlluminaDataReaderTests
    {
        private readonly IlluminaDataReader _reader;

        public IlluminaDataReaderTests()
        {
            _reader = new IlluminaDataReader(
                new DirectoryInfo("sampledata"));
        }

        [Fact]
        public void CanCreateInstance()
        {
            Assert.NotNull(_reader);
        }

        [Fact]
        public async Task CanRead()
        {
            var sequences = _reader.ReadSequences();

            var count = await sequences.CountAsync().ConfigureAwait(false);
            Assert.Equal(2136539, count);
        }

        [Fact]
        public async Task CanGroup()
        {
            var sequences = await _reader.ReadSequences()
                .Select(x => x.index)
                .Distinct()
                .CountAsync()
                .ConfigureAwait(false);
            Assert.Equal(15749, sequences);
        }

        [Fact(Skip = "IO")]
        public async Task CanWriteDemultiplexed()
        {
            var tempPath = Path.GetTempPath();
            var demuxWriter = new DemultiplexFastQWriter(
                            (r, s) => Path.Combine(tempPath, $"{r.Instrument}_{s}.fastq.gz"),
                            _reader.RunInfo());
            await using (demuxWriter.ConfigureAwait(false))
            {
                var sequences = _reader.ReadSequences();
                await demuxWriter.WriteDemultiplexed(sequences)
                    .ConfigureAwait(false);
            }

            await demuxWriter.DisposeAsync().ConfigureAwait(false);
        }
    }
}