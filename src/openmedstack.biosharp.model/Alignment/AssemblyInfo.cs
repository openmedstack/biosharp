namespace OpenMedStack.BioSharp.Model.Alignment;

/// <summary>
/// Information about the De Bruijn graph assembly that produced a variant.
/// </summary>
public class AssemblyInfo
{
    /// <summary>Average coverage across all paths in the bubble/tip.</summary>
    public int Coverage { get; }

    /// <summary>Length of the alternative path sequence.</summary>
    public int AltPathLength { get; }

    /// <summary>Number of distinct alt paths contributing to consensus.</summary>
    public int AltPathCount { get; }

    public AssemblyInfo(int coverage, int altPathLength, int altPathCount = 1)
    {
        Coverage = coverage;
        AltPathLength = altPathLength;
        AltPathCount = altPathCount;
    }

    public override string ToString()
    {
        return $"[cov:{Coverage}, altLen:{AltPathLength}, paths:{AltPathCount}]";
    }
}
