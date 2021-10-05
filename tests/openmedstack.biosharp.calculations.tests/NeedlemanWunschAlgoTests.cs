namespace OpenMedStack.BioSharp.Calculations.Tests
{
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Io.FastQ;
    using Microsoft.Extensions.Logging.Abstractions;
    using NeedlemanWunsch;
    using Xunit;

    public class NeedlemanWunschAlgoTests
    {
        [Theory]
        [InlineData("SEND", "SEND", "AND", "A-ND")]
        [InlineData("SEND", "SEND", "END", "-END")]
        [InlineData("SENDER", "SENDER", "END", "-END--")]
        public async Task CreatesExpectedAlignment(string top, string topAligned, string left, string leftAligned)
        {
            var (top2, left2) = await top.Align(left, -10).ConfigureAwait(false);

            Assert.Equal(topAligned, top2);
            Assert.Equal(leftAligned, left2);
        }

        [Fact]
        public async Task AlignSequences()
        {
            var fastq = "ERR164409.fastq.gz";
            var reader = new FastQReader(NullLogger.Instance);
            var sequences = await reader.Read(fastq, CancellationToken.None).Take(3).ToListAsync().ConfigureAwait(false);

            var oneTwoAligned = await sequences[0].Align(sequences[1], 10).ConfigureAwait(false);

            Assert.Equal(87, oneTwoAligned.GetCombineIndex());

            var twoThreeAligned = await sequences[0].Combine(sequences[1], "test", 87).Align(sequences[2], 10).ConfigureAwait(false);

            Assert.Equal(119, twoThreeAligned.GetCombineIndex());
        }
    }
}