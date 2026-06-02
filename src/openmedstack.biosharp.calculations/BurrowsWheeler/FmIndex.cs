namespace OpenMedStack.BioSharp.Calculations.BurrowsWheeler;

using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Text;

/// <summary>
/// FM-index over a DNA reference for O(m) exact-match seeding — the same
/// algorithmic foundation used by BWA and Bowtie2.
///
/// Construction pipeline
/// ─────────────────────
/// 1. Append sentinel '$' (rank 0) to the reference DNA text.
/// 2. Build the suffix array (SA) using prefix doubling with counting sort
///    — O(n log n) time, O(n) space.  For whole-genome references ≥ 50 MB
///    consider replacing this step with SA-IS (O(n); see libdivsufsort).
/// 3. Derive BWT[i] = T[SA[i]−1] from the SA.
/// 4. Build the C-array: C[b] = number of characters in T$ with rank &lt; b.
/// 5. Build sampled Occ table: Occ[b][j] = count of b in BWT[0 .. j·SR−1],
///    sampled every SR = <see cref="SampleRate"/> rows, giving O(n/SR) memory.
/// 6. Build sampled SA: SA[i] for i = 0, SR, 2·SR, … giving O(n/SR) memory.
///
/// Seeding
/// ───────
/// • <see cref="FindExactSeeds"/> — fixed-length seeds (like BWA-aln), one
///   backward-search per sampled query position; O(m/step × seedLen × SR).
/// • <see cref="FindMemSeeds"/> — variable-length right-anchored MEM seeds
///   (BWA-MEM/SMEM style); non-overlapping, covers the full read.
///
/// Both methods return reference positions via <see cref="Locate"/>, which
/// walks the LF-mapping from sampled SA rows — O(SR) per position.
/// </summary>
public sealed class FmIndex
{
    // ── Alphabet: $ = 0, A = 1, C = 2, G = 3, N = 4, T = 5 ─────────────────
    private const int AlphabetSize = 6;
    private static readonly byte[] CharToBase = BuildCharToBaseTable();

    private static byte[] BuildCharToBaseTable()
    {
        var t = new byte[256];
        Array.Fill(t, (byte)4); // default: N
        t['$'] = 0;
        t['A'] = t['a'] = 1;
        t['C'] = t['c'] = 2;
        t['G'] = t['g'] = 3;
        t['N'] = t['n'] = 4;
        t['T'] = t['t'] = 5;
        t['U'] = t['u'] = 5;
        return t;
    }

    private const uint FormatMagic = 0x58494D46; // 'FMIX'

    // ── Index data ────────────────────────────────────────────────────────────
    private readonly int _n;           // original text length (without sentinel)
    private readonly byte[] _bwt;      // BWT[0..n], length n+1
    private readonly int[] _c;         // C[b] = #chars in T$ with encoded value < b
    private readonly int[][] _occ;     // _occ[b][s] = Occ(b, s * SampleRate)
    private readonly int[] _saSample;  // _saSample[s] = SA[s * SampleRate]
    private readonly int _sampleRate;

    /// <summary>Row-sampling rate for the Occ and SA tables (default 64).</summary>
    public int SampleRate => _sampleRate;

    /// <summary>Length of the original (non-sentinel) reference text.</summary>
    public int TextLength => _n;

    private FmIndex(int n, byte[] bwt, int[] c, int[][] occ, int[] saSample, int sampleRate)
    {
        _n          = n;
        _bwt        = bwt;
        _c          = c;
        _occ        = occ;
        _saSample   = saSample;
        _sampleRate = sampleRate;
    }

    // ── Build ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds an FM-index from a DNA reference string.
    /// Accepts upper- and lower-case A/C/G/T/N; all other characters
    /// are treated as N.
    /// </summary>
    /// <param name="reference">Reference DNA sequence.</param>
    /// <param name="sampleRate">
    /// Row-sampling interval for the Occ and SA tables.
    /// Default 64 gives O(n/64) memory and O(64) time per rank/locate call.
    /// </param>
    public static FmIndex Build(ReadOnlySpan<char> reference, int sampleRate = 64)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(sampleRate, 1);

        var n   = reference.Length;
        var len = n + 1; // includes sentinel

        // Encode text; sentinel stays 0 (array already zero-initialized)
        var text = new byte[len];
        for (var i = 0; i < n; i++)
        {
            text[i] = CharToBase[(byte)reference[i]];
        }

