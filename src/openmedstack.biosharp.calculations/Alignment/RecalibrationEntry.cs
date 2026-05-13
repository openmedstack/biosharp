namespace OpenMedStack.BioSharp.Calculations.Alignment;

/// <summary>Mutable recalibration entry for a single covariate combination.</summary>
public sealed class RecalibrationEntry
{
    /// <summary>Total bases observed at this covariate combination.</summary>
    public long Observations { get; set; }

    /// <summary>Base mismatches (errors) observed at this covariate combination.</summary>
    public long Errors { get; set; }
}