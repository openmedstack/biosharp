namespace OpenMedStack.BioSharp.Calculations.Alignment;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Model;

/// <summary>
/// Lightweight seed index over a reference sequence.
/// Uses exact k-mer matches to identify candidate alignment windows for a read.
/// </summary>
public sealed class ReferenceIndex
{
    private const uint FormatMagic = 0x31595342; // BSY1
    private const int MaxEncodableSeedSize = 64;

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

    public readonly record struct OptionsKey(
        int SeedSize,
        int WindowPadding,
        int MaxCandidateWindowsPerRead,
        int MaxSeedHitsPerKmer,
        int SeedStep,
        int MergeDistance,
        int SmallReferenceFullScanThreshold);

    public readonly record struct CandidateWindow(int Start, int End, int SeedHits, int PreferredStartOffset);

    private readonly Sequence _reference;
    private readonly IndexOptions _options;
    private readonly Dictionary<UInt128, int[]> _seedPositions;
    private readonly ulong _referenceFingerprint;

    public ReferenceIndex(Sequence reference, IndexOptions? options = null)
        : this(reference, CloneOptions(options), ComputeReferenceFingerprint(reference))
    {
    }

    private ReferenceIndex(Sequence reference, IndexOptions options, ulong referenceFingerprint)
    {
        _reference = reference ?? throw new ArgumentNullException(nameof(reference));
        _options = CloneOptions(options);
        ValidateOptions(_options);
        _referenceFingerprint = referenceFingerprint;
        _seedPositions = BuildSeedPositions();
    }

    private ReferenceIndex(
        Sequence reference,
        IndexOptions options,
        Dictionary<UInt128, int[]> seedPositions,
        ulong referenceFingerprint)
    {
        _reference = reference ?? throw new ArgumentNullException(nameof(reference));
        _options = CloneOptions(options);
        ValidateOptions(_options);
        _seedPositions = seedPositions ?? throw new ArgumentNullException(nameof(seedPositions));
        _referenceFingerprint = referenceFingerprint;
    }

    public string ReferenceId => _reference.Id;

    public int ReferenceLength => _reference.Length;

    public ulong ReferenceFingerprint => _referenceFingerprint;

    public IndexOptions Options => CloneOptions(_options);

    public OptionsKey Key => CreateKey(_options);

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
        var seedStep = Math.Max(1, _options.SeedStep);
        var mask = CreateKmerMask(effectiveSeedSize);
        UInt128 encodedKmer = 0;
        var validBaseCount = 0;

