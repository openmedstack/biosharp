namespace OpenMedStack.BioSharp.Calculations.Alignment;

using System;
using System.Buffers;
using System.Runtime.CompilerServices;

internal static class DnaEncoding
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static char Normalize(char value)
    {
        return (uint)(value - 'a') <= (uint)('z' - 'a') ? (char)(value - 32) : value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool AreEqual(char left, char right)
    {
        return Normalize(left) == Normalize(right);
    }

    public static int CountNonGap(ReadOnlySpan<char> data)
    {
        var count = 0;
        foreach (var t in data)
        {
            if (t != '-')
            {
                count++;
            }
        }

        return count;
    }

    public static string ExtractUngapped(ReadOnlySpan<char> data)
    {
        var length = CountNonGap(data);
        if (length == 0)
        {
            return string.Empty;
        }

        var buffer = ArrayPool<char>.Shared.Rent(length);
        try
        {
            var pos = 0;
            foreach (var value in data)
            {
                if (value != '-')
                {
                    buffer[pos++] = Normalize(value);
                }
            }
            return new string(buffer, 0, length);
        }
        finally
        {
            ArrayPool<char>.Shared.Return(buffer, clearArray: false);
        }
    }

    public static string ReverseComplement(ReadOnlySpan<char> sequence)
    {
        if (sequence.IsEmpty)
        {
            return string.Empty;
        }

        var complement = ArrayPool<char>.Shared.Rent(sequence.Length);
        try
        {
            for (var index = 0; index < sequence.Length; index++)
            {
                var baseChar = Normalize(sequence[sequence.Length - 1 - index]);
                complement[index] = baseChar switch
                {
                    'A' => 'T',
                    'T' => 'A',
                    'C' => 'G',
                    'G' => 'C',
                    _   => baseChar
                };
            }
            return new string(complement, 0, sequence.Length);
        }
        finally
        {
            ArrayPool<char>.Shared.Return(complement, clearArray: false);
        }
    }

    public static bool IsLowComplexity(ReadOnlySpan<char> sequence, double threshold = 0.85d)
    {
        if (sequence.Length == 0)
        {
            return true;
        }

        Span<int> counts = stackalloc int[4];
        var canonicalBases = 0;
        foreach (var t in sequence)
        {
            switch (Normalize(t))
            {
                case 'A':
                    counts[0]++;
                    canonicalBases++;
                    break;
                case 'C':
                    counts[1]++;
                    canonicalBases++;
                    break;
                case 'G':
                    counts[2]++;
                    canonicalBases++;
                    break;
                case 'T':
                    counts[3]++;
                    canonicalBases++;
                    break;
            }
        }

        if (canonicalBases == 0)
        {
            return true;
        }

        var max = 0;
        foreach (var t in counts)
        {
            if (t > max)
            {
                max = t;
            }
        }

        return max / (double)canonicalBases >= threshold;
    }
}
