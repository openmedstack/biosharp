using System.Linq;

namespace OpenMedStack.BioSharp.Calculations;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Model;

/// <summary>
/// Computes FastQC-equivalent quality metrics from a stream of <see cref="Sequence"/> records.
/// </summary>
public static class FastQQualityReport
{
    private const int DuplicationSampleSize = 200_000;

    /// <summary>
    /// Computes quality metrics from an async sequence of reads.
    /// </summary>
    /// <param name="reads">Input reads (FASTQ records).</param>
    /// <param name="adapterSequence">Optional adapter sequence to scan for at each cycle.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static async Task<FastQReport> Compute(
        IAsyncEnumerable<Sequence> reads,
        string? adapterSequence = null,
        CancellationToken cancellationToken = default)
    {
        // Accumulators indexed by cycle
        var qualSums = new Dictionary<int, double>();
        var qualSumSq = new Dictionary<int, double>();
        var qualCounts = new Dictionary<int, int>();
        var qualMins = new Dictionary<int, double>();
        var qualMaxs = new Dictionary<int, double>();

        // For quartile computation we store per-cycle histograms
        var qualHistByCycle = new Dictionary<int, int[]>();

        var baseCountsA = new Dictionary<int, int>();
        var baseCountsC = new Dictionary<int, int>();
        var baseCountsG = new Dictionary<int, int>();
        var baseCountsT = new Dictionary<int, int>();
        var baseCountsN = new Dictionary<int, int>();

        // Per-sequence quality histogram
        var seqQualHist = new Dictionary<int, int>();

        // GC content histogram
        var gcHist = new Dictionary<int, int>();

        // Adapter content: cycle → count with adapter starting at or after that cycle
        var adapterCounts = new Dictionary<int, int>();

        // Duplication estimation: track first DuplicationSampleSize reads
        var seenSeqs = new HashSet<string>();
        var dupSampleCount = 0;
        var dupCount = 0;

        long totalReads = 0;
        long totalBases = 0;

        var adapterSpan = adapterSequence?.AsMemory() ?? ReadOnlyMemory<char>.Empty;

        await foreach (var seq in reads.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            totalReads++;
            var data = seq.GetData();
            var quality = seq.GetQuality();
            var len = data.Length;
            totalBases += len;

            // Per-cycle quality and composition
            AccumulateCycleStats(
                data.Span, quality.Span,
                qualSums, qualSumSq, qualCounts, qualMins, qualMaxs,
                qualHistByCycle,
                baseCountsA, baseCountsC, baseCountsG, baseCountsT, baseCountsN);

            // Per-sequence quality (mean of read)
            var seqMeanPhred = ComputeMeanPhred(quality.Span);
            var seqQualBin = (int)Math.Round(seqMeanPhred);
            seqQualHist[seqQualBin] = seqQualHist.GetValueOrDefault(seqQualBin) + 1;

            // GC content
            var gcPct = ComputeGcPercent(data.Span);
            gcHist[gcPct] = gcHist.GetValueOrDefault(gcPct) + 1;

            // Adapter content
            if (adapterSpan.Length > 0)
            {
                AccumulateAdapterContent(data.Span, adapterSpan.Span, adapterCounts);
            }

            // Duplication estimation (sample first N reads)
            if (dupSampleCount < DuplicationSampleSize)
            {
                dupSampleCount++;
                var seqKey = new string(data.Span);
                if (!seenSeqs.Add(seqKey))
                {
                    dupCount++;
                }
            }
        }

        if (totalReads == 0)
        {
            return new FastQReport { TotalReads = 0, TotalBases = 0 };
        }

        // Build per-base quality stats
        var perBaseQuality = new SortedDictionary<int, CycleQualityStats>();
        foreach (var (cycle, count) in qualCounts)
        {
            var mean = qualSums[cycle] / count;
            var hist = qualHistByCycle[cycle];
            var (q1, median, q3) = ComputeQuartiles(hist);
            perBaseQuality[cycle] = new CycleQualityStats
            {
                Mean = mean,
                Median = median,
                LowerQuartile = q1,
                UpperQuartile = q3,
                Min = qualMins[cycle],
                Max = qualMaxs[cycle]
            };
        }

        // Build per-base composition
        var perBaseComp = new Dictionary<int, CycleComposition>();
        var allCycles = new HashSet<int>(baseCountsA.Keys);
        allCycles.UnionWith(baseCountsC.Keys);
        allCycles.UnionWith(baseCountsG.Keys);
        allCycles.UnionWith(baseCountsT.Keys);
        allCycles.UnionWith(baseCountsN.Keys);
        foreach (var cycle in allCycles)
        {
            var total = baseCountsA.GetValueOrDefault(cycle)
                        + baseCountsC.GetValueOrDefault(cycle)
                        + baseCountsG.GetValueOrDefault(cycle)
                        + baseCountsT.GetValueOrDefault(cycle)
                        + baseCountsN.GetValueOrDefault(cycle);

            if (total == 0)
            {
                continue;
            }

            perBaseComp[cycle] = new CycleComposition
            {
                A = 100.0 * baseCountsA.GetValueOrDefault(cycle) / total,
                C = 100.0 * baseCountsC.GetValueOrDefault(cycle) / total,
                G = 100.0 * baseCountsG.GetValueOrDefault(cycle) / total,
                T = 100.0 * baseCountsT.GetValueOrDefault(cycle) / total,
                N = 100.0 * baseCountsN.GetValueOrDefault(cycle) / total
            };
        }

        // Adapter content: convert counts to fractions
        var adapterContent = new SortedDictionary<int, double>();
        if (totalReads > 0 && adapterSpan.Length > 0)
        {
            foreach (var (pos, cnt) in adapterCounts)
            {
                adapterContent[pos] = (double)cnt / totalReads;
            }
        }

        return new FastQReport
        {
            PerBaseQuality = perBaseQuality,
            PerSequenceQualityHistogram = new SortedDictionary<int, int>(seqQualHist),
            PerBaseCompositionRaw = perBaseComp,
            GcContentHistogram = new SortedDictionary<int, int>(gcHist),
            DuplicationLevelEstimate = dupSampleCount == 0
                ? 0.0
                : (double)dupCount / dupSampleCount,
            DuplicationEstimateSampleSize = dupSampleCount,
            AdapterContentByPosition = adapterContent,
            TotalReads = totalReads,
            TotalBases = totalBases
        };
    }

    private static void AccumulateCycleStats(
        ReadOnlySpan<char> data,
        ReadOnlySpan<char> quality,
        Dictionary<int, double> sums,
        Dictionary<int, double> sumSq,
        Dictionary<int, int> counts,
        Dictionary<int, double> mins,
        Dictionary<int, double> maxs,
        Dictionary<int, int[]> histByCycle,
        Dictionary<int, int> cntA,
        Dictionary<int, int> cntC,
        Dictionary<int, int> cntG,
        Dictionary<int, int> cntT,
        Dictionary<int, int> cntN)
    {
        for (var i = 0; i < data.Length; i++)
        {
            var phred = quality[i] - 33;

            // Quality accumulators
            sums[i] = sums.GetValueOrDefault(i) + phred;
            sumSq[i] = sumSq.GetValueOrDefault(i) + (double)phred * phred;
            counts[i] = counts.GetValueOrDefault(i) + 1;

            if (!mins.TryGetValue(i, out var curMin) || phred < curMin)
            {
                mins[i] = phred;
            }

            if (!maxs.TryGetValue(i, out var curMax) || phred > curMax)
            {
                maxs[i] = phred;
            }

            // Build per-cycle quality histogram (0-41)
            if (!histByCycle.TryGetValue(i, out var hist))
            {
                hist = new int[42];
                histByCycle[i] = hist;
            }
            var bin = Math.Clamp(phred, 0, 41);
            hist[bin]++;

            // Base composition
            switch (data[i])
            {
                case 'A': case 'a': cntA[i] = cntA.GetValueOrDefault(i) + 1; break;
                case 'C': case 'c': cntC[i] = cntC.GetValueOrDefault(i) + 1; break;
                case 'G': case 'g': cntG[i] = cntG.GetValueOrDefault(i) + 1; break;
                case 'T': case 't': cntT[i] = cntT.GetValueOrDefault(i) + 1; break;
                default: cntN[i] = cntN.GetValueOrDefault(i) + 1; break;
            }
        }
    }

    private static double ComputeMeanPhred(ReadOnlySpan<char> quality)
    {
        if (quality.IsEmpty)
        {
            return 0.0;
        }

        double sum = 0;
        foreach (var t in quality)
        {
            sum += t - 33;
        }

        return sum / quality.Length;
    }

    private static int ComputeGcPercent(ReadOnlySpan<char> data)
    {
        if (data.IsEmpty)
        {
            return 0;
        }

        var gc = 0;
        foreach (var c in data)
        {
            if (c is 'G' or 'g' or 'C' or 'c')
            {
                gc++;
            }
        }
        return (int)Math.Round(100.0 * gc / data.Length);
    }

    private static void AccumulateAdapterContent(
        ReadOnlySpan<char> data,
        ReadOnlySpan<char> adapter,
        Dictionary<int, int> counts)
    {
        // Scan all positions for an adapter match (full or partial overlap)
        var minOverlap = Math.Max(1, adapter.Length / 2);

        for (var pos = 0; pos <= data.Length - minOverlap; pos++)
        {
            var overlapLen = Math.Min(adapter.Length, data.Length - pos);
            if (overlapLen < minOverlap)
            {
                break;
            }

            var match = true;
            for (var k = 0; k < overlapLen; k++)
            {
                if (char.ToUpperInvariant(data[pos + k]) != char.ToUpperInvariant(adapter[k]))
                {
                    match = false;
                    break;
                }
            }
            if (match)
            {
                counts[pos] = counts.GetValueOrDefault(pos) + 1;
                break; // Only record first hit per read
            }
        }
    }

    private static (double q1, double median, double q3) ComputeQuartiles(int[] hist)
    {
        // hist[i] = count of values equal to i
        var total = hist.Sum();

        if (total == 0)
        {
            return (0, 0, 0);
        }

        return (
            InterpolatedPercentile(hist, total, 0.25),
            InterpolatedPercentile(hist, total, 0.50),
            InterpolatedPercentile(hist, total, 0.75));
    }

    private static double InterpolatedPercentile(int[] hist, int total, double p)
    {
        var target = p * total;
        var cumulative = 0;
        for (var i = 0; i < hist.Length; i++)
        {
            cumulative += hist[i];
            if (cumulative >= target)
            {
                return i;
            }
        }
        return hist.Length - 1;
    }
}
