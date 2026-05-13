namespace OpenMedStack.BioSharp.Calculations;

using System;
using System.Collections.Generic;
using Model;

/// <summary>
/// Detects and removes adapter sequences from <see cref="Sequence"/> objects.
///
/// Adapters are scanned at the 3′ end of the read. The first position where the
/// adapter (or a suffix of it long enough to anchor an overlap) matches within the
/// allowed mismatch budget is taken as the trim point; all bases from that position
/// to the 3′ end are removed.
///
/// Built-in adapter presets are available as constants on the nested <see cref="Presets"/> class.
/// </summary>
public sealed class AdapterTrimmer
{
    /// <summary>Well-known Illumina adapter sequences.</summary>
    public static class Presets
    {
        /// <summary>Nextera transposase Read 1 adapter.</summary>
        public const string NexTera = "CTGTCTCTTATACACATCT";

        /// <summary>TruSeq Universal Adapter / Read 1 primer sequence.</summary>
        public const string TruSeqR1 = "AGATCGGAAGAGCACACGTCTGAACTCCAGTCA";

        /// <summary>TruSeq Read 2 adapter.</summary>
        public const string TruSeqR2 = "AGATCGGAAGAGCGTCGTGTAGGGAAAGAGTGT";
    }

    /// <summary>Cumulative trimming statistics over a batch of reads.</summary>
    public sealed class TrimStats
    {
        /// <summary>Reads where at least one adapter base was trimmed.</summary>
        public int ReadsTrimmed { get; internal set; }

        /// <summary>Total number of adapter bases removed across all trimmed reads.</summary>
        public int BasesRemoved { get; internal set; }

        /// <summary>Reads discarded because their post-trim length fell below <see cref="AdapterTrimmer._minLength"/>.</summary>
        public int ReadsDiscarded { get; internal set; }
    }

    /// <summary>Result of trimming a batch of reads.</summary>
    public sealed class TrimResult
    {
        /// <summary>Reads that survived trimming (null entries are discarded reads).</summary>
        public IReadOnlyList<Sequence?> Reads { get; init; } = [];

        /// <summary>Summary statistics.</summary>
        public TrimStats Stats { get; init; } = new();
    }

    private readonly ReadOnlyMemory<char> _adapter;
    private readonly int _maxMismatches;
    private readonly int _minLength;

    /// <summary>
    /// Creates an <see cref="AdapterTrimmer"/>.
    /// </summary>
    /// <param name="adapterSequence">Adapter sequence to detect and remove (5′→3′).</param>
    /// <param name="maxMismatches">Maximum allowed mismatches during adapter matching (default: 2).</param>
    /// <param name="minLength">
    /// Reads shorter than this value after trimming are discarded (returned as <c>null</c>).
    /// Default: 20 bp.
    /// </param>
    public AdapterTrimmer(string adapterSequence, int maxMismatches = 2, int minLength = 20)
    {
        if (string.IsNullOrEmpty(adapterSequence))
        {
            throw new ArgumentException("Adapter sequence must not be empty.", nameof(adapterSequence));
        }

        _adapter = adapterSequence.AsMemory();
        _maxMismatches = maxMismatches;
        _minLength = minLength;
    }

    /// <summary>
    /// Trims adapter from a single read.
    /// </summary>
    /// <param name="read">Input read.</param>
    /// <returns>
    /// A tuple of the trimmed <see cref="Sequence"/> (or <c>null</c> if discarded) and
    /// a per-read <see cref="TrimStats"/>.
    /// </returns>
    public (Sequence? Trimmed, TrimStats Stats) Trim(Sequence read)
    {
        var stats = new TrimStats();
        var readSpan = read.GetData().Span;
        var adapterSpan = _adapter.Span;

        var trimPos = FindAdapterStart(readSpan, adapterSpan, _maxMismatches);

        if (trimPos < 0)
        {
            // No adapter detected — return unchanged
            return (read, stats);
        }

        var basesRemoved = readSpan.Length - trimPos;
        stats.ReadsTrimmed = 1;
        stats.BasesRemoved = basesRemoved;

        if (trimPos < _minLength)
        {
            stats.ReadsTrimmed = 0; // will be counted as discarded
            stats.ReadsDiscarded = 1;
            stats.BasesRemoved = 0;
            return (null, stats);
        }

        return (read[..trimPos], stats);
    }

    /// <summary>
    /// Trims adapter from a batch of reads.
    /// </summary>
    public TrimResult TrimAll(IEnumerable<Sequence> reads)
    {
        var results = new List<Sequence?>();
        var cumulative = new TrimStats();

        foreach (var read in reads)
        {
            var (trimmed, stats) = Trim(read);
            results.Add(trimmed);
            cumulative.ReadsTrimmed += stats.ReadsTrimmed;
            cumulative.BasesRemoved += stats.BasesRemoved;
            cumulative.ReadsDiscarded += stats.ReadsDiscarded;
        }

        return new TrimResult { Reads = results, Stats = cumulative };
    }

    // -------------------------------------------------------------------------
    // Core matching logic
    // -------------------------------------------------------------------------

    /// <summary>
    /// Returns the 0-based read position at which the adapter starts, or −1 if not found.
    ///
    /// The algorithm also checks partial overlaps of the adapter at the 3′ end of the read
    /// (where only the first K bases of the adapter are present). The minimum overlap length
    /// considered is 8 bp (or the adapter length if shorter).
    /// </summary>
    private static int FindAdapterStart(
        ReadOnlySpan<char> read,
        ReadOnlySpan<char> adapter,
        int maxMismatches)
    {
        if (adapter.IsEmpty || read.IsEmpty)
        {
            return -1;
        }

        var minOverlap = Math.Min(8, adapter.Length);

        // Scan every possible start position in the read where the adapter could begin.
        // We include positions within the last (adapterLength - minOverlap) bases of the read
        // so that partial-overlap matches are also caught.
        var searchEnd = read.Length; // exclusive — adapter can start at any position

        for (var pos = 0; pos < searchEnd; pos++)
        {
            var remainingRead = read.Length - pos;
            var matchLen = Math.Min(remainingRead, adapter.Length);

            if (matchLen < minOverlap)
            {
                break; // overlap too short to be meaningful
            }

            var readSlice = read.Slice(pos, matchLen);
            var adapterSlice = adapter[..matchLen];

            var mismatches = CountMismatches(readSlice, adapterSlice);
            if (mismatches <= maxMismatches)
            {
                return pos;
            }
        }

        return -1;
    }

    /// <summary>Counts the number of mismatching positions between two equal-length spans.</summary>
    private static int CountMismatches(ReadOnlySpan<char> a, ReadOnlySpan<char> b)
    {
        var count = 0;
        for (var i = 0; i < a.Length; i++)
        {
            if (char.ToUpperInvariant(a[i]) != char.ToUpperInvariant(b[i]))
            {
                count++;
            }
        }
        return count;
    }
}
