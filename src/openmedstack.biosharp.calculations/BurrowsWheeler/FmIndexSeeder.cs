namespace OpenMedStack.BioSharp.Calculations.BurrowsWheeler;

using System;
using System.Collections.Generic;
using Alignment;
using Model;

/// <summary>
/// FM-index based reference seeder — a drop-in replacement for
/// <see cref="Alignment.ReferenceIndex"/> that uses the Burrows-Wheeler
/// Transform for O(m × SR) candidate-window discovery instead of a
/// pre-built hash map.
///
/// Advantages over the hash-map seeder
/// ─────────────────────────────────────
/// • Scales to whole-genome references with O(n/SR) memory instead of
///   O(n) hash-table buckets.
/// • Supports both fixed-length seeds (<see cref="FmIndex.FindExactSeeds"/>)
///   and variable-length MEM seeds (<see cref="FmIndex.FindMemSeeds"/>),
///   the latter being equivalent to the BWA-MEM seeding strategy.
/// • No pre-built hash collisions — every lookup is an exact BWT backward
///   search, giving zero false seed hits.
///
/// Integration
/// ───────────
/// Assign an instance to <see cref="VariantCallingPipeline.Seeder"/> before
/// calling <c>LoadFastQ</c> or <c>LoadBam</c>.
/// </summary>
public sealed class FmIndexSeeder : IReferenceSeeder
{
    /// <summary>Configuration for <see cref="FmIndexSeeder"/>.</summary>
    public sealed class Options
    {
        /// <summary>
        /// FM-index row-sampling rate (default 64).
        /// Higher values reduce memory at the cost of slower rank/locate.
        /// </summary>
        public int FmSampleRate { get; set; } = 64;

        /// <summary>
        /// Minimum seed length (default 19 bp).
        /// Longer seeds are more specific and produce fewer spurious windows.
        /// </summary>
        public int MinSeedLen { get; set; } = 19;

        /// <summary>
        /// Step between sampled seed start positions in the read (default 1).
        /// Increasing this trades sensitivity for speed.
        /// </summary>
        public int SeedStep { get; set; } = 1;

        /// <summary>
        /// Seeds matching more than this many reference locations are
        /// discarded as repetitive (default 64, same as ReferenceIndex).
        /// </summary>
        public int MaxSeedHits { get; set; } = 64;

        /// <summary>
        /// Extra bases added on both sides of each candidate window (default 64).
        /// </summary>
        public int WindowPadding { get; set; } = 64;

        /// <summary>
        /// Maximum candidate windows returned per read (default 8).
        /// </summary>
        public int MaxCandidateWindowsPerRead { get; set; } = 8;

        /// <summary>
        /// When true, use variable-length MEM seeding (BWA-MEM style).
        /// When false, use fixed-length exact seeds (BWA-aln style).
        /// Default: true.
        /// </summary>
        public bool UseMemSeeds { get; set; } = true;

        /// <summary>
        /// Distance within which two adjacent candidate windows are merged
        /// into a single wider window (default 32 bp).
        /// </summary>
        public int MergeDistance { get; set; } = 32;
    }

    private readonly FmIndex _index;
    private readonly Options _options;
    private readonly Sequence _reference;

    /// <inheritdoc/>
    public string ReferenceId => _reference.Id;

    /// <inheritdoc/>
    public int ReferenceLength => _reference.Length;

    /// <summary>Exposes the underlying FM-index for advanced consumers.</summary>
    public FmIndex Index => _index;

    // ── Construction ─────────────────────────────────────────────────────────

    /// <summary>
    /// Builds an FM-index seeder over <paramref name="reference"/>.
    ///
    /// Building takes O(n log n) time and O(n/sampleRate) additional memory
    /// for the sampled Occ/SA tables.
    /// </summary>
    public FmIndexSeeder(Sequence reference, Options? options = null)
    {
        ArgumentNullException.ThrowIfNull(reference);
        _reference = reference;
        _options   = Clone(options);
        _index     = FmIndex.Build(reference.GetData().Span, _options.FmSampleRate);
    }

    private FmIndexSeeder(Sequence reference, Options options, FmIndex index)
    {
        _reference = reference;
        _options   = Clone(options);
        _index     = index;
    }

