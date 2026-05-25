namespace OpenMedStack.BioSharp.Calculations;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Io.FastQ;
using Model;

/// <summary>
/// Computes FastQC-equivalent quality metrics from a stream of <see cref="Sequence"/> records.
/// </summary>
public static class FastQQualityReport
{
    private const int DuplicationSampleSize = 200_000;
    private const int WriteBatchSize = 1024;

    public sealed class ProcessingResult
    {
        public FastQReport QualityReport { get; init; } = new();

        public AdapterTrimmer.TrimStats TrimStats { get; init; } = new();

        public long ReadsWritten { get; init; }

        public long BasesWritten { get; init; }

        public long FastqBytesWritten { get; init; }
    }

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
        var accumulator = new ReportAccumulator(adapterSequence);
        await foreach (var seq in reads.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            accumulator.Add(seq);
        }

        return accumulator.Build();
    }

    public static async Task<ProcessingResult> ComputeTrimAndWrite(
        IAsyncEnumerable<Sequence> reads,
        AdapterTrimmer trimmer,
        FastQWriter? writer = null,
        string? adapterSequence = null,
        CancellationToken cancellationToken = default)
    {
        var accumulator = new ReportAccumulator(adapterSequence);
        var cumulative = new AdapterTrimmer.TrimStats();
        var writeBatch = writer == null ? null : new List<Sequence>(WriteBatchSize);
        long readsWritten = 0;
        long basesWritten = 0;
        long fastqBytesWritten = 0;

        await foreach (var seq in reads.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            accumulator.Add(seq);
            var (trimmed, stats) = trimmer.Trim(seq);
            cumulative.ReadsTrimmed += stats.ReadsTrimmed;
            cumulative.BasesRemoved += stats.BasesRemoved;
            cumulative.ReadsDiscarded += stats.ReadsDiscarded;

            if (trimmed == null)
            {
                continue;
            }

            readsWritten++;
            basesWritten += trimmed.Length;
            if (writeBatch == null || writer == null)
            {
                continue;
            }

            writeBatch.Add(trimmed);
            if (writeBatch.Count == WriteBatchSize)
            {
                var (_, byteCount) = await writer.Write(writeBatch, cancellationToken).ConfigureAwait(false);
                fastqBytesWritten += byteCount;
                writeBatch.Clear();
            }
        }

        if (writeBatch is { Count: > 0 } && writer != null)
        {
            var (_, byteCount) = await writer.Write(writeBatch, cancellationToken).ConfigureAwait(false);
            fastqBytesWritten += byteCount;
        }

        return new ProcessingResult
        {
            QualityReport = accumulator.Build(),
            TrimStats = cumulative,
            ReadsWritten = readsWritten,
            BasesWritten = basesWritten,
            FastqBytesWritten = fastqBytesWritten
        };
    }

    private sealed class ReportAccumulator
    {
        // Accumulators indexed by cycle
        private readonly Dictionary<int, double> _qualSums = new();
        private readonly Dictionary<int, double> _qualSumSq = new();
        private readonly Dictionary<int, int> _qualCounts = new();
        private readonly Dictionary<int, double> _qualMins = new();
        private readonly Dictionary<int, double> _qualMaxs = new();

        // For quartile computation we store per-cycle histograms
        private readonly Dictionary<int, int[]> _qualHistByCycle = new();

        private readonly Dictionary<int, int> _baseCountsA = new();
        private readonly Dictionary<int, int> _baseCountsC = new();
        private readonly Dictionary<int, int> _baseCountsG = new();
        private readonly Dictionary<int, int> _baseCountsT = new();
        private readonly Dictionary<int, int> _baseCountsN = new();

        // Per-sequence quality histogram
        private readonly Dictionary<int, int> _seqQualHist = new();

        // GC content histogram
        private readonly Dictionary<int, int> _gcHist = new();

        // Adapter content: cycle → count with adapter starting at or after that cycle
        private readonly Dictionary<int, int> _adapterCounts = new();

        // Duplication estimation: track first DuplicationSampleSize reads
        private readonly HashSet<string> _seenSeqs = new();
        private readonly ReadOnlyMemory<char> _adapter;
        private long _totalReads;
        private long _totalBases;
        private int _dupSampleCount;
        private int _dupCount;

        public ReportAccumulator(string? adapterSequence)
        {
            _adapter = adapterSequence?.AsMemory() ?? ReadOnlyMemory<char>.Empty;
        }

        public void Add(Sequence seq)
        {
            _totalReads++;
            var data = seq.GetData();
            var quality = seq.GetQuality();
            var len = data.Length;
            _totalBases += len;

            // Per-cycle quality and composition
            AccumulateCycleStats(
                data.Span, quality.Span,
                _qualSums, _qualSumSq, _qualCounts, _qualMins, _qualMaxs,
                _qualHistByCycle,
                _baseCountsA, _baseCountsC, _baseCountsG, _baseCountsT, _baseCountsN);

            // Per-sequence quality (mean of read)
            var seqMeanPhred = ComputeMeanPhred(quality.Span);
            var seqQualBin = (int)Math.Round(seqMeanPhred);
            _seqQualHist[seqQualBin] = _seqQualHist.GetValueOrDefault(seqQualBin) + 1;

            // GC content
            var gcPct = ComputeGcPercent(data.Span);
            _gcHist[gcPct] = _gcHist.GetValueOrDefault(gcPct) + 1;

            // Adapter content
            if (_adapter.Length > 0)
            {
                AccumulateAdapterContent(data.Span, _adapter.Span, _adapterCounts);
            }

            // Duplication estimation (sample first N reads)
            if (_dupSampleCount < DuplicationSampleSize)
            {
                _dupSampleCount++;
                var seqKey = new string(data.Span);
                if (!_seenSeqs.Add(seqKey))
                {
                    _dupCount++;
                }
            }
        }

        public FastQReport Build()
        {
            if (_totalReads == 0)
            {
                return new FastQReport { TotalReads = 0, TotalBases = 0 };
            }

            // Build per-base quality stats
            var perBaseQuality = new SortedDictionary<int, CycleQualityStats>();
            foreach (var (cycle, count) in _qualCounts)
            {
                var mean = _qualSums[cycle] / count;
                var hist = _qualHistByCycle[cycle];
                var (q1, median, q3) = ComputeQuartiles(hist);
                perBaseQuality[cycle] = new CycleQualityStats
                {
                    Mean = mean,
                    Median = median,
                    LowerQuartile = q1,
                    UpperQuartile = q3,
                    Min = _qualMins[cycle],
                    Max = _qualMaxs[cycle]
                };
            }

            // Build per-base composition
            var perBaseComp = new Dictionary<int, CycleComposition>();
            var allCycles = new HashSet<int>(_baseCountsA.Keys);
            allCycles.UnionWith(_baseCountsC.Keys);
            allCycles.UnionWith(_baseCountsG.Keys);
            allCycles.UnionWith(_baseCountsT.Keys);
            allCycles.UnionWith(_baseCountsN.Keys);
            foreach (var cycle in allCycles)
            {
                var total = _baseCountsA.GetValueOrDefault(cycle)
                            + _baseCountsC.GetValueOrDefault(cycle)
                            + _baseCountsG.GetValueOrDefault(cycle)
                            + _baseCountsT.GetValueOrDefault(cycle)
                            + _baseCountsN.GetValueOrDefault(cycle);

                if (total == 0)
                {
                    continue;
                }

                perBaseComp[cycle] = new CycleComposition
                {
                    A = 100.0 * _baseCountsA.GetValueOrDefault(cycle) / total,
                    C = 100.0 * _baseCountsC.GetValueOrDefault(cycle) / total,
                    G = 100.0 * _baseCountsG.GetValueOrDefault(cycle) / total,
                    T = 100.0 * _baseCountsT.GetValueOrDefault(cycle) / total,
                    N = 100.0 * _baseCountsN.GetValueOrDefault(cycle) / total
                };
            }

            // Adapter content: convert counts to fractions
            var adapterContent = new SortedDictionary<int, double>();
            if (_adapter.Length > 0)
            {
                foreach (var (pos, cnt) in _adapterCounts)
                {
                    adapterContent[pos] = (double)cnt / _totalReads;
                }
            }

            return new FastQReport
            {
                PerBaseQuality = perBaseQuality,
                PerSequenceQualityHistogram = new SortedDictionary<int, int>(_seqQualHist),
                PerBaseCompositionRaw = perBaseComp,
                GcContentHistogram = new SortedDictionary<int, int>(_gcHist),
                DuplicationLevelEstimate = _dupSampleCount == 0
                    ? 0.0
                    : (double)_dupCount / _dupSampleCount,
                DuplicationEstimateSampleSize = _dupSampleCount,
                AdapterContentByPosition = adapterContent,
                TotalReads = _totalReads,
                TotalBases = _totalBases
            };
        }
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
        var minOverlap = Math.Max(1, adapter.Length / 2);
        var pos = AdapterTrimmer.FindAdapterStart(data, adapter, maxMismatches: 0, minOverlap);
        if (pos >= 0)
        {
            counts[pos] = counts.GetValueOrDefault(pos) + 1;
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
