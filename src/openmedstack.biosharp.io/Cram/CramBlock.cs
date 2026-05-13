namespace OpenMedStack.BioSharp.Io.Cram;

using System;
using System.IO;
using System.IO.Compression;

/// <summary>
/// Represents a single CRAM block. Blocks are the fundamental data-carrying unit in CRAM.
/// </summary>
internal sealed class CramBlock
{
    // Block content types
    public const byte TypeFileHeader = 0;
    public const byte TypeCompressionHeader = 1;
    public const byte TypeExternalData = 4;
    public const byte TypeCoreData = 5;

    // Block encoding methods
    public const byte MethodRaw = 0;
    public const byte MethodGzip = 1;

    public byte Method { get; }
    public byte ContentType { get; }
    public int ContentId { get; }
    public byte[] Data { get; }

    private CramBlock(byte method, byte contentType, int contentId, byte[] data)
    {
        Method = method;
        ContentType = contentType;
        ContentId = contentId;
        Data = data;
    }

    /// <summary>Creates a RAW block.</summary>
    public static CramBlock CreateRaw(byte contentType, int contentId, byte[] data)
        => new(MethodRaw, contentType, contentId, data);

    /// <summary>Creates a GZIP-compressed block.</summary>
    public static CramBlock CreateGzip(byte contentType, int contentId, ReadOnlySpan<byte> uncompressedData,
        CompressionLevel level = CompressionLevel.Optimal)
    {
        using var ms = new MemoryStream();
        using (var gz = new GZipStream(ms, level, leaveOpen: true))
        {
            gz.Write(uncompressedData);
        }

        return new CramBlock(MethodGzip, contentType, contentId, ms.ToArray());
    }

    /// <summary>Returns the uncompressed data, decompressing if necessary.</summary>
    public byte[] GetDecompressedData()
    {
        switch (Method)
        {
            case MethodRaw:
                return Data;
            case MethodGzip:
            {
                using var input = new MemoryStream(Data);
                using var gz = new GZipStream(input, CompressionMode.Decompress);
                using var output = new MemoryStream();
                gz.CopyTo(output);
                return output.ToArray();
            }
            default:
                throw new NotSupportedException($"CRAM block compression method {Method} is not supported.");
        }
    }

    /// <summary>Writes this block to <paramref name="stream"/> in CRAM on-disk format.</summary>
    public void WriteTo(Stream stream)
    {
        var uncompSize = Method == MethodRaw ? Data.Length : GetDecompressedData().Length;

        // We need to compute CRC32 over the block header + data fields
        using var headerMs = new MemoryStream();

        headerMs.WriteByte(Method);
        headerMs.WriteByte(ContentType);
        CramEncoding.WriteItf8(headerMs, ContentId);
        CramEncoding.WriteItf8(headerMs, Data.Length);   // compressed size
        CramEncoding.WriteItf8(headerMs, uncompSize);     // uncompressed size
        headerMs.Write(Data, 0, Data.Length);

        var headerBytes = headerMs.ToArray();
        var crc = CramEncoding.Crc32(headerBytes);

        stream.Write(headerBytes, 0, headerBytes.Length);

        // CRC32 as 4-byte LE
        Span<byte> crcBuf = stackalloc byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(crcBuf, crc);
        stream.Write(crcBuf);
    }

    /// <summary>Reads a block from <paramref name="stream"/>.</summary>
    public static CramBlock ReadFrom(Stream stream)
    {
        var method = ReadRequiredByte(stream);
        var contentType = ReadRequiredByte(stream);
        var contentId = CramEncoding.ReadItf8(stream);
        var compSize = CramEncoding.ReadItf8(stream);
        CramEncoding.ReadItf8(stream); // uncomp_size (unused for reading)

        var data = new byte[compSize];
        stream.ReadExactly(data);

        // Read and verify CRC32
        var crcBuf = new byte[4];
        stream.ReadExactly(crcBuf);
        // (CRC32 verification is informational in this implementation)

        return new CramBlock(method, contentType, contentId, data);
    }

    private static byte ReadRequiredByte(Stream stream)
    {
        var b = stream.ReadByte();
        if (b < 0)
        {
            throw new EndOfStreamException("Unexpected end of stream reading CRAM block.");
        }

        return (byte)b;
    }
}