    // ── Core seeding ─────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public ReferenceIndex.CandidateWindow[] FindCandidateWindows(Sequence read)
    {
        ArgumentNullException.ThrowIfNull(read);

        var readSpan  = read.GetData().Span;
        var readLen   = readSpan.Length;
        if (readLen == 0 || _reference.Length == 0)
        {
            return [];
        }

        // Find seeds using the configured strategy
        IReadOnlyList<FmSeed> seeds = _options.UseMemSeeds
            ? _index.FindMemSeeds(readSpan, _options.MinSeedLen, _options.MaxSeedHits)
            : _index.FindExactSeeds(readSpan, _options.MinSeedLen, _options.SeedStep, _options.MaxSeedHits);

        if (seeds.Count == 0)
        {
            return [];
        }

        // Convert seeds → candidate windows, using the same merging logic
        // as ReferenceIndex to stay compatible with the rest of the pipeline.
        var votes = new Dictionary<int, (int Hits, int PreferredOffset)>();

        foreach (var seed in seeds)
        {
            var queryOffset = seed.QueryStart;
            foreach (var refPos in seed.ReferencePositions)
            {
                // Projected start of the read in the reference
                var candidateStart = refPos - queryOffset;
                if (!votes.TryGetValue(candidateStart, out var existing))
                {
                    votes[candidateStart] = (1, candidateStart);
                }
                else
                {
                    votes[candidateStart] = (existing.Hits + 1, existing.PreferredOffset);
                }
            }
        }

        if (votes.Count == 0)
        {
            return [];
        }

        // Sort by hit count descending, then by position
        var ranked = new List<(int Start, int Hits, int PrefOffset)>(votes.Count);
        foreach (var (start, (hits, pref)) in votes)
            ranked.Add((start, hits, pref));
        ranked.Sort((a, b) => b.Hits != a.Hits ? b.Hits.CompareTo(a.Hits) : a.Start.CompareTo(b.Start));

        // Build and merge candidate windows
        var windows = new List<ReferenceIndex.CandidateWindow>(_options.MaxCandidateWindowsPerRead);
        var refLen  = _reference.Length;

        foreach (var (cStart, hits, prefOffset) in ranked)
        {
            var wStart = Math.Max(0, cStart - _options.WindowPadding);
            var wEnd   = Math.Min(refLen, cStart + readLen + _options.WindowPadding);
            if (wEnd <= wStart)
            {
                continue;
            }

            var merged = false;
            for (var wi = 0; wi < windows.Count; wi++)
            {
                var w = windows[wi];
                if (wStart <= w.End + _options.MergeDistance &&
                    wEnd   >= w.Start - _options.MergeDistance)
                {
                    windows[wi] = new ReferenceIndex.CandidateWindow(
                        Math.Min(w.Start, wStart),
                        Math.Max(w.End,   wEnd),
                        w.SeedHits + hits,
                        w.PreferredStartOffset);
                    merged = true;
                    break;
                }
            }

            if (!merged)
            {
                windows.Add(new ReferenceIndex.CandidateWindow(wStart, wEnd, hits, prefOffset - wStart));
            }

            if (windows.Count >= _options.MaxCandidateWindowsPerRead)
            {
                break;
            }
        }

        // Return top-N by seed hits
        windows.Sort((a, b) => b.SeedHits != a.SeedHits
            ? b.SeedHits.CompareTo(a.SeedHits)
            : a.Start.CompareTo(b.Start));

        var count = Math.Min(windows.Count, _options.MaxCandidateWindowsPerRead);
        return windows.GetRange(0, count).ToArray();
    }

    // ── Serialisation ─────────────────────────────────────────────────────────

    /// <summary>Saves the FM-index to <paramref name="path"/>.</summary>
    public void Save(string path) => _index.Save(path);

    /// <summary>
    /// Loads an FM-index seeder from a previously saved file.
    /// The <paramref name="reference"/> must be the same sequence used when
    /// saving (no fingerprint check is enforced — the caller is responsible).
    /// </summary>
    public static FmIndexSeeder Load(Sequence reference, string path, Options? options = null)
    {
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var idx = FmIndex.Load(path);
        return new FmIndexSeeder(reference, Clone(options), idx);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static Options Clone(Options? src)
    {
        src ??= new Options();
        return new Options
        {
            FmSampleRate              = src.FmSampleRate,
            MinSeedLen                = src.MinSeedLen,
            SeedStep                  = src.SeedStep,
            MaxSeedHits               = src.MaxSeedHits,
            WindowPadding             = src.WindowPadding,
            MaxCandidateWindowsPerRead = src.MaxCandidateWindowsPerRead,
            UseMemSeeds               = src.UseMemSeeds,
            MergeDistance             = src.MergeDistance,
        };
    }
}

