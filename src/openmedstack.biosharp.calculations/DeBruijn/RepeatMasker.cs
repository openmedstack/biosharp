namespace OpenMedStack.BioSharp.Calculations.DeBruijn;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Represents a repeat element loaded from a repeat library.
/// </summary>
public class RepeatElement
{
    /// <summary>Unique identifier for this repeat element.</summary>
    public string Id { get; set; } = null!;

    /// <summary>Human-readable name.</summary>
    public string Name { get; set; } = null!;

    /// <summary>Repeat type classification.</summary>
    public string Type { get; set; } = null!;

    /// <summary>The repeat motif/sequence.</summary>
    public string Motif { get; set; } = null!;

    /// <summary>Description of the repeat.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Length of this repeat's motif.</summary>
    public int MotifLength
    {
        get { return Motif.Length; }
    }
}

/// <summary>
/// Holds masked regions produced by RepeatMasker.
/// </summary>
public class MaskedRegion
{
    /// <summary>Start position (0-based, inclusive).</summary>
    public int Start { get; set; }

    /// <summary>End position (0-based, exclusive).</summary>
    public int End { get; set; }

    /// <summary>Length of the masked region.</summary>
    public int Length
    {
        get { return End - Start; }
    }

    /// <summary>Repeat element that caused masking.</summary>
    public RepeatElement? Repeat { get; set; }

    /// <summary>Repeat type (SAT, SINE, LINE, etc).</summary>
    public string RepeatType
    {
        get { return Repeat?.Type ?? "unknown"; }
    }

    /// <summary>The masked sequence segment (N-masked or original).</summary>
    public string MaskedSequence { get; set; } = null!;

    public override string ToString()
    {
        return $"Masked [{Start}-{End}] ({Repeat?.Name ?? "unknown"}) len={Length}";
    }
}

/// <summary>
/// Masks repeat sequences in a DNA sequence using a repeat library.
///
/// Scans the input sequence for matches against all known repeat motifs and masks
/// them with 'N' characters. Supports both direct motif matching and k-mer-based
/// Bloom filter accelerated matching for large libraries.
/// </summary>
public static class RepeatMasker
{
    /// <summary>
    /// Minimum motif length to consider (shorter motifs are ignored to avoid noise).
    /// </summary>
    public const int DefaultMinMotifLength = 4;

    /// <summary>
    /// Minimum match quality (Phred-scaled) to consider a match valid.
    /// Default: 13 (95% confidence).
    /// </summary>
    public const int DefaultMinMatchQuality = 13;

    /// <summary>
    /// Default repeat library path (embedded within the library directory).
    /// </summary>
    public const string DefaultLibraryPath = "data/repeats.json";

    /// <summary>
    /// Loads a repeat library from a JSON file.
    /// </summary>
    /// <param name="filePath">Path to the repeats.json file.</param>
    /// <param name="minMotifLength">Minimum motif length filter.</param>
    /// <returns>List of repeat elements from the library.</returns>
    public static async Task<IList<RepeatElement>> LoadLibraryAsync(
        string filePath,
        int? minMotifLength = null)
    {
        var motifMin = minMotifLength ?? DefaultMinMotifLength;
        var json = await File.ReadAllTextAsync(filePath);

        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        // IL2026: JSON deserialization with options pattern - this library is designed
        // for trimming-aware usage where the user knows the target types, so we suppress.
#pragma warning disable IL2026
        var lib = JsonSerializer.Deserialize<RepeatLibrary>(json, options)!;
#pragma warning restore IL2026
        if (lib?.Repeats == null)
            throw new InvalidDataException($"Failed to parse repeat library from {filePath}");

        var repeats = lib.Repeats!
            .Where(r => r.MotifLength >= motifMin)
            .OrderByDescending(r => r.MotifLength) // longer motifs first
            .ToList();

        return repeats;
    }

