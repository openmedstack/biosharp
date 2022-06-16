namespace OpenMedStack.BioSharp.Calculations.Tests
{
    using System;
    using Model;
    using Xunit;

    public class UtilsTests
    {
        [Theory]
        [InlineData(0.1, '+')]
        [InlineData(0.2, '\'')]
        [InlineData(0.01, '5')]
        [InlineData(0.001, '?')]
        public void CanCalculateQualityCharacter(double probability, char qualChar)
        {
            var qualityChar = probability.ToQualChar();

            Assert.Equal(qualChar, qualityChar);
        }

        [Theory]
        [InlineData('+', 0.1)]
        [InlineData('5', 0.01)]
        [InlineData('?', 0.001)]
        public void CanConvertToQuality(char qualityChar, double probability)
        {
            Assert.Equal(probability, qualityChar.ToQuality());
        }

        [Fact]
        public void CanMapBack()
        {
            for (var i = 0; i < 100; i++)
            {
                var prob = ((char)i).ToQuality();
                var q = prob.ToQualChar();

                Assert.Equal((char)i, q);
            }
        }

        [Fact]
        public void CanMapPhredBack()
        {
            var rnd = new Random(DateTime.UtcNow.Millisecond);
            for (var i = 0; i < 100; i++)
            {
                var d = rnd.NextDouble();
                var prob = d.ToQuality();
                var q = prob.ToPhred();

                Assert.Equal(d, q, 8);
            }
        }
    }
}