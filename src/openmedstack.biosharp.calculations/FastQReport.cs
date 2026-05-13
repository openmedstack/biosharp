using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace OpenMedStack.BioSharp.Calculations;

/// <summary>
/// FastQC-equivalent quality report for a FASTQ file.
/// </summary>
public sealed class FastQReport
{
    /// <summary>
    /// Per-base quality score statistics keyed by 0-based cycle index.
    /// </summary>
    public SortedDictionary<int, CycleQualityStats> PerBaseQuality { get; init; } = new();

    /// <summary>
    /// Per-sequence quality score histogram: Phred score → read count.
    /// </summary>
    public SortedDictionary<int, int> PerSequenceQualityHistogram { get; init; } = new();

    /// <summary>
    /// Per-base sequence composition keyed by 0-based cycle index.
    /// </summary>
    public Dictionary<int, CycleComposition> PerBaseCompositionRaw { get; init; } = new();

    /// <summary>
    /// Per-base sequence composition as a lookup dict (cycle → base → %).
    /// Computed from <see cref="PerBaseCompositionRaw"/> on access.
    /// </summary>
    [JsonIgnore]
    public IReadOnlyDictionary<int, IReadOnlyDictionary<char, double>> PerBaseComposition
    {
        get
        {
            var result = new Dictionary<int, IReadOnlyDictionary<char, double>>(PerBaseCompositionRaw.Count);
            foreach (var kv in PerBaseCompositionRaw)
            {
                result[kv.Key] = kv.Value.AsDict();
            }

            return result;
        }
    }

    /// <summary>
    /// GC content histogram: integer GC% → read count.
    /// </summary>
    public SortedDictionary<int, int> GcContentHistogram { get; init; } = new();

    /// <summary>
    /// Estimated duplication level: fraction of reads that are duplicates.
    /// Estimated from the first <see cref="DuplicationEstimateSampleSize"/> reads.
    /// </summary>
    public double DuplicationLevelEstimate { get; init; }

    /// <summary>
    /// Number of reads sampled for duplication estimation.
    /// </summary>
    public int DuplicationEstimateSampleSize { get; init; }

    /// <summary>
    /// Adapter content by position: 0-based cycle index → fraction of reads with adapter at that position.
    /// Only populated when an adapter sequence is supplied.
    /// </summary>
    public SortedDictionary<int, double> AdapterContentByPosition { get; init; } = new();

    /// <summary>Total number of reads processed.</summary>
    public long TotalReads { get; init; }

    /// <summary>Total bases processed.</summary>
    public long TotalBases { get; init; }
}