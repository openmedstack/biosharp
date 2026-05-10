using OpenMedStack.BioSharp.Calculations.Alignment;
using Xunit;

namespace OpenMedStack.BioSharp.Calculations.Tests;

public class LocalVariantResultTests
{
    [Fact]
    public void IsPass_HighQuality_ReturnsTrue()
    {
        var v = new LocalVariantResult { QuantitativeQuality = 50 };
        Assert.True(v.IsPass);
    }

    [Fact]
    public void IsPass_LowQuality_ReturnsFalse()
    {
        var v = new LocalVariantResult { QuantitativeQuality = 20 };
        Assert.False(v.IsPass);
    }

    [Fact]
    public void IsSubstitution_TrueWhenSameLengthDifferentBases()
    {
        var v = new LocalVariantResult { Reference = "A", Alternate = "T" };
        Assert.True(v.IsSubstitution);
    }

    [Fact]
    public void IsSubstitution_FalseWhenSameBases()
    {
        var v = new LocalVariantResult { Reference = "A", Alternate = "A" };
        Assert.False(v.IsSubstitution);
    }

    [Fact]
    public void IsInsertion_TrueWhenAltLonger()
    {
        var v = new LocalVariantResult { Reference = "A", Alternate = "ACGT" };
        Assert.True(v.IsInsertion);
    }

    [Fact]
    public void IsInsertion_FalseWhenAltShorter()
    {
        var v = new LocalVariantResult { Reference = "ACGT", Alternate = "A" };
        Assert.False(v.IsInsertion);
    }

    [Fact]
    public void IsDeletion_TrueWhenRefLonger()
    {
        var v = new LocalVariantResult { Reference = "ACGT", Alternate = "A" };
        Assert.True(v.IsDeletion);
    }

    [Fact]
    public void IsDeletion_FalseWhenRefShorter()
    {
        var v = new LocalVariantResult { Reference = "A", Alternate = "ACGT" };
        Assert.False(v.IsDeletion);
    }

    [Fact]
    public void FilterThreshold_IsConstant()
    {
        Assert.Equal(30, LocalVariantResult.FilterThreshold);
    }
}