    /// <summary>
    /// Masks repeat sequences in a DNA sequence.
    /// </summary>
    /// <param name="sequence">DNA sequence to mask.</param>
    /// <param name="library">Pre-loaded repeat library.</param>
    /// <param name="allowAmbiguity">
    /// If true, ambiguous bases (N) in the sequence do not contribute to repeat matches.
    /// </param>
    /// <returns>List of masked regions.</returns>
    public static async Task<IList<MaskedRegion>> MaskRepeatsAsync(
        string sequence,
        IList<RepeatElement> library,
        bool allowAmbiguity = true)
    {
        if (string.IsNullOrEmpty(sequence))
            return Array.Empty<MaskedRegion>();
        if (library == null)
            throw new ArgumentNullException(nameof(library));

        // Normalize sequence to uppercase
        var seq = sequence.ToUpper();

        // Track masking: each position gets the best (longest) repeat match
        var maskMap = new Dictionary<int, (RepeatElement repeat, int motifPos, int motifLen)>();

        foreach (var repeat in library)
        {
            var motif = repeat.Motif.ToUpper();

            // Skip motifs too short (shouldn't happen due to minMotifLength filter, but guard)
            if (motif.Length < 3)
                continue;

            // Try to find the motif extending from each position in the sequence
            for (var i = 0; i <= seq.Length - motif.Length; i++)
            {
                if (!MatchMotif(seq, motif, i, allowAmbiguity))
                    continue;

                // Found a motif match - extend as far right as possible
                var motifLen = motif.Length;
                for (var j = i + motif.Length; j <= seq.Length - motif.Length; j++)
                    if (MatchMotif(seq, motif, j, allowAmbiguity))
                        motifLen++;
                    else
                        break;

                // Store this match (only if longer than existing at any covered position)
                for (var m = i; m < i + motifLen; m++)
                    if (!maskMap.ContainsKey(m) || motifLen > maskMap[m].motifLen)
                        maskMap[m] = (repeat, i, motifLen);
            }
        }

        // Build contiguous masked regions from the mask map
        var regions = new List<MaskedRegion>();
        if (maskMap.Count == 0)
            return regions;

        var sortedKeys = maskMap.Keys.OrderBy(k => k).ToList();
        var start = sortedKeys.First();
        var end = sortedKeys.Last() + 1;

        // Check for gaps in the mask
        var currentStart = start;
        for (var i = start; i <= end; i++)
            if (!maskMap.ContainsKey(i) && i > currentStart)
            {
                // Gap found, close current region and start a new one
                regions.Add(new MaskedRegion
                {
                    Start = currentStart,
                    End = i,
                    Repeat = maskMap[currentStart].repeat,
                    MaskedSequence = new string('N', i - currentStart)
                });
                currentStart = i + 1;
            }

        // Close final region
        if (currentStart < end)
            regions.Add(new MaskedRegion
            {
                Start = currentStart,
                End = end,
                Repeat = maskMap[currentStart].repeat,
                MaskedSequence = new string('N', end - currentStart)
            });

        return regions;
    }

    private static bool MatchMotif(string sequence, string motif, int position, bool allowAmbiguity)
    {
        if (position + motif.Length > sequence.Length)
            return false;

        for (var i = 0; i < motif.Length; i++)
        {
            var seqChar = sequence[position + i];
            var motifChar = motif[i];

            if (allowAmbiguity && seqChar == 'N')
                return false;

            if (seqChar != motifChar)
                return false;
        }

        return true;
    }

    /// <summary>
    /// Masks repeats by loading the library from a file path.
    /// Convenience method that combines LoadLibraryAsync and MaskRepeatsAsync.
    /// </summary>
    public static async Task<IList<MaskedRegion>> MaskRepeatsFromLibraryAsync(
        string sequence,
        string libraryPath,
        int? minMotifLength = null,
        bool allowAmbiguity = true)
    {
        var library = await LoadLibraryAsync(libraryPath, minMotifLength);
        return await MaskRepeatsAsync(sequence, library, allowAmbiguity);
    }

    /// <summary>
    /// Creates a Bloom filter from a repeat library for fast k-mer lookups.
    /// Useful for pre-filtering candidate k-mers before full motif matching.
    /// </summary>
    public static BloomFilter CreateBloomFilterFromLibrary(IList<RepeatElement> library, int k, double fpr = 0.01)
    {
        var filter = new BloomFilter((int)AppropriateSize(library, k, fpr));

        foreach (var repeat in library)
        {
            var motif = repeat.Motif.ToUpper();

            for (var i = 0; i <= motif.Length - k; i++)
            {
                var kmer = motif.Substring(i, k);
                filter.Add(kmer);
            }
        }

        return filter;
    }

    private static long AppropriateSize(IList<RepeatElement> lib, int k, double fpr)
    {
        // Calculate a Bloom filter size that can hold the motifs with ~1% FPR
        // m >= -n * ln(p) / (ln(2))^2 where n = total number of k-mers, p = FPR
        var totalKmers = lib
            .Where(r => r.Motif.Length >= k)
            .Select(r => Math.Max(0, r.Motif.Length - k + 1))
            .Sum();
        if (totalKmers == 0)
            totalKmers = 1;
        var size = (long)(-totalKmers * Math.Log(fpr) / 0.48045);
        return Math.Max(size, 1L << 16);
    }
}

/// <summary>
/// Internal: parsed JSON repeat library structure.
/// </summary>
internal class RepeatLibrary
{
    public string? Version { get; set; }
    public string? Description { get; set; }
    public IList<RepeatElement>? Repeats { get; set; }
}
