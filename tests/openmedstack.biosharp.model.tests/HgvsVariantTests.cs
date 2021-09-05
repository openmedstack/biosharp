namespace OpenMedStack.BioSharp.Model.Tests
{
    using Model;
    using Xunit;

    public class HgvsVariantTests
    {
        [Fact]
        public void CanParseSubstitutionVariant()
        {
            const string input = "NM_004006.2:c.4375C>T";
            var variant = HgvsVariant.Parse(input);

            Assert.Equal("NM_004006", variant.Reference);
            Assert.Equal(2, variant.Version);
            Assert.IsType<SubstitutionDescription>(variant.Description);
        }

        [Theory]
        [InlineData("c.4375_4379del")]
        [InlineData("c.4375_4379delCGATT")]
        public void CanParseDeletionVariant(string input)
        {
            var variant = HgvsVariant.Parse("NM_004006.2:" + input);

            Assert.Equal("NM_004006", variant.Reference);
            Assert.Equal(2, variant.Version);
            Assert.IsType<DeletionDescription>(variant.Description);
        }

        [Theory]
        [InlineData("c.4375_4385dup")]
        [InlineData("c.4375_4385dupCGATTATTCCA")]
        public void CanParseDuplicationVariant(string input)
        {
            var variant = HgvsVariant.Parse("NM_004006.2:" + input);

            Assert.Equal("NM_004006", variant.Reference);
            Assert.Equal(2, variant.Version);
            Assert.IsType<DuplicationDescription>(variant.Description);
        }

        [Fact]
        public void CanParseInsertionVariant()
        {
            const string input = "NM_004006.2:c.4375_4376insACCT";
            var variant = HgvsVariant.Parse(input);

            Assert.Equal("NM_004006", variant.Reference);
            Assert.Equal(2, variant.Version);
            Assert.IsType<InsertionDescription>(variant.Description);
        }

        [Theory]
        [InlineData("c.4375_4376delinsAGTT")]
        [InlineData("c.4375_4376delCGinsAGTT")]
        public void CanParseInsertDeleteVariant(string input)
        {
            var variant = HgvsVariant.Parse("NM_004006.2:" + input);

            Assert.Equal("NM_004006", variant.Reference);
            Assert.Equal(2, variant.Version);
            Assert.IsType<DeletionInsertionDescription>(variant.Description);
        }
    }
}
