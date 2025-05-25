namespace OpenMedStack.BioSharp.Calculations.DeBruijn;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

/// <summary>
/// Bloom filter for probabilistic membership testing of k-mers.
/// Uses two hash functions applied to a base-4 interpreted value for
/// efficient in-memory k-mer presence checks with sub-linear memory usage.
///
/// Parameters:
///   n = expected number of insertions
///   p = expected false positive rate (FPR)
///   m = bit array size = ceil(-n * ln(p) / ln(2)^2)
///   k = number of hash functions = round((m/n) * ln(2))
/// </summary>
public sealed class BloomFilter
{
    private int _numHashFunctions; // non-readonly so Union/Intersection can override
    private ulong _bitCount;
    private ulong[] _bits;
    private readonly ulong _expectedInsertions;
    private readonly double _targetFpr;
    private long _m; // store for reference in Union
    private ulong _actualInsertions = 0;

    /// <summary>
    /// Creates a Bloom filter with the given expected capacity and false-positive target.
    /// </summary>
    /// <param name="expectedInsertions">Expected number of k-mers to add.</param>
    /// <param name="targetFpr">Target false-positive rate (0 < p < 1). Default 0.01 (1%).</param>
    public BloomFilter(int expectedInsertions, double targetFpr = 0.01)
    {
        if (expectedInsertions <= 0)
            throw new ArgumentOutOfRangeException(nameof(expectedInsertions), "Expected insertions must be positive.");

        if (targetFpr <= 0.0 || targetFpr >= 1.0)
            throw new ArgumentOutOfRangeException(nameof(targetFpr), "Expected FPR must be in (0, 1).");

        _targetFpr = targetFpr;
        _expectedInsertions = (ulong)expectedInsertions;

        // m = -n * ln(p) / (ln(2)^2)
        var ln2 = Math.Log(2.0);
        _m = (long)Math.Ceiling(-expectedInsertions * Math.Log(targetFpr) / (ln2 * ln2));
        // At least 64 bits
        _m = Math.Max(_m, 64);

        _bitCount = (ulong)((int)_m / 64 + ((int)_m % 64 > 0 ? 1 : 0));
        _bits = new ulong[_bitCount];
        _numHashFunctions = Math.Max(1, (int)Math.Round(_m / Math.Max((long)expectedInsertions, 1) * ln2));
    }

    /// <summary>
    /// Private constructor for reuse in set operations.
    /// </summary>
    private BloomFilter(long m, int bitCount, ulong[] bits, int numHashFunctions)
    {
        _m = m;
        _bitCount = (ulong)bitCount;
        _bits = bits;
        _numHashFunctions = numHashFunctions;
        _targetFpr = 0.01;
        _expectedInsertions = 1;
        _actualInsertions = 0;
    }

    /// <summary>The target false-positive rate configured in the constructor.</summary>
    public double TargetFpr
    {
        get { return _targetFpr; }
    }

    /// <summary>The number of bits in the filter (m * 64).</summary>
    public ulong TotalBits
    {
        get { return _bitCount * 64; }
    }

    /// <summary>Returns the target false-positive rate configured in the constructor.</summary>
    public double FalsePositiveRate
    {
        get { return _targetFpr; }
    }

    /// <summary>The number of hash functions used.</summary>
    public int NumHashFunctions
    {
        get { return _numHashFunctions; }
    }

    /// <summary>Approximate number of distinct items inserted so far.</summary>
    public ulong ActualInsertions
    {
        get { return _actualInsertions; }
    }

    /// <summary>Approximate number of items expected.</summary>
    public ulong ExpectedCount
    {
        get { return _expectedInsertions; }
    }

    /// <summary>
    /// Returns an estimate of the current false-positive rate based on fill ratio.
    /// Formula: (1 - e^(-kn/m))^k
    /// </summary>
    public double EstimatedFprValue
    {
        get
        {
            var m = TotalBits;
            var n = ActualInsertions;
            if (n == 0 || m == 0) return 0.0;
            var ratio = Math.Pow(1.0 - Math.Exp(-(double)_numHashFunctions * n / m), _numHashFunctions);
            return ratio;
        }
    }

    /// <summary>
    /// Adds a k-mer string to the filter.
    /// </summary>
    public void Add(string kmer)
    {
        if (string.IsNullOrEmpty(kmer))
            throw new ArgumentException("K-mer must be non-empty.", nameof(kmer));

        var (h1, h2) = Hash(kmer);
        for (var i = 0; i < _numHashFunctions; i++)
        {
            var h = h1 + (ulong)i * h2;
            var pos = h % TotalBits; // Map 64-bit hash to [0, TotalBits)
            var word = (int)(pos / 64);
            var bit = (int)(pos % 64);
            var mask = 1UL << bit;

            if ((_bits[word] & mask) == 0)
            {
                _bits[word] |= mask;
                _actualInsertions++;
            }
        }
    }

    /// <summary>
    /// Adds multiple k-mers to the filter.
    /// </summary>
    public void Add(IEnumerable<string> kmers)
    {
        if (kmers == null) throw new ArgumentNullException(nameof(kmers));
        foreach (var kmer in kmers) Add(kmer);
    }

    /// <summary>
    /// Checks if a k-mer is likely present in the filter.
    /// Returns true if the k-mer was possibly added (may be a false positive).
    /// Returns false if the k-mer was definitely NOT added.
    /// </summary>
    public bool Contains(string kmer)
    {
        if (string.IsNullOrEmpty(kmer)) return false;

        var (h1, h2) = Hash(kmer);

        for (var i = 0; i < _numHashFunctions; i++)
        {
            var h = h1 + (ulong)i * h2;
            var pos = h % TotalBits; // Map 64-bit hash to [0, TotalBits)
            var word = (int)(pos / 64);
            var bit = (int)(pos % 64);
            var mask = 1UL << bit;

            if ((_bits[word] & mask) == 0)
                return false;
        }

        return true;
    }