        // Suffix array
        var sa = BuildSuffixArray(text, n);

        // BWT from SA: BWT[i] = T[SA[i]-1] (sentinel wraps to byte 0)
        var bwt = new byte[len];
        for (var i = 0; i < len; i++)
        {
            bwt[i] = sa[i] > 0 ? text[sa[i] - 1] : (byte)0;
        }

        // C array
        var freq = new int[AlphabetSize];
        foreach (var b in text) freq[b]++;
        var c = new int[AlphabetSize];
        for (var b = 1; b < AlphabetSize; b++)
        {
            c[b] = c[b - 1] + freq[b - 1];
        }

        // Sampled Occ table
        var numPartitions = len / sampleRate + 2;
        var occ = new int[AlphabetSize][];
        for (var b = 0; b < AlphabetSize; b++)
        {
            occ[b] = new int[numPartitions];
        }

        var running = new int[AlphabetSize];
        for (var i = 0; i < len; i++)
        {
            if (i % sampleRate == 0)
            {
                var s = i / sampleRate;
                for (var b = 0; b < AlphabetSize; b++)
                {
                    occ[b][s] = running[b];
                }
            }
            running[bwt[i]]++;
        }
        // Store total counts in the last partition slot (for queries past the last sampled row)
        {
            var sLast = len / sampleRate + 1; // always use the +1 slot, never the same as any i/sampleRate
            for (var b = 0; b < AlphabetSize; b++)
            {
                occ[b][sLast] = running[b];
            }
        }

        // Sampled SA: _saSample[s] = SA[s * sampleRate]
        var numSaSamples = len / sampleRate + 2;
        var saSample = new int[numSaSamples];
        for (var i = 0; i < len; i++)
        {
            if (i % sampleRate == 0)
            {
                saSample[i / sampleRate] = sa[i];
            }
        }

