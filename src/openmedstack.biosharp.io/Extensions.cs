using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace OpenMedStack.BioSharp.Io;

public static class Extensions
{
    private const string CigarCodes = "MIDNSHP=X";
    private const string SequenceChars = "=ACMGRSVTWYHKDBN";

    public static uint Encode(this (uint count, char op) ops)
    {
        return ops.count << 4 | (uint)CigarCodes.IndexOf(ops.op);
    }

    public static (uint count, char op) Decode(this uint ops)
    {
        var count = ops >> 4;
        var index = ops & 0b1111;
        var code = CigarCodes[(int)index];
        return (count, code);
    }

    public static string ReadSequence(this Span<byte> bytes)
    {
        var buffer = new List<char>();
        foreach (var b in bytes)
        {
            buffer.Add(SequenceChars[b >> 4]);
            buffer.Add(SequenceChars[b & 0b1111]);
        }

        if (buffer.Last() == '=')
        {
            buffer.RemoveAt(buffer.Count - 1);
        }

        return new string(CollectionsMarshal.AsSpan(buffer));
    }

    public static Span<byte> WriteSequence(this string sequence)
    {
        var buffer = new List<byte>();
        for (var i = 0; i < sequence.Length; i += 2)
        {
            var first = (byte)(SequenceChars.IndexOf(sequence[i]) << 4);
            var second = i == sequence.Length - 1 ? 0 : SequenceChars.IndexOf(sequence[i + 1]) & 0b1111;
            buffer.Add((byte)(first | second));
        }

        return CollectionsMarshal.AsSpan(buffer);
    }
}
