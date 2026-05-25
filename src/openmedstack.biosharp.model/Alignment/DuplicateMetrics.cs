namespace OpenMedStack.BioSharp.Model.Alignment;

/// <summary>Summary statistics from a duplicate-marking run.</summary>
public sealed class DuplicateMetrics
{
    /// <summary>Total reads evaluated.</summary>
    public int TotalReads { get; set; }

    /// <summary>Reads marked as PCR or optical duplicates.</summary>
    public int DuplicateReads { get; set; }

    /// <summary>Reads additionally identified as optical duplicates (subset of <see cref="DuplicateReads"/>).</summary>
    public int OpticalDuplicateReads { get; set; }

    /// <summary>Fraction of total reads that are duplicates.</summary>
    public double DuplicateRate
    {
        get { return TotalReads == 0 ? 0.0 : (double)DuplicateReads / TotalReads; }
    }
}