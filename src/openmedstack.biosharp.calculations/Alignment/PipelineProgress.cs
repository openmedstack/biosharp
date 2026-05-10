namespace OpenMedStack.BioSharp.Calculations.Alignment;

using System;

/// <summary>
/// Progress snapshot reported by the variant calling pipeline as it processes reads.
/// </summary>
public sealed class PipelineProgress
{
    /// <summary>Total number of reads seen so far.</summary>
    public int ReadsProcessed { get; init; }

    /// <summary>Number of reads that produced a mapping alignment.</summary>
    public int ReadsMapped { get; init; }

    /// <summary>Number of reads discarded by quality or other filters.</summary>
    public int ReadsFiltered { get; init; }

    /// <summary>Number of variant sites identified so far (pre-merge).</summary>
    public int VariantsCalled { get; init; }

    /// <summary>Human-readable name of the current pipeline phase (e.g. "Alignment").</summary>
    public string CurrentPhase { get; init; } = string.Empty;

    /// <summary>Wall-clock time elapsed since the pipeline started.</summary>
    public TimeSpan Elapsed { get; init; }
}