        for (var index = 0; index < readLength; index++)
        {
            if (!TryEncodeBase(readSpan[index], out var encodedBase))
            {
                encodedKmer = 0;
                validBaseCount = 0;
                continue;
            }

            encodedKmer = ((encodedKmer << 2) | encodedBase) & mask;
            validBaseCount = Math.Min(validBaseCount + 1, effectiveSeedSize);
            if (validBaseCount < effectiveSeedSize)
            {
                continue;
            }

            var readOffset = index - effectiveSeedSize + 1;
            if (readOffset % seedStep != 0 || !_seedPositions.TryGetValue(encodedKmer, out var positions))
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

    public void Save(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        using var stream = File.Create(path);
        Save(stream);
    }

    public void Save(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(FormatMagic);
        writer.Write(ReferenceId);
        writer.Write(ReferenceLength);
        writer.Write(_referenceFingerprint);
        writer.Write(_options.SeedSize);
        writer.Write(_options.WindowPadding);
        writer.Write(_options.MaxCandidateWindowsPerRead);
        writer.Write(_options.MaxSeedHitsPerKmer);
        writer.Write(_options.SeedStep);
        writer.Write(_options.MergeDistance);
        writer.Write(_options.SmallReferenceFullScanThreshold);
        writer.Write(_seedPositions.Count);

        foreach (var entry in _seedPositions.OrderBy(entry => entry.Key))
        {
            writer.Write((ulong)(entry.Key >> 64));
            writer.Write((ulong)entry.Key);
            writer.Write(entry.Value.Length);
            foreach (var position in entry.Value)
            {
                writer.Write(position);
            }
        }
    }

    public static ReferenceIndex Load(
        Sequence reference,
        string path,
        IndexOptions? expectedOptions = null,
        ulong? expectedReferenceFingerprint = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        using var stream = File.OpenRead(path);
        return Load(reference, stream, expectedOptions, expectedReferenceFingerprint);
    }

    public static ReferenceIndex Load(
        Sequence reference,
        Stream stream,
        IndexOptions? expectedOptions = null,
        ulong? expectedReferenceFingerprint = null)
    {
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentNullException.ThrowIfNull(stream);

        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        var formatMagic = reader.ReadUInt32();
        if (formatMagic != FormatMagic)
        {
            throw new InvalidDataException("The provided stream is not a BioSharp reference index.");
        }

        var serializedReferenceId = reader.ReadString();
        var serializedReferenceLength = reader.ReadInt32();
        var serializedFingerprint = reader.ReadUInt64();
        var options = new IndexOptions
        {
            SeedSize = reader.ReadInt32(),
            WindowPadding = reader.ReadInt32(),
            MaxCandidateWindowsPerRead = reader.ReadInt32(),
            MaxSeedHitsPerKmer = reader.ReadInt32(),
            SeedStep = reader.ReadInt32(),
            MergeDistance = reader.ReadInt32(),
            SmallReferenceFullScanThreshold = reader.ReadInt32()
        };

        var computedFingerprint = expectedReferenceFingerprint ?? ComputeReferenceFingerprint(reference);
        if (serializedReferenceLength != reference.Length ||
            serializedFingerprint != computedFingerprint)
        {
            throw new InvalidDataException(
                $"The serialized index targets reference '{serializedReferenceId}' ({serializedReferenceLength} bp), but the provided reference '{reference.Id}' does not match.");
        }

        if (expectedOptions != null && CreateKey(options) != CreateKey(expectedOptions))
        {
            throw new InvalidDataException("The serialized index options do not match the expected pipeline index options.");
        }

        var seedPositionCount = reader.ReadInt32();
        var seedPositions = new Dictionary<UInt128, int[]>(seedPositionCount);
        for (var index = 0; index < seedPositionCount; index++)
        {
            var high = reader.ReadUInt64();
            var low = reader.ReadUInt64();
            var positionCount = reader.ReadInt32();
            var positions = new int[positionCount];
            for (var positionIndex = 0; positionIndex < positionCount; positionIndex++)
            {
                positions[positionIndex] = reader.ReadInt32();
            }

            seedPositions[((UInt128)high << 64) | low] = positions;
        }

        return new ReferenceIndex(reference, options, seedPositions, computedFingerprint);
    }

    public static OptionsKey CreateKey(IndexOptions? options)
    {
        var normalized = CloneOptions(options);
        return new OptionsKey(
            normalized.SeedSize,
            normalized.WindowPadding,
            normalized.MaxCandidateWindowsPerRead,
            normalized.MaxSeedHitsPerKmer,
            normalized.SeedStep,
            normalized.MergeDistance,
            normalized.SmallReferenceFullScanThreshold);
    }

    public static IndexOptions CloneOptions(IndexOptions? options)
    {
        options ??= new IndexOptions();
        return new IndexOptions
        {
            SeedSize = options.SeedSize,
            WindowPadding = options.WindowPadding,
            MaxCandidateWindowsPerRead = options.MaxCandidateWindowsPerRead,
            MaxSeedHitsPerKmer = options.MaxSeedHitsPerKmer,
            SeedStep = options.SeedStep,
            MergeDistance = options.MergeDistance,
            SmallReferenceFullScanThreshold = options.SmallReferenceFullScanThreshold
        };
    }

    public static ulong ComputeReferenceFingerprint(Sequence reference)
    {
        ArgumentNullException.ThrowIfNull(reference);

        const ulong offsetBasis = 14695981039346656037;
        const ulong prime = 1099511628211;
        var hash = offsetBasis;
        foreach (var baseChar in reference.GetData().Span)
        {
            hash ^= char.ToUpperInvariant(baseChar);
            hash *= prime;
        }

        return hash;
    }

    private Dictionary<UInt128, int[]> BuildSeedPositions()
    {
        var seedSize = Math.Min(_options.SeedSize, _reference.Length);
        if (seedSize <= 0)
        {
            return new Dictionary<UInt128, int[]>();
        }

        var positions = new Dictionary<UInt128, List<int>>();
        var referenceSpan = _reference.GetData().Span;
        var mask = CreateKmerMask(seedSize);
        UInt128 encodedKmer = 0;
        var validBaseCount = 0;

        for (var index = 0; index < _reference.Length; index++)
        {
            if (!TryEncodeBase(referenceSpan[index], out var encodedBase))
            {
                encodedKmer = 0;
                validBaseCount = 0;
                continue;
            }

            encodedKmer = ((encodedKmer << 2) | encodedBase) & mask;
            validBaseCount = Math.Min(validBaseCount + 1, seedSize);
            if (validBaseCount < seedSize)
            {
                continue;
            }

            if (!positions.TryGetValue(encodedKmer, out var seedHits))
            {
                seedHits = [];
                positions[encodedKmer] = seedHits;
            }

            seedHits.Add(index - seedSize + 1);
        }

        return positions.ToDictionary(entry => entry.Key, entry => entry.Value.ToArray());
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

    private static void ValidateOptions(IndexOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.SeedSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "SeedSize must be greater than zero.");
        }

        if (options.SeedSize > MaxEncodableSeedSize)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                $"SeedSize must be {MaxEncodableSeedSize} or less for compact encoded indexing.");
        }
    }

    private static UInt128 CreateKmerMask(int seedSize)
    {
        return seedSize >= MaxEncodableSeedSize
            ? UInt128.MaxValue
            : (((UInt128)1) << (seedSize * 2)) - 1;
    }

    private static bool TryEncodeBase(char value, out UInt128 encodedBase)
    {
        switch (char.ToUpperInvariant(value))
        {
            case 'A':
                encodedBase = 0;
                return true;
            case 'C':
                encodedBase = 1;
                return true;
            case 'G':
                encodedBase = 2;
                return true;
            case 'T':
            case 'U':
                encodedBase = 3;
                return true;
            default:
                encodedBase = 0;
                return false;
        }
    }
}
