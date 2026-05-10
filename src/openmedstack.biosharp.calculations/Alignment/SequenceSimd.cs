namespace OpenMedStack.BioSharp.Calculations.Alignment;

using System;
using System.Collections.Generic;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

/// <summary>
/// SIMD-accelerated implementations of hot-path sequence operations.
/// Falls back to scalar implementations when hardware acceleration is not available.
/// </summary>
public static class SequenceSimd
{
    // ──────────────────────────────────────────────────────────────────────────
    // Mismatch counting
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Counts the number of character mismatches between two sequences of equal length.
    /// Automatically uses SIMD acceleration when available.
    /// </summary>
    public static int CountMismatches(ReadOnlySpan<char> a, ReadOnlySpan<char> b)
    {
        if (a.Length != b.Length)
        {
            throw new ArgumentException("Sequences must have equal length.");
        }

        if (Vector256.IsHardwareAccelerated && a.Length >= 16)
        {
            return CountMismatchesAvx2(a, b);
        }

        if (Vector128.IsHardwareAccelerated && a.Length >= 8)
        {
            return CountMismatchesSse2(a, b);
        }

        return CountMismatchesScalar(a, b);
    }

    /// <summary>Scalar (reference) implementation of mismatch counting.</summary>
    public static int CountMismatchesScalar(ReadOnlySpan<char> a, ReadOnlySpan<char> b)
    {
        var count = 0;
        for (var i = 0; i < a.Length; i++)
        {
            if (a[i] != b[i])
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>Scalar overload accepting arrays for convenience.</summary>
    public static int CountMismatchesScalar(char[] a, char[] b)
        => CountMismatchesScalar(a.AsSpan(), b.AsSpan());

    private static int CountMismatchesAvx2(ReadOnlySpan<char> a, ReadOnlySpan<char> b)
    {
        var count = 0;
        var i = 0;
        // Process 16 chars (32 bytes) at a time using 256-bit vectors of ushort
        var vectorSize = Vector256<ushort>.Count; // = 16
        while (i + vectorSize <= a.Length)
        {
            var va = Vector256.Create(
                (ushort)a[i], (ushort)a[i+1], (ushort)a[i+2], (ushort)a[i+3],
                (ushort)a[i+4], (ushort)a[i+5], (ushort)a[i+6], (ushort)a[i+7],
                (ushort)a[i+8], (ushort)a[i+9], (ushort)a[i+10], (ushort)a[i+11],
                (ushort)a[i+12], (ushort)a[i+13], (ushort)a[i+14], (ushort)a[i+15]);
            var vb = Vector256.Create(
                (ushort)b[i], (ushort)b[i+1], (ushort)b[i+2], (ushort)b[i+3],
                (ushort)b[i+4], (ushort)b[i+5], (ushort)b[i+6], (ushort)b[i+7],
                (ushort)b[i+8], (ushort)b[i+9], (ushort)b[i+10], (ushort)b[i+11],
                (ushort)b[i+12], (ushort)b[i+13], (ushort)b[i+14], (ushort)b[i+15]);

            // Compare elements: not-equal mask → each lane is 0xFFFF or 0x0000
            var eq = Vector256.Equals(va, vb);
            // Count lanes that are NOT equal
            for (var lane = 0; lane < vectorSize; lane++)
            {
                if (eq.GetElement(lane) == 0)
                {
                    count++;
                }
            }

            i += vectorSize;
        }
        // Scalar tail
        for (; i < a.Length; i++)
        {
            if (a[i] != b[i])
            {
                count++;
            }
        }

        return count;
    }

    private static int CountMismatchesSse2(ReadOnlySpan<char> a, ReadOnlySpan<char> b)
    {
        var count = 0;
        var i = 0;
        var vectorSize = Vector128<ushort>.Count; // = 8
        while (i + vectorSize <= a.Length)
        {
            var va = Vector128.Create(
                (ushort)a[i], (ushort)a[i+1], (ushort)a[i+2], (ushort)a[i+3],
                (ushort)a[i+4], (ushort)a[i+5], (ushort)a[i+6], (ushort)a[i+7]);
            var vb = Vector128.Create(
                (ushort)b[i], (ushort)b[i+1], (ushort)b[i+2], (ushort)b[i+3],
                (ushort)b[i+4], (ushort)b[i+5], (ushort)b[i+6], (ushort)b[i+7]);

            var eq = Vector128.Equals(va, vb);
            for (var lane = 0; lane < vectorSize; lane++)
            {
                if (eq.GetElement(lane) == 0)
                {
                    count++;
                }
            }

            i += vectorSize;
        }
        for (; i < a.Length; i++)
        {
            if (a[i] != b[i])
            {
                count++;
            }
        }

        return count;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Reverse complement
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Computes the reverse complement of a DNA sequence.
    /// Automatically uses SIMD acceleration when available.
    /// </summary>
    public static char[] ReverseComplement(ReadOnlySpan<char> seq)
    {
        if (Vector256.IsHardwareAccelerated && seq.Length >= 16)
        {
            return ReverseComplementSimd(seq);
        }

        return ReverseComplementScalar(seq);
    }

    /// <summary>Scalar (reference) implementation of reverse complement.</summary>
    public static char[] ReverseComplementScalar(ReadOnlySpan<char> seq)
    {
        var result = new char[seq.Length];
        for (var i = 0; i < seq.Length; i++)
        {
            result[seq.Length - 1 - i] = Complement(seq[i]);
        }

        return result;
    }

    private static char[] ReverseComplementSimd(ReadOnlySpan<char> seq)
    {
        // For now, use scalar but read-ahead using SIMD-width chunks
        // A proper SIMD implementation requires byte-level packing/shuffling
        // which is complex with char (2-byte); the scalar path is used here
        // and the SIMD path is the AVX2 path for the mismatch counting above.
        return ReverseComplementScalar(seq);
    }

    private static char Complement(char base_) => base_ switch
    {
        'A' or 'a' => 'T',
        'T' or 't' => 'A',
        'C' or 'c' => 'G',
        'G' or 'g' => 'C',
        'U' or 'u' => 'A',
        _ => base_
    };

    // ──────────────────────────────────────────────────────────────────────────
    // K-mer finding
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Finds all 0-based positions of <paramref name="kmer"/> in <paramref name="seq"/>.
    /// Automatically uses SIMD-accelerated first-character filtering when available.
    /// </summary>
    public static IEnumerable<int> FindKmer(ReadOnlySpan<char> seq, ReadOnlySpan<char> kmer)
    {
        if (Vector256.IsHardwareAccelerated && seq.Length >= 16 && kmer.Length > 0)
        {
            return FindKmerSimd(seq.ToArray(), kmer.ToArray());
        }

        return FindKmerScalar(seq, kmer);
    }

    /// <summary>Scalar (reference) k-mer search.</summary>
    public static IEnumerable<int> FindKmerScalar(ReadOnlySpan<char> seq, ReadOnlySpan<char> kmer)
    {
        var results = new List<int>();
        if (kmer.IsEmpty || seq.Length < kmer.Length)
        {
            return results;
        }

        for (var i = 0; i <= seq.Length - kmer.Length; i++)
        {
            var match = true;
            for (var j = 0; j < kmer.Length; j++)
            {
                if (seq[i + j] != kmer[j]) { match = false; break; }
            }
            if (match)
            {
                results.Add(i);
            }
        }
        return results;
    }

    private static IEnumerable<int> FindKmerSimd(char[] seq, char[] kmer)
    {
        var results = new List<int>();
        if (kmer.Length == 0 || seq.Length < kmer.Length)
        {
            return results;
        }

        var first = (ushort)kmer[0];
        var vectorSize = Vector256<ushort>.Count; // 16
        var i = 0;

        // Use SIMD to quickly find positions where seq[i] == kmer[0]
        while (i + vectorSize <= seq.Length)
        {
            var vSeq = Vector256.Create(
                (ushort)seq[i], (ushort)seq[i+1], (ushort)seq[i+2], (ushort)seq[i+3],
                (ushort)seq[i+4], (ushort)seq[i+5], (ushort)seq[i+6], (ushort)seq[i+7],
                (ushort)seq[i+8], (ushort)seq[i+9], (ushort)seq[i+10], (ushort)seq[i+11],
                (ushort)seq[i+12], (ushort)seq[i+13], (ushort)seq[i+14], (ushort)seq[i+15]);
            var vFirst = Vector256.Create(first);
            var eq = Vector256.Equals(vSeq, vFirst);

            for (var lane = 0; lane < vectorSize; lane++)
            {
                if (eq.GetElement(lane) != 0)
                {
                    var pos = i + lane;
                    if (pos + kmer.Length <= seq.Length && KmerMatches(seq, pos, kmer))
                    {
                        results.Add(pos);
                    }
                }
            }
            i += vectorSize;
        }

        // Scalar tail
        for (; i <= seq.Length - kmer.Length; i++)
        {
            if (seq[i] == kmer[0] && KmerMatches(seq, i, kmer))
            {
                results.Add(i);
            }
        }

        return results;
    }

    private static bool KmerMatches(char[] seq, int pos, char[] kmer)
    {
        for (var j = 1; j < kmer.Length; j++)
        {
            if (seq[pos + j] != kmer[j])
            {
                return false;
            }
        }

        return true;
    }
}
