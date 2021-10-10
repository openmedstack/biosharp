﻿namespace OpenMedStack.BioSharp.Io.Tests.Bcl
{
    using System.IO;
    using System.Linq;
    using System.Threading.Tasks;
    using Io.Bcl;
    using Xunit;

    public class ClocsFileReaderTests
    {
        private readonly ClocsFileReader _reader;

        public ClocsFileReaderTests()
        {
            _reader = new ClocsFileReader(new FileInfo("sampledata/Data/Intensities/L001/s_1_1101.clocs"));
        }

        [Fact]
        public async Task CanRead()
        {
            var positions = await _reader.ToArrayAsync().ConfigureAwait(false);

            Assert.Equal(2136539, positions.Length);
        }
    }
}