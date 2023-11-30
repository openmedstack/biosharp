using Microsoft.Extensions.Logging;

namespace OpenMedStack.BioSharp.Io.Tests.Bcl
{
    using System.Diagnostics;
    using System.Diagnostics.CodeAnalysis;
    using System.IO;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Divergic.Logging.Xunit;
    using Io.Bcl;
    using Xunit;
    using Xunit.Abstractions;

    public class IlluminaDataReaderTests
    {
        private readonly ITestOutputHelper _outputHelper;
        private readonly IlluminaDataReader _reader;

        public IlluminaDataReaderTests(ITestOutputHelper outputHelper)
        {
            _outputHelper = outputHelper;
            _reader = new IlluminaDataReader(
                new DirectoryInfo("sampledata"),
                LoggerFactory.Create(b => b.AddXunit(outputHelper)));
        }

        [Fact]
        [RequiresUnreferencedCode("The test data is not annotated for trimming")]
        public async Task CanRead()
        {
            var stopwatch = new Stopwatch();
            stopwatch.Start();
            var sequences = _reader.ReadClusterData(1);

            var count = await sequences
                .SelectMany(x => x.ReadBclData(DefaultQualityTrimmer.Instance, CancellationToken.None))
                .CountAsync();
            stopwatch.Stop();
            Assert.Equal(2136539 * 3, count);
            _outputHelper.WriteLine(stopwatch.Elapsed.ToString());
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
            Assert.Equal(15749, sequences);
        }
    }
}
