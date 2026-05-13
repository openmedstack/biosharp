namespace OpenMedStack.BioSharp.Io.Cram;

using System;
using System.Buffers.Binary;
using System.IO;

/// <summary>
/// Utilities for CRAM ITF8 (variable-length integer) and LTF8 (64-bit) encoding/decoding,
/// plus CRC32 checksum used in CRAM block and container headers.
/// </summary>
internal static class CramEncoding
{
    /// <summary>
    /// CRAM magic bytes: "CRAM" (4 bytes).
    /// </summary>
    public static readonly byte[] Magic = "CRAM"u8.ToArray(); // CRAM

    /// <summary>Encodes a 32-bit signed integer as ITF8.</summary>
    public static void WriteItf8(Stream stream, int value)
    {
        var uv = unchecked((uint)value);
        WriteItf8(stream, uv);
    }

    /// <summary>Encodes a 32-bit unsigned integer as ITF8.</summary>
    public static void WriteItf8(Stream stream, uint value)
    {
        switch (value)
        {
            case < 0x80:
                stream.WriteByte((byte)value);
                break;
            case < 0x4000:
                stream.WriteByte((byte)(0x80 | (value >> 8)));
                stream.WriteByte((byte)(value & 0xFF));
                break;
            case < 0x200000:
                stream.WriteByte((byte)(0xC0 | (value >> 16)));
                stream.WriteByte((byte)((value >> 8) & 0xFF));
                stream.WriteByte((byte)(value & 0xFF));
                break;
            case < 0x10000000:
                stream.WriteByte((byte)(0xE0 | (value >> 24)));
                stream.WriteByte((byte)((value >> 16) & 0xFF));
                stream.WriteByte((byte)((value >> 8) & 0xFF));
                stream.WriteByte((byte)(value & 0xFF));
                break;
            default:
                stream.WriteByte((byte)(0xF0 | ((value >> 28) & 0x0F)));
                stream.WriteByte((byte)((value >> 20) & 0xFF));
                stream.WriteByte((byte)((value >> 12) & 0xFF));
                stream.WriteByte((byte)((value >> 4) & 0xFF));
                stream.WriteByte((byte)((value & 0x0F) << 4));
                break;
        }
    }

    /// <summary>Encodes a 64-bit integer as LTF8.</summary>
    public static void WriteLtf8(Stream stream, long value)
    {
        var uv = unchecked((ulong)value);

        if (uv < 0x80)
        {
            stream.WriteByte((byte)uv);
        }
        else if (uv < 0x4000)
        {
            stream.WriteByte((byte)(0x80 | (uv >> 8)));
            stream.WriteByte((byte)(uv & 0xFF));
        }
        else if (uv < 0x200000)
        {
            stream.WriteByte((byte)(0xC0 | (uv >> 16)));
            stream.WriteByte((byte)((uv >> 8) & 0xFF));
            stream.WriteByte((byte)(uv & 0xFF));
        }
        else if (uv < 0x10000000)
        {
            stream.WriteByte((byte)(0xE0 | (uv >> 24)));
            stream.WriteByte((byte)((uv >> 16) & 0xFF));
            stream.WriteByte((byte)((uv >> 8) & 0xFF));
            stream.WriteByte((byte)(uv & 0xFF));
        }
        else if (uv < 0x800000000UL)
        {
            stream.WriteByte((byte)(0xF0 | (int)((uv >> 32) & 0x07)));
            stream.WriteByte((byte)((uv >> 24) & 0xFF));
            stream.WriteByte((byte)((uv >> 16) & 0xFF));
            stream.WriteByte((byte)((uv >> 8) & 0xFF));
            stream.WriteByte((byte)(uv & 0xFF));
        }
        else if (uv < 0x40000000000UL)
        {
            stream.WriteByte((byte)(0xF8 | (int)((uv >> 40) & 0x03)));
            stream.WriteByte((byte)((uv >> 32) & 0xFF));
            stream.WriteByte((byte)((uv >> 24) & 0xFF));
            stream.WriteByte((byte)((uv >> 16) & 0xFF));
            stream.WriteByte((byte)((uv >> 8) & 0xFF));
            stream.WriteByte((byte)(uv & 0xFF));
        }
        else if (uv < 0x2000000000000UL)
        {
            stream.WriteByte((byte)(0xFC | (int)((uv >> 48) & 0x01)));
            stream.WriteByte((byte)((uv >> 40) & 0xFF));
            stream.WriteByte((byte)((uv >> 32) & 0xFF));
            stream.WriteByte((byte)((uv >> 24) & 0xFF));
            stream.WriteByte((byte)((uv >> 16) & 0xFF));
            stream.WriteByte((byte)((uv >> 8) & 0xFF));
            stream.WriteByte((byte)(uv & 0xFF));
        }
        else if (uv < 0x100000000000000UL)
        {
            stream.WriteByte(0xFE);
            stream.WriteByte((byte)((uv >> 48) & 0xFF));
            stream.WriteByte((byte)((uv >> 40) & 0xFF));
            stream.WriteByte((byte)((uv >> 32) & 0xFF));
            stream.WriteByte((byte)((uv >> 24) & 0xFF));
            stream.WriteByte((byte)((uv >> 16) & 0xFF));
            stream.WriteByte((byte)((uv >> 8) & 0xFF));
            stream.WriteByte((byte)(uv & 0xFF));
        }
        else
        {
            stream.WriteByte(0xFF);
            var buf = new byte[8];
            BinaryPrimitives.WriteUInt64BigEndian(buf, uv);
            stream.Write(buf, 0, 8);
        }
    }

