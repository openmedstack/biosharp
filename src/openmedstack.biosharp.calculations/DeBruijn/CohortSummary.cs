namespace OpenMedStack.BioSharp.Calculations.DeBruijn;

/// <summary>
/// Summary statistics for a cohort variant call result.
/// </summary>
public class CohortSummary
{
    public int TotalVariants { get; set; }
    public int OncogenicCount { get; set; }
    public int PresentCount { get; set; }
    public int SubclonalCount { get; set; }
    public int SingleSampleCount { get; set; }
    public double AverageQuality { get; set; }
    public double AverageAlleleFrequency { get; set; }

    public override string ToString()
    {
        return
            $"CohortSummary: {TotalVariants} variants, AvgQuality={AverageQuality:F1}, AvgFreq={AverageAlleleFrequency:F2}. Single={SingleSampleCount}, Oncogenic={OncogenicCount}, Present={PresentCount}, Subclonal={SubclonalCount}";
    }
}