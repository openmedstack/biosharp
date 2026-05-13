using System;
using OpenMedStack.BioSharp.Io.Sam;

namespace OpenMedStack.BioSharp.Io;

public static class Extensions
{
    private const string SequenceChars = "=ACMGRSVTWYHKDBN";

    public static uint Encode(this (uint count, CigarOp op) ops)
    {
        return (ops.count << 4) | (byte)ops.op;
    }

    public static (uint count, CigarOp op) Decode(this uint ops)
    {
        var count = ops >> 4;
        var op = (CigarOp)(ops & 0b1111);
        return (count, op);
    }

    public static string ReadSequence(this Span<byte> bytes)
    {
        var maxLength = bytes.Length * 2;
        var buffer = maxLength <= 512 ? stackalloc char[maxLength] : new char[maxLength];
        var len = 0;
        foreach (var b in bytes)
        {
            buffer[len++] = SequenceChars[b >> 4];
            buffer[len++] = SequenceChars[b & 0b1111];
        }

        if (len > 0 && buffer[len - 1] == '=')
        {
            len--;
        }

        return new string(buffer[..len]);
    }

    public static byte[] WriteSequence(this string sequence) =>
        WriteSequence(sequence.AsSpan());

    public static byte[] WriteSequence(ReadOnlySpan<char> sequence)
    {
        var result = new byte[(sequence.Length + 1) / 2];
        for (var i = 0; i < sequence.Length; i += 2)
        {
            var first = (byte)(SequenceChars.IndexOf(sequence[i]) << 4);
            var second = i == sequence.Length - 1 ? 0 : SequenceChars.IndexOf(sequence[i + 1]) & 0b1111;
            result[i / 2] = (byte)(first | second);
        }

        return result;
    }

    /// <summary>
    /// Splits <paramref name="source"/> using <see cref="MemoryExtensions.Split(ReadOnlySpan{char},Span{Range},char,StringSplitOptions)"/>,
    /// writing segment <see cref="Range"/> values into <paramref name="destination"/>.
    /// Combine with <c>source.AsSpan()[range]</c> to access each segment as a <see cref="ReadOnlySpan{T}"/>
    /// without allocating a <see cref="string"/> array.
    /// </summary>
    /// <returns>The number of segments written to <paramref name="destination"/>.</returns>
    public static int SplitRanges(
        this string source,
        Span<Range> destination,
        char separator,
        StringSplitOptions options = StringSplitOptions.None)
        => source.AsSpan().Split(destination, separator, options);
}