    /// <summary>Reads an ITF8-encoded integer from <paramref name="stream"/>.</summary>
    public static int ReadItf8(Stream stream)
    {
        var b0 = stream.ReadByte();
        if (b0 < 0)
        {
            throw new EndOfStreamException("Unexpected end of stream reading ITF8.");
        }

        if ((b0 & 0x80) == 0)
        {
            return b0;
        }

        if ((b0 & 0xC0) == 0x80)
        {
            var b1 = ReadByte(stream);
            return ((b0 & 0x3F) << 8) | b1;
        }

        if ((b0 & 0xE0) == 0xC0)
        {
            var b1 = ReadByte(stream);
            var b2 = ReadByte(stream);
            return ((b0 & 0x1F) << 16) | (b1 << 8) | b2;
        }

        if ((b0 & 0xF0) == 0xE0)
        {
            var b1 = ReadByte(stream);
            var b2 = ReadByte(stream);
            var b3 = ReadByte(stream);
            return ((b0 & 0x0F) << 24) | (b1 << 16) | (b2 << 8) | b3;
        }

        // 5-byte form
        var b4 = ReadByte(stream);
        var b5 = ReadByte(stream);
        var b6 = ReadByte(stream);
        var b7 = ReadByte(stream);
        return unchecked(((b0 & 0x0F) << 28) | (b4 << 20) | (b5 << 12) | (b6 << 4) | ((b7 >> 4) & 0x0F));
    }

    /// <summary>Reads an LTF8-encoded 64-bit integer from <paramref name="stream"/>.</summary>
    public static long ReadLtf8(Stream stream)
    {
        var b0 = stream.ReadByte();
        if (b0 < 0)
        {
            throw new EndOfStreamException("Unexpected end of stream reading LTF8.");
        }

        if ((b0 & 0x80) == 0)
        {
            return b0;
        }

        if ((b0 & 0xC0) == 0x80)
        {
            return ((b0 & 0x3FL) << 8) | (uint)ReadByte(stream);
        }

        if ((b0 & 0xE0) == 0xC0)
        {
            return ((b0 & 0x1FL) << 16) | ((long)ReadByte(stream) << 8) | (uint)ReadByte(stream);
        }

        if ((b0 & 0xF0) == 0xE0)
        {
            return ((b0 & 0x0FL) << 24) | ((long)ReadByte(stream) << 16) |
                ((long)ReadByte(stream) << 8) | (uint)ReadByte(stream);
        }

        if ((b0 & 0xF8) == 0xF0)
        {
            return ((b0 & 0x07L) << 32) | ((long)ReadByte(stream) << 24) |
                ((long)ReadByte(stream) << 16) | ((long)ReadByte(stream) << 8) | (uint)ReadByte(stream);
        }

        if ((b0 & 0xFC) == 0xF8)
        {
            return ((b0 & 0x03L) << 40) | ((long)ReadByte(stream) << 32) |
                ((long)ReadByte(stream) << 24) | ((long)ReadByte(stream) << 16) |
                ((long)ReadByte(stream) << 8) | (uint)ReadByte(stream);
        }

        if ((b0 & 0xFE) == 0xFC)
        {
            return ((b0 & 0x01L) << 48) | ((long)ReadByte(stream) << 40) |
                ((long)ReadByte(stream) << 32) | ((long)ReadByte(stream) << 24) |
                ((long)ReadByte(stream) << 16) | ((long)ReadByte(stream) << 8) | (uint)ReadByte(stream);
        }

        if (b0 == 0xFE)
        {
            long v = 0;
            for (var i = 0; i < 7; i++)
            {
                v = (v << 8) | (uint)ReadByte(stream);
            }

            return v;
        }

        // b0 == 0xFF: full 8 bytes
        var buf = new byte[8];
        stream.ReadExactly(buf);
        return unchecked((long)BinaryPrimitives.ReadUInt64BigEndian(buf));
    }

    private static int ReadByte(Stream stream)
    {
        var b = stream.ReadByte();
        if (b < 0)
        {
            throw new EndOfStreamException("Unexpected end of stream.");
        }

        return b;
    }

    /// <summary>Computes CRC32 over <paramref name="data"/>.</summary>
    public static uint Crc32(ReadOnlySpan<byte> data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in data)
        {
            crc ^= b;
            for (var j = 0; j < 8; j++)
            {
                crc = (crc >> 1) ^ (0xEDB88320u * (crc & 1));
            }
        }

        return crc ^ 0xFFFFFFFFu;
    }

    /// <summary>Computes CRC32 over a stream buffer (from MemoryStream).</summary>
    public static uint Crc32(MemoryStream ms)
    {
        var buf = ms.GetBuffer();
        return Crc32(buf.AsSpan(0, (int)ms.Length));
    }
}