        return new FmIndex(n, bwt, c, occ, saSample, sampleRate);
    }

    // ── Suffix Array Construction — Prefix Doubling (O(n log n)) ─────────────

    /// <summary>
    /// Constructs the suffix array of <paramref name="text"/> (length n+1,
    /// where text[n] = 0 is the sentinel) using prefix doubling with
    /// counting sort — O(n log n) time, O(n) space.
    ///
    /// For whole-genome references (n ≥ 50 M) consider replacing this with
    /// the SA-IS algorithm (O(n); implemented in libdivsufsort).
    /// </summary>
    public static int[] BuildSuffixArray(byte[] text, int n)
    {
        var len   = n + 1;
        var sa    = new int[len];
        var rank  = new int[len + 1]; // rank[len] acts as −∞ sentinel
        var tmp   = new int[len];

        // ── Initialise ranks from first character ─────────────────────────
        for (var i = 0; i < len; i++) { sa[i] = i; rank[i] = text[i]; }
        rank[len] = -1;

        // Sort SA by initial rank (counting sort over full byte range 0-255)
        {
            var cnt = new int[257]; // 256 possible byte values, +1 for offset trick
            for (var i = 0; i < len; i++)
            {
                cnt[rank[i] + 1]++;
            }

            for (var i = 1; i <= 256; i++)
            {
                cnt[i] += cnt[i - 1];
            }

            var buf = new int[len];
            for (var i = 0; i < len; i++)
            {
                buf[cnt[rank[i]]++] = i;
            }

            Array.Copy(buf, sa, len);
        }

        // Assign ranks after initial sort
        tmp[sa[0]] = 0;
        for (var i = 1; i < len; i++)
        {
            tmp[sa[i]] = tmp[sa[i - 1]] + (rank[sa[i]] != rank[sa[i - 1]] ? 1 : 0);
        }

        Array.Copy(tmp, 0, rank, 0, len);

        // ── Prefix-doubling iterations ────────────────────────────────────
        for (var k = 1; k < len; k <<= 1)
        {
            if (rank[sa[len - 1]] == len - 1)
            {
                break; // all ranks unique → done
            }

            // Build SA ordered by secondary key rank[i+k]:
            //   positions i where i+k ≥ len have secondary rank = −1, sort first
            var sa2 = new int[len];
            var j2  = 0;
            for (var i = len - k; i < len; i++)
            {
                sa2[j2++] = i;
            }

            // Remaining positions ordered by rank[i+k] = rank[sa[?]], derived from SA
            for (var i = 0; i < len; i++)
            {
                if (sa[i] >= k)
                {
                    sa2[j2++] = sa[i] - k;
                }
            }

            // Stable sort SA2 by primary key rank[sa2[i]] (counting sort)
            var maxRnk = rank[sa[len - 1]] + 2;
            var cnt2   = new int[maxRnk + 1];
            for (var i = 0; i < len; i++)
            {
                cnt2[rank[sa2[i]] + 1]++;
            }

            for (var i = 1; i <= maxRnk; i++)
            {
                cnt2[i] += cnt2[i - 1];
            }

            var newSa = new int[len];
            for (var i = 0; i < len; i++)
            {
                newSa[cnt2[rank[sa2[i]]]++] = sa2[i];
            }

            Array.Copy(newSa, sa, len);

            // Assign new ranks based on (rank[i], rank[i+k]) pairs
            tmp[sa[0]] = 0;
            for (var i = 1; i < len; i++)
            {
                tmp[sa[i]] = tmp[sa[i - 1]];
                var prev = sa[i - 1]; var cur = sa[i];
                var rp = rank[prev]; var rc = rank[cur];
                var rp2 = prev + k < len ? rank[prev + k] : -1;
                var rc2 = cur  + k < len ? rank[cur  + k] : -1;
                if (rp != rc || rp2 != rc2)
                {
                    tmp[sa[i]]++;
                }
            }
            Array.Copy(tmp, 0, rank, 0, len);
        }

        return sa;
    }

    // ── Core FM operations ────────────────────────────────────────────────────

    /// <summary>
    /// Rank query: number of occurrences of encoded byte <paramref name="b"/>
    /// in BWT[0 .. <paramref name="i"/>−1].
    /// O(<see cref="SampleRate"/>) per call; uses SIMD when available.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int Rank(byte b, int i)
    {
        if (i <= 0)
        {
            return 0;
        }

        var s     = i / _sampleRate;
        var count = _occ[b][s];
        var start = s * _sampleRate;
        var len   = i - start;
        if (len <= 0)
        {
            return count;
        }

        return count + CountByte(_bwt.AsSpan(start, len), b);
    }

    /// <summary>
    /// Counts occurrences of <paramref name="target"/> in <paramref name="span"/>
    /// using SIMD when available (Vector256 → Vector128 → scalar fallback).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int CountByte(ReadOnlySpan<byte> span, byte target)
    {
        var n     = span.Length;
        var count = 0;
        var i     = 0;

        if (Vector256.IsHardwareAccelerated && n >= 32)
        {
            var vTarget = Vector256.Create(target);
            ref var baseRef = ref System.Runtime.InteropServices.MemoryMarshal.GetReference(span);
            for (; i <= n - 32; i += 32)
            {
                var v  = Vector256.LoadUnsafe(ref baseRef, (nuint)i);
                var eq = Vector256.Equals(v, vTarget);
                count += BitOperations.PopCount(Vector256.ExtractMostSignificantBits(eq));
            }
        }
        else if (Vector128.IsHardwareAccelerated && n >= 16)
        {
            var vTarget = Vector128.Create(target);
            ref var baseRef = ref System.Runtime.InteropServices.MemoryMarshal.GetReference(span);
            for (; i <= n - 16; i += 16)
            {
                var v  = Vector128.LoadUnsafe(ref baseRef, (nuint)i);
                var eq = Vector128.Equals(v, vTarget);
                count += BitOperations.PopCount(Vector128.ExtractMostSignificantBits(eq));
            }
        }

        // Scalar tail
        for (; i < n; i++)
        {
            if (span[i] == target)
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>
    /// LF-mapping: row i → row number of its BWT predecessor in sorted order.
    /// In other words, if SA[i] = p then LF(i) = inverse_SA[p−1].
    /// Used for SA locate: SA[i] = SA[LF(i)] + 1.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int LFMap(int i) => _c[_bwt[i]] + Rank(_bwt[i], i);

    // ── Backward search ───────────────────────────────────────────────────────

    /// <summary>
    /// Backward search for the exact pattern <paramref name="pattern"/> in the
    /// indexed reference.
    ///
    /// Processes characters from right to left in O(m × SR) time (m =
    /// pattern length, SR = <see cref="SampleRate"/>).
    ///
    /// Returns the half-open SA interval [Sp, Ep): any SA[k] for k in
    /// [Sp, Ep) is a reference position where <paramref name="pattern"/>
    /// occurs.  Ep == Sp means no match.
    /// </summary>
    public (int Sp, int Ep) BackwardSearch(ReadOnlySpan<char> pattern)
    {
        var sp = 0;
        var ep = _n + 1;

        for (var i = pattern.Length - 1; i >= 0; i--)
        {
            var ch = (byte)pattern[i];
            if (ch >= CharToBase.Length)
            {
                return (0, 0);
            }

            var b = CharToBase[ch];
            if (b == 0)
            {
                return (0, 0); // sentinel not valid in a query
            }

            sp = _c[b] + Rank(b, sp);
            ep = _c[b] + Rank(b, ep);
            if (sp >= ep)
            {
                return (0, 0);
            }
        }
        return (sp, ep);
    }

    // ── SA locate ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Converts a BWT SA interval [<paramref name="sp"/>, <paramref name="ep"/>)
    /// to at most <paramref name="maxCount"/> reference positions using the
    /// sampled SA and LF-mapping.
    ///
    /// Each position is found in O(<see cref="SampleRate"/>) LF steps.
    /// The formula SA[i] = (SA[LF^k(i)] + k) mod (n+1) is used,
    /// where k is the number of LF-steps to reach a sampled row.
    /// </summary>
    public int[] Locate(int sp, int ep, int maxCount = 64)
    {
        var count    = Math.Min(ep - sp, maxCount);
        var buf      = new int[count];
        var actual   = 0;
        var nPlusOne = _n + 1;
        for (var idx = 0; idx < count; idx++)
        {
            var row   = sp + idx;
            var steps = 0;
            while (row % _sampleRate != 0)
            {
                row = LFMap(row);
                steps++;
            }
            // SA[original_row] = (SA[sampled_row] + steps) mod (n+1)
            var pos = (_saSample[row / _sampleRate] + steps) % nPlusOne;
            // Exclude the sentinel position (_n) — it is not a valid reference position
            if (pos < _n)
            {
                buf[actual++] = pos;
            }
        }
        return actual == count ? buf : buf[..actual];
    }

    // ── Fixed-length exact-match seeds ───────────────────────────────────────

    /// <summary>
    /// Produces fixed-length exact-match seeds of the query against the
    /// reference — directly analogous to BWA-aln's seed phase.
    ///
    /// For every sampled query start position i = 0, <paramref name="seedStep"/>,
    /// 2·<paramref name="seedStep"/>, … a backward search is run over the
    /// window Q[i .. i+<paramref name="seedLen"/>−1].  Seed hits that exceed
    /// <paramref name="maxHits"/> are discarded as repetitive (same policy as
    /// <see cref="Alignment.ReferenceIndex"/>).
    ///
    /// Total cost: O(m/step × seedLen × SR).
    /// </summary>
    public IReadOnlyList<FmSeed> FindExactSeeds(
        ReadOnlySpan<char> query,
        int seedLen  = 19,
        int seedStep = 1,
        int maxHits  = 64)
    {
        var m     = query.Length;
        var seeds = new List<FmSeed>();

        for (var start = 0; start + seedLen <= m; start += seedStep)
        {
            var sp    = 0;
            var ep    = _n + 1;
            var valid = true;

            // Backward search: process Q[start+seedLen-1] .. Q[start]
            for (var pos = start + seedLen - 1; pos >= start; pos--)
            {
                var b = CharToBase[(byte)query[pos]];
                if (b == 0) { valid = false; break; }

                var newSp = _c[b] + Rank(b, sp);
                var newEp = _c[b] + Rank(b, ep);
                if (newSp >= newEp) { valid = false; break; }

                sp = newSp;
                ep = newEp;
            }

            if (!valid || ep - sp > maxHits)
            {
                continue;
            }

            seeds.Add(new FmSeed(start, start + seedLen, Locate(sp, ep, maxHits)));
        }

        return seeds;
    }

    // ── Variable-length MEM seeds (BWA-MEM / SMEM style) ────────────────────

    /// <summary>
    /// Produces non-overlapping, right-anchored maximal exact match (MEM)
    /// seeds — the same seeding strategy used by BWA-MEM.
    ///
    /// The algorithm performs a single right-to-left pass over the query.
    /// At each anchor position j it extends the backward search as far left
    /// as possible.  When the match reaches minimum length, the seed is
    /// recorded and the anchor jumps to just before the seed's left boundary,
    /// ensuring no two seeds overlap.
    ///
    /// Seeds shorter than <paramref name="minSeedLen"/> or with more than
    /// <paramref name="maxHits"/> reference positions are discarded.
    ///
    /// Total cost: O(m × SR) amortised (each query base is visited at most
    /// once across all backward search passes).
    /// </summary>
    public IReadOnlyList<FmSeed> FindMemSeeds(
        ReadOnlySpan<char> query,
        int minSeedLen = 19,
        int maxHits    = 64)
    {
        var m     = query.Length;
        var seeds = new List<FmSeed>();

        var rightAnchor = m - 1;
        while (rightAnchor >= minSeedLen - 1)
        {
            var sp  = 0;
            var ep  = _n + 1;
            var cur = rightAnchor;

            // Extend leftward from rightAnchor
            for (; cur >= 0; cur--)
            {
                var b = CharToBase[(byte)query[cur]];
                if (b == 0)
                {
                    break; // N breaks the match
                }

                var newSp = _c[b] + Rank(b, sp);
                var newEp = _c[b] + Rank(b, ep);
                if (newSp >= newEp)
                {
                    break;
                }

                sp = newSp;
                ep = newEp;
            }

            // Left boundary of this MEM ends one past the break point
            var left    = cur + 1;
            var seedLen = rightAnchor - left + 1;

            if (seedLen >= minSeedLen && ep > sp && ep - sp <= maxHits)
            {
                seeds.Add(new FmSeed(left, rightAnchor + 1, Locate(sp, ep, maxHits)));
                rightAnchor = left - 1; // jump past the seed
            }
            else
            {
                rightAnchor--; // try a shorter window
            }
        }

        // Seeds were collected right-to-left; reverse for left-to-right order
        seeds.Reverse();
        return seeds;
    }

    // ── Serialisation ─────────────────────────────────────────────────────────

    /// <summary>Saves the FM-index to a file.</summary>
    public void Save(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        using var fs = File.Create(path);
        Save(fs);
    }

    /// <summary>Saves the FM-index to a stream.</summary>
    public void Save(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var w = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        w.Write(FormatMagic);
        w.Write(_n);
        w.Write(_sampleRate);
        // BWT
        w.Write(_bwt.Length);
        w.Write(_bwt);
        // C array
        for (var i = 0; i < AlphabetSize; i++)
        {
            w.Write(_c[i]);
        }

        // Occ samples
        for (var b = 0; b < AlphabetSize; b++)
        {
            w.Write(_occ[b].Length);
            foreach (var v in _occ[b]) w.Write(v);
        }
        // SA samples
        w.Write(_saSample.Length);
        foreach (var v in _saSample) w.Write(v);
    }

    /// <summary>Loads an FM-index from a file.</summary>
    public static FmIndex Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        using var fs = File.OpenRead(path);
        return Load(fs);
    }

    /// <summary>Loads an FM-index from a stream.</summary>
    public static FmIndex Load(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var r = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        var magic = r.ReadUInt32();
        if (magic != FormatMagic)
        {
            throw new InvalidDataException("Not a valid FmIndex stream (wrong magic).");
        }

        var n          = r.ReadInt32();
        var sampleRate = r.ReadInt32();
        var bwtLen     = r.ReadInt32();
        var bwt        = r.ReadBytes(bwtLen);
        var c          = new int[AlphabetSize];
        for (var i = 0; i < AlphabetSize; i++)
        {
            c[i] = r.ReadInt32();
        }

        var occ = new int[AlphabetSize][];
        for (var b = 0; b < AlphabetSize; b++)
        {
            var len = r.ReadInt32();
            occ[b]  = new int[len];
            for (var i = 0; i < len; i++)
            {
                occ[b][i] = r.ReadInt32();
            }
        }
        var saLen  = r.ReadInt32();
        var saLookup = new int[saLen];
        for (var i = 0; i < saLen; i++)
        {
            saLookup[i] = r.ReadInt32();
        }

        return new FmIndex(n, bwt, c, occ, saLookup, sampleRate);
    }
}

/// <summary>
/// An exact-match seed identified by <see cref="FmIndex.FindExactSeeds"/>
/// or <see cref="FmIndex.FindMemSeeds"/>.
/// </summary>
/// <param name="QueryStart">0-based inclusive start position in the query.</param>
/// <param name="QueryEnd">0-based exclusive end position in the query.</param>
/// <param name="ReferencePositions">
/// 0-based positions in the reference at which this seed occurs.
/// Length ≤ the <c>maxHits</c> passed to the seeding call.
/// </param>
public readonly record struct FmSeed(int QueryStart, int QueryEnd, int[] ReferencePositions);




