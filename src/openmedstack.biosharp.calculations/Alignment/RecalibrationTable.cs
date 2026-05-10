namespace OpenMedStack.BioSharp.Calculations.Alignment;

using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

/// <summary>
/// A recalibration table produced by <see cref="BaseQualityRecalibrator.CollectCovariates"/>.
///
/// Each entry records the empirical error rate observed for a particular combination of
/// (reported Phred quality, read cycle, dinucleotide context). The table is used to adjust
/// per-base quality scores to better reflect the true error probability.
/// </summary>
public sealed class RecalibrationTable
{
    /// <summary>Individual recalibration entries keyed by covariate tuple.</summary>
    public Dictionary<string, RecalibrationEntry> Entries { get; set; } = new();

    /// <summary>
    /// Looks up the empirical quality for a given set of covariates.
    /// Returns the reported quality unchanged when no entry is found.
    /// </summary>
    public int GetEmpiricalQuality(int reportedQuality, int cycle, string dinucContext)
    {
        var key = BuildKey(reportedQuality, cycle, dinucContext);
        if (!Entries.TryGetValue(key, out var entry) || entry.Observations == 0)
        {
            return reportedQuality;
        }

        var empiricalErrorRate = Math.Max(1e-10, (double)entry.Errors / entry.Observations);
        var empiricalPhred = -10.0 * Math.Log10(empiricalErrorRate);
        return (int)Math.Round(Math.Clamp(empiricalPhred, 0, 40));
    }

    internal void Record(int reportedQuality, int cycle, string dinucContext, bool isError)
    {
        var key = BuildKey(reportedQuality, cycle, dinucContext);
        if (!Entries.TryGetValue(key, out var entry))
        {
            entry = new RecalibrationEntry();
            Entries[key] = entry;
        }
        entry.Observations++;
        if (isError)
        {
            entry.Errors++;
        }
    }

    private static string BuildKey(int quality, int cycle, string dinuc) =>
        $"{quality}:{cycle}:{dinuc}";
}

/// <summary>Mutable recalibration entry for a single covariate combination.</summary>
public sealed class RecalibrationEntry
{
    /// <summary>Total bases observed at this covariate combination.</summary>
    public long Observations { get; set; }

    /// <summary>Base mismatches (errors) observed at this covariate combination.</summary>
    public long Errors { get; set; }
}
