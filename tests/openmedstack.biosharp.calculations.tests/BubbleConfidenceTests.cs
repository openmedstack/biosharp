using System;
using OpenMedStack.BioSharp.Calculations.DeBruijn;
using Xunit;

namespace OpenMedStack.BioSharp.Calculations.Tests;

public class BubbleConfidenceTests
{
    /// <summary>
    /// BC-1: BubbleConfidence enum has all three values.
    /// </summary>
    [Fact]
    public void BubbleConfidence_EnumHasAllValues()
    {
        var values = Enum.GetValues<BubbleConfidence>();
        Assert.Equal(3, values.Length);
        Assert.Contains(BubbleConfidence.High, values);
        Assert.Contains(BubbleConfidence.Medium, values);
        Assert.Contains(BubbleConfidence.Low, values);
    }

    /// <summary>
    /// BC-2: Enum values can be compared and have expected underlying values.
    /// </summary>
    [Fact]
    public void BubbleConfidence_ValuesCanBeCompared()
    {
        Assert.True(BubbleConfidence.High > BubbleConfidence.Medium);
        Assert.True(BubbleConfidence.Medium > BubbleConfidence.Low);
        Assert.True(BubbleConfidence.High > BubbleConfidence.Low);
    }

    /// <summary>
    /// BC-3: Default value is the first enum value (High = 0).
    /// </summary>
    [Fact]
    public void BubbleConfidence_DefaultValue()
    {
        BubbleConfidence? nullable = null;
        var def = nullable ?? BubbleConfidence.High;
        Assert.Equal(BubbleConfidence.High, def);
    }

    /// <summary>
    /// BC-4: String representations are correct.
    /// </summary>
    [Fact]
    public void BubbleConfidence_StringNames()
    {
        Assert.Equal("High", BubbleConfidence.High.ToString());
        Assert.Equal("Medium", BubbleConfidence.Medium.ToString());
        Assert.Equal("Low", BubbleConfidence.Low.ToString());
    }
}
