namespace OpenMedStack.BioSharp.Io.Tests.Bcl
{
    using System.IO;
    using System.Linq;
    using System.Threading.Tasks;
    using Io.Bcl;
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
                    new Read { IsIndexedRead = "N", Number = 1, NumCycles = 25 },
                    new Read { IsIndexedRead = "Y", Number = 2, NumCycles = 8 },
                    new Read { IsIndexedRead = "N", Number = 3, NumCycles = 25 }));
        }
        
        [Fact]
        public async Task CanRead()
        {
            var sequences = _reader.ReadSequences();

            var count = await sequences.CountAsync().ConfigureAwait(false);
            Assert.Equal(180, count);
        }

        [Fact]
        public async Task CanGroup()
        {
            var sequences = await _reader.ReadSequences()
                .Select(x => x.index)
                .Distinct()
                .CountAsync()
                .ConfigureAwait(false);
            Assert.Equal(40, sequences);
        }
    }
}