    /// <summary>
    /// Resets the filter to empty state, preserving capacity.
    /// </summary>
    public void Clear()
    {
        Array.Clear(_bits, 0, _bits.Length);
        _actualInsertions = 0;
    }

    /// <summary>
    /// Merges another Bloom filter into this one (bitwise OR).
    /// Useful for multi-sample union operations.
    /// The other filter must have the same m and k parameters.
    /// </summary>
    public void Union(BloomFilter other)
    {
        if (other == null) throw new ArgumentNullException(nameof(other));
        if (other._bitCount != _bitCount)
            throw new ArgumentException("Bloom filter bit array sizes must match for union.");

        for (var i = 0; i < _bits.Length; i++)
            _bits[i] |= other._bits[i];

        // Update actual insertions estimate
        _actualInsertions = EstimateInsertionsFromFill();
    }

    /// <summary>
    /// Estimates the number of unique insertions from current fill ratio.
    /// </summary>
    private ulong EstimateInsertionsFromFill()
    {
        var setBits = CountSetBits();
        var m = TotalBits;
        if (setBits >= m) return _expectedInsertions;
        return (ulong)Math.Round((double)m / _numHashFunctions * Math.Log((double)m / (m - setBits)));
    }

    /// <summary>
    /// Counts the number of set bits in the bit array.
    /// </summary>
    private ulong CountSetBits()
    {
        ulong count = 0;
        for (var i = 0; i < _bits.Length; i++)
            count += (ulong)System.Numerics.BitOperations.PopCount(_bits[i]);
        return count;
    }

    /// <summary>PopCount with fallback.</summary>
    private static ulong PopCount(ulong value)
    {
        return (ulong)System.Numerics.BitOperations.PopCount(value);
    }

    /// <summary>
    /// Two hash functions based on Rook's method:
    /// h1 = fnv-1a of the string
    /// h2 = fnv-1a of reversed string
    /// Final hash: h = h1 + i * h2 (mod 2^64)
    /// </summary>
    private static (ulong, ulong) Hash(string s)
    {
        // FNV-1a 64-bit
        var h1 = 14695981039346656037UL;
        var h2 = 14695981039346656037UL;

        foreach (var c in s)
        {
            h1 ^= c;
            h1 *= 1099511628211UL;
        }

        for (var i = s.Length - 1; i >= 0; i--)
        {
            h2 ^= s[i];
            h2 *= 1099511628211UL;
        }

        // Make sure neither is 0 (would cause all hashes to collide)
        h1 = h1 | 1;
        h2 = h2 | 1;

        return (h1, h2);
    }

    /// <summary>
    /// Returns the union of a collection of Bloom filters by bitwise OR.
    /// All filters must have the same bit array size, or the largest is used.
    /// The result uses the SAME hash function parameters (k) as the source filters
    /// so that hash positions remain consistent.
    /// </summary>
    public static BloomFilter Union(IReadOnlyList<BloomFilter> filters)
    {
        if (filters == null) throw new ArgumentNullException(nameof(filters));

        // Validate: check nulls BEFORE accessing any member
        foreach (var filter in filters)
            if (filter == null)
                throw new ArgumentNullException(nameof(filter));

        if (filters.Count == 0)
            return new BloomFilter(1, 0.01); // Empty filter

        // Use the parameter values from the first filter so hash params stay consistent
        var first = filters.First();
        var targetN = (int)Math.Max(filters.Select(f => (long)f._actualInsertions).Sum(), 1);
        var targetBitCount = filters.Max(f => f._bitCount);

        var result = new BloomFilter(targetN, first.TargetFpr);
        result._bitCount = targetBitCount;
        result._m = (long)targetBitCount * 64;
        result._bits = new ulong[targetBitCount];
        result._numHashFunctions = first._numHashFunctions; // Override to match source filters

        foreach (var filter in filters)
        {
            var limit = Math.Min((long)targetBitCount, (long)filter._bitCount);
            for (var i = 0; i < limit; i++)
                result._bits[i] |= filter._bits[i];
        }

        return result;
    }

    /// <summary>
    /// Returns the intersection of a collection of Bloom filters by bitwise AND.
    /// If fewer than 2 filters are provided, the first one is returned.
    /// Null elements are checked BEFORE any other operations.
    /// </summary>
    public static BloomFilter Intersection(IReadOnlyList<BloomFilter> filters)
    {
        if (filters == null) throw new ArgumentNullException(nameof(filters));

        // Validate: check nulls BEFORE accessing any member (Min would NRE otherwise)
        foreach (var filter in filters)
            if (filter == null)
                throw new ArgumentNullException(nameof(filter));

        if (filters.Count == 0)
            return new BloomFilter(1, 0.01); // Empty filter

        if (filters.Count == 1)
            return filters[0];

        // Use the same m and bit array size from the first filter so hash positions stay consistent
        var first = filters[0];
        var bitCount = (int)first._bitCount;
        var mBits = first._m; // same bit array size as source filters

        // Build result with exact same parameters using the factory constructor
        // AND all bit arrays together
        var resultBits = new ulong[bitCount];
        for (var i = 0; i < bitCount; i++)
            resultBits[i] = (ulong)filters[0]._bits[i];

        for (var f = 1; f < filters.Count; f++)
            for (var i = 0; i < bitCount; i++)
                resultBits[i] &= filters[f]._bits[i];

        var result = new BloomFilter(mBits, bitCount, resultBits, first._numHashFunctions);

        return result;
    }
}
