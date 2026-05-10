namespace OpenMedStack.BioSharp.Calculations.Alignment;

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using Model;

/// <summary>
/// Lightweight seed index over a reference sequence.
/// Uses exact k-mer matches to identify candidate alignment windows for a read.
/// </summary>
public sealed class ReferenceIndex
{
    public sealed class IndexOptions
    {
        /// <summary>
        /// Seed size used for exact k-mer lookups.
        /// </summary>
        public int SeedSize { get; set; } = 11;

        /// <summary>
        /// Extra bases to include on both sides of a candidate window.
        /// </summary>
        public int WindowPadding { get; set; } = 64;

        /// <summary>
        /// Maximum number of candidate windows to return per read.
        /// </summary>
        public int MaxCandidateWindowsPerRead { get; set; } = 8;

        /// <summary>
        /// Seeds with more hits than this are ignored as overly repetitive.
        /// </summary>
        public int MaxSeedHitsPerKmer { get; set; } = 64;

        /// <summary>
        /// Step size when sampling seeds from the read.
        /// </summary>
        public int SeedStep { get; set; } = 1;

        /// <summary>
        /// Nearby candidate windows are merged when their bounds are within this distance.
        /// </summary>
        public int MergeDistance { get; set; } = 32;

        /// <summary>
        /// Small references can still use a whole-reference candidate as a compatibility fallback.
        /// </summary>
        public int SmallReferenceFullScanThreshold { get; set; } = 4096;
    }

    public readonly record struct CandidateWindow(int Start, int End, int SeedHits, int PreferredStartOffset);

    private readonly Sequence _reference;
    private readonly IndexOptions _options;
    private readonly Dictionary<string, int[]> _seedPositions;

    public ReferenceIndex(Sequence reference, IndexOptions? options = null)
    {
        _reference = reference ?? throw new ArgumentNullException(nameof(reference));
        _options = options ?? new IndexOptions();
        _seedPositions = BuildSeedPositions();
    }

    public CandidateWindow[] FindCandidateWindows(Sequence read)
    {
        ArgumentNullException.ThrowIfNull(read);

        var readSpan = read.GetData().Span;
        var readLength = readSpan.Length;
        if (readLength == 0 || _reference.Length == 0)
        {
            return [];
        }

        var effectiveSeedSize = Math.Min(_options.SeedSize, Math.Min(readLength, _reference.Length));
        if (effectiveSeedSize <= 0)
        {
            return [];
        }

        var seedVotes = new Dictionary<int, int>();
        var maxOffset = readLength - effectiveSeedSize;
        var seedStep = Math.Max(1, _options.SeedStep);

        // Use a pooled char buffer + alternate span-based lookup to avoid allocating
        // a new string per k-mer in the hot loop (O(readLen/seedStep) iterations per read).
        var kmerPool = ArrayPool<char>.Shared.Rent(effectiveSeedSize);
        try
        {
            var kmerSpan = kmerPool.AsSpan(0, effectiveSeedSize);
            var lookup = _seedPositions.GetAlternateLookup<ReadOnlySpan<char>>();

            for (var readOffset = 0; readOffset <= maxOffset; readOffset += seedStep)
            {
                NormalizeIntoSpan(readSpan, readOffset, effectiveSeedSize, kmerSpan);
                if (!lookup.TryGetValue(kmerSpan, out var positions))
                {
                    continue;
                }

                if (positions.Length > _options.MaxSeedHitsPerKmer)
                {
                    continue;
                }

                foreach (var referencePosition in positions)
                {
                    var candidateStart = referencePosition - readOffset;
                    if (candidateStart < -_options.WindowPadding || candidateStart >= _reference.Length)
                    {
                        continue;
                    }

                    seedVotes[candidateStart] = seedVotes.GetValueOrDefault(candidateStart) + 1;
                }
            }
        }
        finally
        {
            ArrayPool<char>.Shared.Return(kmerPool, clearArray: false);
        }

        if (seedVotes.Count == 0)
        {
            return CreateFallbackWindows(readLength);
        }

        var rankedStarts = seedVotes
            .OrderByDescending(entry => entry.Value)
            .ThenBy(entry => entry.Key)
            .ToArray();

        var windows = new List<CandidateWindow>(_options.MaxCandidateWindowsPerRead);
        foreach (var rankedStart in rankedStarts)
        {
            var start = Math.Max(0, rankedStart.Key - _options.WindowPadding);
            var end = Math.Min(_reference.Length, rankedStart.Key + readLength + _options.WindowPadding);
            if (end <= start)
            {
                continue;
            }

            var merged = false;
            for (var index = 0; index < windows.Count; index++)
            {
                if (!CanMerge(windows[index], start, end))
                {
                    continue;
                }

                var existing = windows[index];
                windows[index] = new CandidateWindow(
                    Math.Min(existing.Start, start),
                    Math.Max(existing.End, end),
                    existing.SeedHits + rankedStart.Value,
                    existing.PreferredStartOffset);
                merged = true;
                break;
            }

            if (!merged)
            {
                windows.Add(new CandidateWindow(start, end, rankedStart.Value, rankedStart.Key - start));
            }

            if (windows.Count >= _options.MaxCandidateWindowsPerRead)
            {
                break;
            }
        }

        return windows
            .OrderByDescending(window => window.SeedHits)
            .ThenBy(window => window.Start)
            .Take(_options.MaxCandidateWindowsPerRead)
            .ToArray();
    }

    private Dictionary<string, int[]> BuildSeedPositions()
    {
        var seedSize = Math.Min(_options.SeedSize, _reference.Length);
        if (seedSize <= 0)
        {
            return new Dictionary<string, int[]>();
        }

        var positions = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        var referenceSpan = _reference.GetData().Span;

        for (var index = 0; index <= _reference.Length - seedSize; index++)
        {
            var kmer = CreateNormalizedKmer(referenceSpan, index, seedSize);
            if (!positions.TryGetValue(kmer, out var seedHits))
            {
                seedHits = new List<int>();
                positions[kmer] = seedHits;
            }

            seedHits.Add(index);
        }

        return positions.ToDictionary(entry => entry.Key, entry => entry.Value.ToArray(), StringComparer.Ordinal);
    }

    private CandidateWindow[] CreateFallbackWindows(int readLength)
    {
        if (_reference.Length > _options.SmallReferenceFullScanThreshold)
        {
            return [];
        }

        var end = Math.Min(_reference.Length, Math.Max(readLength + _options.WindowPadding * 2, readLength));
        return [new CandidateWindow(0, end, 0, 0)];
    }

    private bool CanMerge(CandidateWindow window, int start, int end)
    {
        return start <= window.End + _options.MergeDistance && end >= window.Start - _options.MergeDistance;
    }

    private static void NormalizeIntoSpan(ReadOnlySpan<char> source, int start, int length, Span<char> dest)
    {
        for (var i = 0; i < length; i++)
        {
            dest[i] = char.ToUpperInvariant(source[start + i]);
        }
    }

    private static string CreateNormalizedKmer(ReadOnlySpan<char> data, int start, int length)
    {
        var buffer = new char[length];
        for (var index = 0; index < length; index++)
        {
            buffer[index] = char.ToUpperInvariant(data[start + index]);
        }

        return new string(buffer);
    }
}