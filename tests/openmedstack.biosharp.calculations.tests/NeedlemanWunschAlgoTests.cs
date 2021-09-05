namespace OpenMedStack.BioSharp.Calculations.Tests
{
    using System.Threading.Tasks;
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
    }
}