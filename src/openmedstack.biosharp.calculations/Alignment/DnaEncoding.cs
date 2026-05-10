namespace OpenMedStack.BioSharp.Calculations.Alignment;

using System;

internal static class DnaEncoding
{
    public static char Normalize(char value)
    {
        return value switch
        {
            >= 'a' and <= 'z' => (char)(value - 32),
            _ => value
        };
    }

    public static bool AreEqual(char left, char right)
    {
        return Normalize(left) == Normalize(right);
    }

    public static int CountNonGap(ReadOnlySpan<char> data)
    {
        var count = 0;
        for (var index = 0; index < data.Length; index++)
        {
            if (data[index] != '-')
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

        var buffer = new char[length];
        var pos = 0;
        for (var index = 0; index < data.Length; index++)
        {
            var value = data[index];
            if (value == '-')
            {
                continue;
            }

            buffer[pos++] = Normalize(value);
        }

        return new string(buffer);
    }

    public static string ReverseComplement(ReadOnlySpan<char> sequence)
    {
        if (sequence.IsEmpty)
        {
            return string.Empty;
        }

        var complement = new char[sequence.Length];
        for (var index = 0; index < sequence.Length; index++)
        {
            var baseChar = Normalize(sequence[sequence.Length - 1 - index]);
            complement[index] = baseChar switch
            {
                'A' => 'T',
                'T' => 'A',
                'C' => 'G',
                'G' => 'C',
                _ => baseChar
            };
        }

        return new string(complement);
    }

    public static bool IsLowComplexity(ReadOnlySpan<char> sequence, double threshold = 0.85d)
    {
        if (sequence.Length == 0)
        {
            return true;
        }

        Span<int> counts = stackalloc int[4];
        var canonicalBases = 0;
        for (var index = 0; index < sequence.Length; index++)
        {
            switch (Normalize(sequence[index]))
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
        for (var index = 0; index < counts.Length; index++)
        {
            if (counts[index] > max)
            {
                max = counts[index];
            }
        }

        return max / (double)canonicalBases >= threshold;
    }
}